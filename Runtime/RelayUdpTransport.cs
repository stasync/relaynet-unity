using System;
using System.Net;
using Core.Networking.Udp;
using Core.Networking.Udp.LowLevel;
using Core.Networking.Udp.LowLevel.DataTransferLayers;

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

        public NetTransferStatistics Statistics =>
            new(_transferAmountCapture.BytesSent, _transferAmountCapture.BytesReceived,
                _transferAmountCapture.PacketsSent, _transferAmountCapture.PacketsReceived);

        private readonly ReliableUdpClient _client = new();

        /// <summary>
        /// The layer is ours, not the client's - it survives a disconnect (which drops every layer the client holds),
        /// so the counters keep the totals of the whole session and not of the last connection only.
        /// </summary>
        private readonly DataTransferAmountCaptureLayer _transferAmountCapture = new();

        public void Connect(IPEndPoint endPoint, string connectionData)
        {
            _client.Connect(endPoint, connectionData);

            // The protocol the layers live on is created by 'Connect' itself, so the capture can't be installed any earlier.
            _client.RegisterDataTransferLayer(_transferAmountCapture);
        }

        public void Send(ArraySegment<byte> data, UdpFullProtocol.DgramDeliveryMethod deliveryMethod) =>
            _client.Send(data, deliveryMethod);

        public void Update() =>
            _client.Update();

        public void Disconnect() =>
            _client.Disconnect();
    }
}