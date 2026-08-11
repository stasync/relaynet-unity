namespace LROTech.Network
{
    /// <summary>
    /// Raw traffic counters of an <see cref="IRelayTransport"/>, accumulated since it was created.
    /// Everything measured here is what actually goes over the wire - protocol overhead included, not just the payload
    /// the session above asked to send.
    /// </summary>
    public readonly struct NetTransferStatistics
    {
        public readonly int BytesSent;
        public readonly int BytesReceived;
        public readonly int PacketsSent;
        public readonly int PacketsReceived;

        public NetTransferStatistics(int bytesSent, int bytesReceived, int packetsSent, int packetsReceived)
        {
            BytesSent = bytesSent;
            BytesReceived = bytesReceived;
            PacketsSent = packetsSent;
            PacketsReceived = packetsReceived;
        }

        /// <summary>
        /// The traffic accumulated between the two measurements - the counters are cumulative, so a difference of two
        /// of them is what happened in between.
        /// </summary>
        public static NetTransferStatistics operator -(NetTransferStatistics value0, NetTransferStatistics value1) =>
            new(bytesSent: value0.BytesSent - value1.BytesSent,
                bytesReceived: value0.BytesReceived - value1.BytesReceived,
                packetsSent: value0.PacketsSent - value1.PacketsSent,
                packetsReceived: value0.PacketsReceived - value1.PacketsReceived);
    }
}