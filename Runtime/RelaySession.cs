using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using Keel.Networking;
using Keel.Networking.Udp;
using Keel.Networking.Udp.LowLevel;
using Keel.RelayNet.Compression;
using UnityEngine;

namespace Keel.RelayNet
{
    internal sealed class RelaySession
    {
        public delegate void DataReceivedDelegate(ClientUid senderConnectionId, uint userDataOpCode, byte[] data, DeliveryMethod deliveryMethod);

        public IReadOnlyCollection<ClientUid> ConnectedClients => _connectedClients;
        private readonly HashSet<ClientUid> _connectedClients = new();

        public event Action LocalUserDisconnected = delegate { };
        public event Action LocalUserConnected = delegate { };
        public event Action<ClientUid> RemoteUserConnected = delegate { };
        public event Action<ClientUid> RemoteUserDisconnected = delegate { };

        public event DataReceivedDelegate DataReceived = delegate { };

        public ClientUid ConnectionUid { get; private set; }

        public bool IsConnected => _transport.IsConnected;
        public NetTransferStatistics Statistics => _transport.Statistics;

        public NetPayloadStatistics PayloadStatistics =>
            new(_messagesSent, _messagesReceived, _payloadBytesSent, _payloadBytesReceived, _compressedBytesSent, _compressedBytesReceived);

        private readonly NetReader _dataReader = new();
        private readonly NetWriter _dataWriter = new();

        private readonly IRelayTransport _transport;
        private readonly INetCompressor _compressor;
        private bool _disposed;
        private float _lastUpdateTime;
        private float _currentTime;

        private int _messagesSent;
        private int _messagesReceived;
        private int _payloadBytesSent;
        private int _payloadBytesReceived;
        private int _compressedBytesSent;
        private int _compressedBytesReceived;

        public RelaySession(IRelayTransport transport, IPEndPoint endPoint, string connectionData = null, INetCompressor compressor = null)
        {
            _transport = transport;
            _compressor = compressor;

            if (_transport.IsConnected)
            {
                Debug.LogError("Already connecting or connected.");
                return;
            }

            _transport.Connected += OnConnected;
            _transport.ConnectionRejected += OnConnectionRejected;
            _transport.ConnectionFailed += OnConnectionFailed;
            _transport.Disconnected += OnDisconnected;
            _transport.ReliabilityFailure += OnReliabilityFailure;
            _transport.DataReceived += OnDataReceived;

            _transport.Connect(endPoint, connectionData);
        }

        public void Update(float deltaTime)
        {
            EnsureValid();
            _currentTime += deltaTime;

            // Aiming for 20 times per second.
            if (_currentTime - _lastUpdateTime < 0.05f)
                return;

            _lastUpdateTime = _currentTime;
            _transport.Update();
        }

        public void TryDispose()
        {
            if (_disposed)
                return;

            _transport.Disconnect();
            ConnectionUid = ClientUid.Invalid;
            LocalUserDisconnected();

            _disposed = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureValid()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        public void SendData(uint userDataOpCode, byte[] data, DeliveryMethod deliveryMethod)
        {
            EnsureValid();

            if (!ConnectionUid.IsValid)
            {
                Debug.LogError("Unable to send data over the disconnected client.");
                return;
            }

            _dataWriter.SeekZero();
            _dataWriter.WriteByte((byte)RelayClientMessageType.OnPacket);
            _dataWriter.WritePackedUInt32(userDataOpCode);
            _dataWriter.WritePackedUInt32(0); // Receivers count is 0 for now - userDataReceiversCount == 0 means that the message will be sent to everyone except sender.

            // Only the payload is compressed: the relay reads everything before it to route the packet.
            var payload = new ArraySegment<byte>(data ?? Array.Empty<byte>());
            if (_compressor == null)
                _dataWriter.WriteBytesAndSize(data);
            else
                _dataWriter.WriteBytesAndSize(payload = _compressor.Compress(payload));

            _messagesSent++;
            _payloadBytesSent += data?.Length ?? 0;
            _compressedBytesSent += payload.Count;

            _transport.Send(_dataWriter.AsArraySegment(), (UdpProtocol.DeliveryMethod)deliveryMethod);
        }

        private void OnDataReceived(byte[] rawData, UdpProtocol.DeliveryMethod deliveryMethod)
        {
            try
            {
                _dataReader.Replace(rawData);
                var type = (RelayServerMessageType)_dataReader.ReadByte();

                switch (type)
                {
                    case RelayServerMessageType.OnJoined:
                    {
                        var newConnectionId = _dataReader.ReadClientUid();
                        var connectionsCount = _dataReader.ReadPackedUInt32();
                        for (var i = 0; i < connectionsCount; i++)
                        {
                            var connectionId = _dataReader.ReadClientUid();
                            _connectedClients.Add(connectionId);
                        }

                        // Handle local connection.
                        if (newConnectionId == new ClientUid(_transport.ConnectionUid))
                        {
                            ConnectionUid = newConnectionId;
                            LocalUserConnected();
                        }
                        else
                            RemoteUserConnected(newConnectionId);

                        break;
                    }

                    case RelayServerMessageType.OnPacket:
                    {
                        var senderConnectionId = _dataReader.ReadClientUid();
                        var userDataOpCode = _dataReader.ReadPackedUInt32();

                        var receivedPayload = _dataReader.ReadBytesAndSizeNonAlloc();
                        var payload = _compressor == null ? receivedPayload : _compressor.Decompress(receivedPayload);
                        var userDataPayload = payload.Count == 0 ? Array.Empty<byte>() : payload.ToArray();

                        _messagesReceived++;
                        _payloadBytesReceived += payload.Count;
                        _compressedBytesReceived += receivedPayload.Count;

                        DataReceived(senderConnectionId, userDataOpCode, userDataPayload, (DeliveryMethod)deliveryMethod);
                        break;
                    }

                    case RelayServerMessageType.OnDisconnect:
                    {
                        var disconnectedConnectionId = _dataReader.ReadClientUid();

                        // Remove disconnected connection.
                        _connectedClients.Remove(disconnectedConnectionId);
                        RemoteUserDisconnected(disconnectedConnectionId);
                        break;
                    }

                    default:
                        Debug.LogError($"Unknown message type: '{type}'.");
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void OnConnected()
        {
            // Send auth request.
            _dataWriter.SeekZero();
            _dataWriter.WriteByte((byte)RelayClientMessageType.OnAuth);

            _transport.Send(_dataWriter.AsArraySegment(), UdpProtocol.DeliveryMethod.Reliable);
        }

        private void OnDisconnected() =>
            TryDispose();

        private void OnConnectionRejected(ReliableUdpClient.RejectionReason reason)
        {
            Debug.LogError($"{nameof(OnConnectionRejected)} with reason: '{reason}'.");
            TryDispose();
        }

        private void OnReliabilityFailure()
        {
            Debug.LogError(nameof(OnReliabilityFailure));
            TryDispose();
        }

        private void OnConnectionFailed()
        {
            Debug.LogError(nameof(OnConnectionFailed));
            TryDispose();
        }
    }
}