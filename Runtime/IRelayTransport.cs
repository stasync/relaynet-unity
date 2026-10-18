using System;
using System.Net;
using Keel.Networking.Udp;
using Keel.Networking.Udp.LowLevel;

namespace Keel.RelayNet
{
    /// <summary>
    /// A transport the <see cref="RelaySession"/> exchanges raw relay messages over.
    /// Mirrors the surface of the <see cref="Keel.Networking.Udp.ReliableUdpClient"/>, so the session doesn't know
    /// whether it talks to a real relay server or to an in-process one (see <see cref="RelayLoopbackTransport"/>).
    /// </summary>
    internal interface IRelayTransport
    {
        event Action Connected;
        event Action<ReliableUdpClient.RejectionReason> ConnectionRejected;
        event Action ConnectionFailed;
        event Action Disconnected;
        event Action ReliabilityFailure;
        event Action<byte[], UdpProtocol.DeliveryMethod> DataReceived;

        bool IsConnected { get; }
        uint ConnectionUid { get; }

        /// <summary>
        /// The traffic the transport has moved so far. Cumulative - never resets, not even on a reconnect.
        /// </summary>
        NetTransferStatistics Statistics { get; }

        void Connect(IPEndPoint endPoint, string connectionData);
        void Send(ArraySegment<byte> data, UdpProtocol.DeliveryMethod deliveryMethod);
        void Update();
        void Disconnect();
    }
}
