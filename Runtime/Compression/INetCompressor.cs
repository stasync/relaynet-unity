using System;

namespace Keel.RelayNet.Compression
{
    /// <summary>
    /// Compresses the payloads a <see cref="NetSession"/> sends and restores the ones it receives. Only the payload is
    /// compressed: the relay reads the envelope around it to route the packet, and passes the payload on untouched.
    /// Every client in a room must use the same compressor - a client without one, or with a different one, misreads
    /// the others' packets.
    /// </summary>
    public interface INetCompressor
    {
        /// <summary>
        /// Compresses a payload. The result may point into the compressor's own buffer: it only has to stay valid until
        /// the next call.
        /// </summary>
        ArraySegment<byte> Compress(ArraySegment<byte> data);

        /// <summary>
        /// Restores a payload that <see cref="Compress"/> produced. Throws on malformed data: the session logs the
        /// exception and drops the payload. The result may point into the compressor's own buffer: it only has to stay
        /// valid until the next call.
        /// </summary>
        ArraySegment<byte> Decompress(ArraySegment<byte> data);
    }
}
