using System;
using System.Net;
using Core.Networking;
using LROTech.Network.StateManagement;

namespace LROTech.Network.Sessions
{
    public sealed class NetSession
    {
        public event Action Disposed = delegate { };

        public NetWorldRuntime MainWorld { get; private set; }

        private RelaySession _clientSession;
        private readonly NetReader _reader = new();

        public NetSession(IPAddress address, ushort port)
        {
            if (_clientSession != null)
                throw new InvalidOperationException("Session is already initialized.");

            MainWorld = new NetWorldRuntime();
            MainWorld.DataSendRequested += OnWorldDataSendRequest;

            _clientSession = new RelaySession(new IPEndPoint(address, port), connectionKey: 0);
            _clientSession.Disposed += OnSessionDisposed;
            _clientSession.DataReceived += OnDataReceived;
            _clientSession.LocalUserConnected += OnLocalUserConnected;
            _clientSession.RemoteUserDisconnected += OnRemoteUserDisconnected;
            _clientSession.DataReceived += OnDataReceived;
        }
        
        public void Update(float deltaTime)
        {
            _clientSession?.Update(deltaTime);
            MainWorld?.Update(deltaTime);
        }

        public void Dispose()
        {
            if (_clientSession != null)
            {
                _clientSession.TryDispose();

                _clientSession.Disposed -= OnSessionDisposed;
                _clientSession.DataReceived -= OnDataReceived;
                _clientSession.LocalUserConnected -= OnLocalUserConnected;
                _clientSession.RemoteUserDisconnected -= OnRemoteUserDisconnected;
                _clientSession.DataReceived -= OnDataReceived;
            }
            
            _clientSession = null;
        }
        
        private void OnSessionDisposed()
        {
            MainWorld?.Dispose();
            MainWorld = null;
            
            Disposed();
        }

        private void OnDataReceived(ClientUid senderConnectionId, uint userDataOpCode, byte[] data, DeliveryMethod deliveryMethod)
        {
            //DataReceived(senderConnectionUid, userDataOpCode, data, deliveryMethod);

            switch (userDataOpCode)
            {
                case MessageCodes.NET_WORLD_UPDATE:
                {
                    MainWorld.ScheduleRemoteUpdate(data);
                    break;
                }

                case MessageCodes.CUSTOM_MESSAGE:
                {
                    var messageCode = _reader.ReadInt32();
                    var messageData = _reader.ReadBytesAndSize();
                    //CustomMessageReceived(senderConnectionUid, messageCode, messageData);

                    break;
                }
            }
        }
        
        private void OnLocalUserConnected() =>
            MainWorld.RegisterLocalPlayer(_clientSession.ConnectionUid);
        
        private void OnRemoteUserDisconnected(ClientUid clientUid) =>
            MainWorld.OnDisconnect(clientUid);

        private void OnWorldDataSendRequest(byte[] data, DeliveryMethod deliveryMethod) =>
            _clientSession.SendData(MessageCodes.NET_WORLD_UPDATE, data, deliveryMethod);
    }
}