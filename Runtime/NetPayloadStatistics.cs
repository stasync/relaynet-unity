namespace Keel.RelayNet
{
    /// <summary>
    /// The messages a <see cref="NetSession"/> exchanged with the room, accumulated since it was created.
    /// <see cref="NetTransferStatistics"/> counts what goes over the wire; this counts the payloads inside the packets,
    /// both as the game produced them and as they were sent after compression. The difference is what the compressor
    /// saves. Without a compressor, the two are equal.
    /// </summary>
    public readonly struct NetPayloadStatistics
    {
        public readonly int MessagesSent;
        public readonly int MessagesReceived;

        /// <summary>
        /// The payload bytes before compression on the way out, and after decompression on the way in.
        /// </summary>
        public readonly int BytesSent;
        public readonly int BytesReceived;

        /// <summary>
        /// The same payloads as they travelled: after compression on the way out, before decompression on the way in.
        /// </summary>
        public readonly int CompressedBytesSent;
        public readonly int CompressedBytesReceived;

        public NetPayloadStatistics(int messagesSent, int messagesReceived, int bytesSent, int bytesReceived, int compressedBytesSent, int compressedBytesReceived)
        {
            MessagesSent = messagesSent;
            MessagesReceived = messagesReceived;
            BytesSent = bytesSent;
            BytesReceived = bytesReceived;
            CompressedBytesSent = compressedBytesSent;
            CompressedBytesReceived = compressedBytesReceived;
        }

        /// <summary>
        /// The messages exchanged between the two measurements - the counters are cumulative, so a difference of two
        /// of them is what happened in between.
        /// </summary>
        public static NetPayloadStatistics operator -(NetPayloadStatistics value0, NetPayloadStatistics value1) =>
            new(messagesSent: value0.MessagesSent - value1.MessagesSent,
                messagesReceived: value0.MessagesReceived - value1.MessagesReceived,
                bytesSent: value0.BytesSent - value1.BytesSent,
                bytesReceived: value0.BytesReceived - value1.BytesReceived,
                compressedBytesSent: value0.CompressedBytesSent - value1.CompressedBytesSent,
                compressedBytesReceived: value0.CompressedBytesReceived - value1.CompressedBytesReceived);
    }
}
