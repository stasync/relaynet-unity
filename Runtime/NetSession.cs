using System;
using System.Net;
using LROTech.Network.StateManagement;

namespace LROTech.Network
{
    public sealed class NetSession
    {
        public event Action Disposed = delegate { };

        public ClientUid SessionOwner { get; private set; }
        public NetWorldRuntime MainWorld { get; private set; }

        private RelaySession _clientSession;
        //private readonly NetReader _reader = new();

        public NetSession(IPAddress address, ushort port)
        {
            if (_clientSession != null)
                throw new InvalidOperationException("Session is already initialized.");

            MainWorld = new NetWorldRuntime(sessionOwnerProvider: () => SessionOwner);
            MainWorld.DataSendRequested += OnWorldDataSendRequest;
            MainWorld.Disposed += OnWorldDisposed;

            _clientSession = new RelaySession(new IPEndPoint(address, port), connectionKey: 0);
            _clientSession.LocalUserDisconnected += OnLocalUserDisconnected;
            _clientSession.DataReceived += OnDataReceived;
            _clientSession.LocalUserConnected += OnLocalUserConnected;
            _clientSession.RemoteUserDisconnected += OnRemoteUserDisconnected;
            _clientSession.RemoteUserConnected += OnRemoteUserConnected;
        }

        private void OnWorldDisposed()
        {
            if (MainWorld != null)
            {
                MainWorld.DataSendRequested -= OnWorldDataSendRequest;
                MainWorld.Disposed -= OnWorldDisposed;
            }

            MainWorld = null;

            if (_clientSession != null)
            {
                _clientSession.TryDispose();

                _clientSession.LocalUserDisconnected -= OnLocalUserDisconnected;
                _clientSession.DataReceived -= OnDataReceived;
                _clientSession.LocalUserConnected -= OnLocalUserConnected;
                _clientSession.RemoteUserDisconnected -= OnRemoteUserDisconnected;
                _clientSession.RemoteUserConnected -= OnRemoteUserConnected;
            }

            _clientSession = null;
            Disposed();
        }

        public void Update(float deltaTime)
        {
            _clientSession?.Update(deltaTime);
            MainWorld?.Update(deltaTime);
        }

        public void Dispose() =>
            MainWorld?.Dispose();

        public void SendCustomMessage(byte[] data, DeliveryMethod deliveryMethod)
        {
            _clientSession.SendData(MessageCodes.CUSTOM_MESSAGE, data, deliveryMethod);
        }

        private void OnLocalUserDisconnected() =>
            MainWorld?.Dispose();

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
                    //var messageCode = _reader.ReadInt32();
                    //var messageData = _reader.ReadBytesAndSize();
                    //CustomMessageReceived(senderConnectionUid, messageCode, messageData);

                    break;
                }
            }
        }
        
        private void RecalculateMasterPlayerUid()
        {
            SessionOwner = ClientUid.Invalid;
            foreach (var clientUid in _clientSession.ConnectedClients)
            {
                if (clientUid < SessionOwner || !SessionOwner.IsValid)
                    SessionOwner = clientUid;
            }
        }

        private void OnLocalUserConnected()
        {
            RecalculateMasterPlayerUid();
            MainWorld.RegisterLocalPlayer(_clientSession.ConnectionUid);
        }

        private void OnRemoteUserDisconnected(ClientUid clientUid)
        {
            RecalculateMasterPlayerUid();
            MainWorld.OnDisconnect(clientUid);
        }

        private void OnRemoteUserConnected(ClientUid clientUid) =>
            RecalculateMasterPlayerUid();

        private void OnWorldDataSendRequest(byte[] data, DeliveryMethod deliveryMethod) =>
            _clientSession.SendData(MessageCodes.NET_WORLD_UPDATE, data, deliveryMethod);
    }
}