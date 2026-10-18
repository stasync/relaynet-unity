using System;
using System.Collections.Generic;
using System.Net;
using Keel.RelayNet.StateManagement;

namespace Keel.RelayNet
{
    public sealed class NetSession
    {
        /// <summary>
        /// Every session alive at the moment, in creation order. Kept for the debug tooling - there is no other way to
        /// reach a session from the outside, whoever creates a session holds its own reference.
        /// </summary>
        public static IReadOnlyList<NetSession> All => s_all;
        private static readonly List<NetSession> s_all = new();

        public event Action Disposed = delegate { };

        public ClientUid SessionOwner { get; private set; }
        public NetWorldRuntime MainWorld { get; private set; }

        public bool IsConnected => _clientSession is { IsConnected: true };

        /// <summary>
        /// The traffic of the underlying transport, or an empty one for an already disposed session.
        /// </summary>
        public NetTransferStatistics Statistics => _clientSession?.Statistics ?? default;

        private RelaySession _clientSession;

        public NetSession(IPAddress address, ushort port, string connectionData = null)
            : this(new RelayUdpTransport(), new IPEndPoint(address, port), connectionData)
        {
        }

        /// <summary>
        /// Creates an offline session - the relay server is emulated in-process, so the local client is the only
        /// (and therefore the master) client of the room. Everything above this point behaves exactly as online.
        /// </summary>
        public static NetSession CreateOffline() =>
            new(new RelayLoopbackTransport(), new IPEndPoint(IPAddress.Loopback, port: 0), connectionData: null);

        private NetSession(IRelayTransport transport, IPEndPoint endPoint, string connectionData)
        {
            if (_clientSession != null)
                throw new InvalidOperationException("Session is already initialized.");

            MainWorld = new NetWorldRuntime(masterClientUidProvider: () => SessionOwner);
            MainWorld.DataSendRequested += OnWorldDataSendRequest;
            MainWorld.Disposed += OnWorldDisposed;

            _clientSession = new RelaySession(transport, endPoint, connectionData);
            _clientSession.LocalUserDisconnected += OnLocalUserDisconnected;
            _clientSession.DataReceived += OnDataReceived;
            _clientSession.LocalUserConnected += OnLocalUserConnected;
            _clientSession.RemoteUserDisconnected += OnRemoteUserDisconnected;
            _clientSession.RemoteUserConnected += OnRemoteUserConnected;

            s_all.Add(this);
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
            s_all.Remove(this);

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
        
        private void RecalculateSessionOwner()
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
            RecalculateSessionOwner();
            MainWorld.RegisterLocalPlayer(_clientSession.ConnectionUid);
        }

        private void OnRemoteUserDisconnected(ClientUid clientUid)
        {
            RecalculateSessionOwner();
            MainWorld.OnDisconnect(clientUid);
        }

        private void OnRemoteUserConnected(ClientUid clientUid) =>
            RecalculateSessionOwner();

        private void OnWorldDataSendRequest(byte[] data, DeliveryMethod deliveryMethod) =>
            _clientSession.SendData(MessageCodes.NET_WORLD_UPDATE, data, deliveryMethod);
    }
}