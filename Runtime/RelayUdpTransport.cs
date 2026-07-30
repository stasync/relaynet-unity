using System;
using System.Net;
using Core.Networking.Udp;
using Core.Networking.Udp.LowLevel;

namespace LROTech.Network
{
    /// <summary>
    /// The real transport - a thin wrapper over the <see cref="ReliableUdpClient"/>.
    /// </summary>
    internal sealed class RelayUdpTransport : IRelayTransport
    {
        public event Action Connected
        {
            add => _client.Connected += value;
            remove => _client.Connected -= value;
        }

        public event Action<ReliableUdpClient.RejectionReason> ConnectionRejected
        {
            add => _client.ConnectionRejected += value;
            remove => _client.ConnectionRejected -= value;
        }

        public event Action ConnectionFailed
        {
            add => _client.ConnectionFailed += value;
            remove => _client.ConnectionFailed -= value;
        }

        public event Action Disconnected
        {
            add => _client.Disconnected += value;
            remove => _client.Disconnected -= value;
        }

        public event Action ReliabilityFailure
        {
            add => _client.ReliabilityFailure += value;
            remove => _client.ReliabilityFailure -= value;
        }

        public event Action<byte[], UdpFullProtocol.DgramDeliveryMethod> DataReceived
        {
            add => _client.DataReceived += value;
            remove => _client.DataReceived -= value;
        }

        public bool IsConnected => _client.IsConnected;
        public uint ConnectionUid => _client.ConnectionUid;

        private readonly ReliableUdpClient _client = new();

        public void Connect(IPEndPoint endPoint, string connectionData) =>
            _client.Connect(endPoint, connectionData);

        public void Send(ArraySegment<byte> data, UdpFullProtocol.DgramDeliveryMethod deliveryMethod) =>
            _client.Send(data, deliveryMethod);

        public void Update() =>
            _client.Update();

        public void Disconnect() =>
            _client.Disconnect();
    }
}
