using System;
using System.Collections.Generic;
using System.Net;
using Core.Networking;
using Core.Networking.Udp;
using Core.Networking.Udp.LowLevel;
using UnityEngine;

namespace LROTech.Network.Sessions
{
    [DisallowMultipleComponent]
    internal sealed class InternalClientSession : MonoBehaviour
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

        public event Action<ClientUid> RemoteUserConnected = delegate { };
        public event Action<ClientUid> RemoteUserDisconnected = delegate { };
    
        public event Action LocalUserConnected = delegate { };
        public event Action LocalUserDisconnected = delegate { };
    
        public event DataReceivedDelegate DataReceived = delegate { };

        public bool IsConnected => ConnectionUid.IsValid;
        public bool IsMasterClient => ConnectionUid.IsValid && ConnectionUid == MasterConnectionUid;
        public ClientUid ConnectionUid { get; private set; }
        public ClientUid MasterConnectionUid { get; private set; }
    
        private readonly NetReader _dataReader = new();
        private readonly NetWriter _dataWriter = new();
    
        private readonly ReliableUdpClient _reliableUdpClient = new();
        private bool _disposed;
        private float _lastUpdateTime;
    
        public void Connect(IPEndPoint endPoint, long connectionKey = 0)
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

        private void OnHeartbeatSent()
        {
        }

        public void SendData(uint userDataOpCode, byte[] data, DeliveryMethod deliveryMethod)
        {
            if (!IsConnected)
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
            _dataWriter.SeekZero();
            _dataWriter.WriteBytesAndSize(data);
            _reliableUdpClient.Send(_dataWriter.AsArraySegment(), (UdpFullProtocol.DgramDeliveryMethod)deliveryMethod);
        }
    
        public void TryDispose()
        {
            if (_disposed)
                return;
        
            _disposed = true;
            Destroy(gameObject);
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
                            RecalculateMasterConnectionUid();
                        }
                        
                        // Handle local connection.
                        if (newConnectionId == _reliableUdpClient.ConnectionUid)
                        {
                            ConnectionUid = newConnectionId;
                            LocalUserConnected();
                        }
                        else
                            RemoteUserConnected(newConnectionId);
                    }
                        break;

                    case ServerMessageType.OnPacket:
                    {
                        var senderConnectionId = _dataReader.ReadClientUid();
                        var userDataOpCode = _dataReader.ReadPackedUInt32();
                        var userDataPayload = _dataReader.ReadBytesAndSize();
                        
                        DataReceived(senderConnectionId, userDataOpCode, userDataPayload, (DeliveryMethod)deliveryMethod);
                    }
                        break;
                
                    case ServerMessageType.OnDisconnect:
                    {
                        var disconnectedConnectionId = _dataReader.ReadClientUid();
                        
                        // Remove disconnected connection.
                        _connectedClients.Remove(disconnectedConnectionId);
                        RecalculateMasterConnectionUid();
                        
                        RemoteUserDisconnected(disconnectedConnectionId);
                    }
                        break;

                    default:
                        Debug.LogError($"Unknown message type: '{type}'.");
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            return;

            void RecalculateMasterConnectionUid()
            {
                MasterConnectionUid = ClientUid.Invalid;
                foreach (var clientUid in _connectedClients)
                {
                    if (clientUid < MasterConnectionUid || !MasterConnectionUid.IsValid) 
                        MasterConnectionUid = clientUid;
                }
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
    
        private void Update()
        {
            // Aiming for 20 times per second.
            if (Time.unscaledTime - _lastUpdateTime < 0.05f)
                return;
        
            _lastUpdateTime = Time.unscaledTime;
            _reliableUdpClient.Update();
        }

        private void OnDestroy()
        {
            _reliableUdpClient.Disconnect();
            ConnectionUid = ClientUid.Invalid;
            MasterConnectionUid = ClientUid.Invalid;
            LocalUserDisconnected();
        }
    }
}
