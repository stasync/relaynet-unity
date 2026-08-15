using System;
using System.Collections.Generic;
using System.Net;
using Core.Networking;
using Core.Networking.Udp;
using Core.Networking.Udp.LowLevel;

namespace LROTech.Network
{
    /// <summary>
    /// The offline transport - a relay server running in-process, hosting a room with a single (local) connection.
    /// It speaks the very same wire protocol as the real relay (see 'rt_server' - RelayServer.cs), so the
    /// <see cref="RelaySession"/> above it can't tell the difference: the local client authorizes, receives its
    /// 'OnJoined' message back, and every packet it sends afterwards is routed to everyone except the sender -
    /// which, in a room of one, is nobody.
    /// </summary>
    internal sealed class RelayLoopbackTransport : IRelayTransport
    {
        /// <summary>
        /// The uid the "server" assigns to the only connection of the room. Any valid (non-zero) value works.
        /// </summary>
        private const uint LOCAL_CONNECTION_UID = 1u;

        public event Action Connected = delegate { };
        public event Action<ReliableUdpClient.RejectionReason> ConnectionRejected = delegate { };
        public event Action ConnectionFailed = delegate { };
        public event Action Disconnected = delegate { };
        public event Action ReliabilityFailure = delegate { };
        public event Action<byte[], UdpFullProtocol.DgramDeliveryMethod> DataReceived = delegate { };

        public bool IsConnected { get; private set; }
        public uint ConnectionUid { get; private set; }

        /// <summary>
        /// There is no wire here, so the counters measure the messages as the session handed them over / got them back -
        /// no protocol overhead is added on top.
        /// </summary>
        public NetTransferStatistics Statistics =>
            new(_bytesSent, _bytesReceived, _packetsSent, _packetsReceived);

        private readonly Queue<(byte[] Data, UdpFullProtocol.DgramDeliveryMethod DeliveryMethod)> _incoming = new();
        private readonly HashSet<uint> _connections = new();
        private readonly NetReader _dataReader = new();
        private readonly NetWriter _dataWriter = new();

        private bool _connectionRequested;

        private int _bytesSent;
        private int _bytesReceived;
        private int _packetsSent;
        private int _packetsReceived;

        // Connections are never confirmed immediately - the confirmation arrives with the next update, same as a real round trip.
        public void Connect(IPEndPoint endPoint, string connectionData) =>
            _connectionRequested = true;

        public void Update()
        {
            // Only what the "server" had queued before this update is delivered now: a packet can't arrive within the
            // same tick it was sent, so anything the handlers below produce waits for the next one - just like a round trip.
            var messagesToDeliver = _incoming.Count;

            if (_connectionRequested)
            {
                _connectionRequested = false;
                ConnectionUid = LOCAL_CONNECTION_UID;
                IsConnected = true;
                Connected();
            }

            for (var i = 0; i < messagesToDeliver; i++)
            {
                var (data, deliveryMethod) = _incoming.Dequeue();

                _bytesReceived += data.Length;
                _packetsReceived++;

                DataReceived(data, deliveryMethod);
            }
        }

        public void Send(ArraySegment<byte> data, UdpFullProtocol.DgramDeliveryMethod deliveryMethod)
        {
            if (!IsConnected)
                return;

            _bytesSent += data.Count;
            _packetsSent++;

            _dataReader.Replace(ToArray(data));
            var type = (RelayClientMessageType)_dataReader.ReadByte();

            switch (type)
            {
                case RelayClientMessageType.OnAuth:
                {
                    if (!_connections.Add(ConnectionUid))
                        break;

                    // The join message is sent back to all the connections of the room, including the sender.
                    _dataWriter.SeekZero();
                    _dataWriter.WriteByte((byte)RelayServerMessageType.OnJoined);
                    _dataWriter.WritePackedUInt32(ConnectionUid);
                    _dataWriter.WritePackedUInt32((uint)_connections.Count);
                    foreach (var existedConnection in _connections)
                        _dataWriter.WritePackedUInt32(existedConnection);

                    Enqueue(UdpFullProtocol.DgramDeliveryMethod.ReliableOrdered);
                    break;
                }

                case RelayClientMessageType.OnPacket:
                {
                    // Nothing to route: the packet goes to the explicitly listed receivers, or to everyone except the sender - and there is no one else here.
                    break;
                }
            }
        }

        public void Disconnect()
        {
            if (!IsConnected)
                return;

            IsConnected = false;
            ConnectionUid = 0u;
            _connections.Clear();
            _incoming.Clear();
        }

        private void Enqueue(UdpFullProtocol.DgramDeliveryMethod deliveryMethod) =>
            _incoming.Enqueue((_dataWriter.ToArray(), deliveryMethod));

        private static byte[] ToArray(ArraySegment<byte> data)
        {
            var array = new byte[data.Count];
            Array.Copy(data.Array!, data.Offset, array, destinationIndex: 0, data.Count);
            return array;
        }
    }
}
