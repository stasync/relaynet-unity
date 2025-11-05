using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using Core.Networking;
using Core.Networking.Udp;
using Core.Networking.Udp.LowLevel;
using UnityEngine;

namespace LROTech.Network
{
    internal sealed class RelaySession
    {
        public delegate void DataReceivedDelegate(ClientUid senderConnectionId, uint userDataOpCode, byte[] data, DeliveryMethod deliveryMethod);

        private enum ClientMessageType : byte
        {
            OnAuth,
            OnPacket,
        }

        private enum ServerMessageType : byte
        {
            OnJoined,
            OnPacket,
            OnDisconnect,
        }

        public IReadOnlyCollection<ClientUid> ConnectedClients => _connectedClients;
        private readonly HashSet<ClientUid> _connectedClients = new();

        public event Action LocalUserDisconnected = delegate { };
        public event Action LocalUserConnected = delegate { };
        public event Action<ClientUid> RemoteUserConnected = delegate { };
        public event Action<ClientUid> RemoteUserDisconnected = delegate { };
        
        public event DataReceivedDelegate DataReceived = delegate { };

        public ClientUid ConnectionUid { get; private set; }

        private readonly NetReader _dataReader = new();
        private readonly NetWriter _dataWriter = new();

        private readonly ReliableUdpClient _reliableUdpClient = new();
        private bool _disposed;
        private float _lastUpdateTime;
        private float _currentTime;

        public RelaySession(IPEndPoint endPoint, long connectionKey = 0)
        {
            if (_reliableUdpClient.IsConnected)
            {
                Debug.LogError("Already connecting or connected.");
                return;
            }

            _reliableUdpClient.Connected += OnConnected;
            _reliableUdpClient.ConnectionRejected += OnConnectionRejected;
            _reliableUdpClient.ConnectionFailed += OnConnectionFailed;
            _reliableUdpClient.Disconnected += OnDisconnected;
            _reliableUdpClient.ReliabilityFailure += OnReliabilityFailure;
            _reliableUdpClient.DataReceived += OnDataReceived;
            _reliableUdpClient.HeartbeatSent += OnHeartbeatSent;

            _reliableUdpClient.Connect(endPoint, connectionKey);
        }

        public void Update(float deltaTime)
        {
            EnsureValid();
            _currentTime += deltaTime;

            // Aiming for 20 times per second.
            if (_currentTime - _lastUpdateTime < 0.05f)
                return;

            _lastUpdateTime = _currentTime;
            _reliableUdpClient.Update();
        }

        public void TryDispose()
        {
            if (_disposed)
                return;

            _reliableUdpClient.Disconnect();
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

        private void OnHeartbeatSent()
        {
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
            _dataWriter.WriteByte((byte)ClientMessageType.OnPacket);
            _dataWriter.WritePackedUInt32(userDataOpCode);
            _dataWriter.WritePackedUInt32(0); // Receivers count is 0 for now - userDataReceiversCount == 0 means that message will be sent to everyone except sender.
            _dataWriter.WriteBytesAndSize(data);

            _reliableUdpClient.Send(_dataWriter.AsArraySegment(), (UdpFullProtocol.DgramDeliveryMethod)deliveryMethod);
        }

        public void SendRaw(byte[] data, DeliveryMethod deliveryMethod)
        {
            EnsureValid();

            _dataWriter.SeekZero();
            _dataWriter.WriteBytesAndSize(data);
            _reliableUdpClient.Send(_dataWriter.AsArraySegment(), (UdpFullProtocol.DgramDeliveryMethod)deliveryMethod);
        }

        private void OnDataReceived(byte[] rawData, UdpFullProtocol.DgramDeliveryMethod deliveryMethod)
        {
            try
            {
                _dataReader.Replace(rawData);
                var type = (ServerMessageType)_dataReader.ReadByte();

                switch (type)
                {
                    case ServerMessageType.OnJoined:
                    {
                        var newConnectionId = _dataReader.ReadClientUid();
                        var connectionsCount = _dataReader.ReadPackedUInt32();
                        for (var i = 0; i < connectionsCount; i++)
                        {
                            var connectionId = _dataReader.ReadClientUid();
                            _connectedClients.Add(connectionId);
                        }

                        // Handle local connection.
                        if (newConnectionId == new ClientUid(_reliableUdpClient.ConnectionUid))
                        {
                            ConnectionUid = newConnectionId;
                            LocalUserConnected();
                        }
                        else
                            RemoteUserConnected(newConnectionId);

                        break;
                    }

                    case ServerMessageType.OnPacket:
                    {
                        var senderConnectionId = _dataReader.ReadClientUid();
                        var userDataOpCode = _dataReader.ReadPackedUInt32();
                        var userDataPayload = _dataReader.ReadBytesAndSize();

                        DataReceived(senderConnectionId, userDataOpCode, userDataPayload, (DeliveryMethod)deliveryMethod);
                        break;
                    }

                    case ServerMessageType.OnDisconnect:
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
            Debug.Log(nameof(OnConnected));

            // Send auth request.
            _dataWriter.SeekZero();
            _dataWriter.WriteByte((byte)ClientMessageType.OnAuth);

            _reliableUdpClient.Send(_dataWriter.AsArraySegment(), UdpFullProtocol.DgramDeliveryMethod.Reliable);
        }

        private void OnDisconnected()
        {
            Debug.Log(nameof(OnDisconnected));
            TryDispose();
        }

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