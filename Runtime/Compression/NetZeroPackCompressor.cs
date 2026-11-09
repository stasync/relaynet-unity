using System;
using System.IO;

namespace Keel.RelayNet.Compression
{
    /// <summary>
    /// Leaves out the zero bytes of a payload. Every 8 bytes become a tag byte, with a bit set for each byte that isn't
    /// zero, followed by those bytes. Network state is full of zeros: the high bytes of small integers, still velocities,
    /// unused rotation axes, false flags. A typical state update shrinks by a fifth to a third.
    /// Each payload is packed on its own, so the compressor keeps no state and a lost packet affects nothing else.
    /// </summary>
    public sealed class NetZeroPackCompressor : INetCompressor
    {
        private const int BLOCK_SIZE = 8;
        private const int MAX_VARINT_SIZE = 5;

        /// <summary>
        /// Payloads travel with a 16-bit size, so nothing longer can legitimately be restored.
        /// </summary>
        private const int MAX_PAYLOAD_SIZE = ushort.MaxValue;

        private byte[] _compressed = new byte[256];
        private byte[] _decompressed = new byte[256];

        public ArraySegment<byte> Compress(ArraySegment<byte> data)
        {
            // At worst, nothing is zero: the length, then a tag byte for every block on top of the bytes themselves.
            EnsureSize(ref _compressed, MAX_VARINT_SIZE + data.Count + (data.Count + BLOCK_SIZE - 1) / BLOCK_SIZE);

            var source = data.Array;
            var end = data.Offset + data.Count;
            var output = _compressed;
            var position = WriteVarUInt(output, 0, (uint)data.Count);

            for (var block = data.Offset; block < end; block += BLOCK_SIZE)
            {
                var tagPosition = position++;
                var tag = 0;

                var blockEnd = Math.Min(block + BLOCK_SIZE, end);
                for (var i = block; i < blockEnd; i++)
                {
                    if (source[i] == 0)
                        continue;

                    tag |= 1 << (i - block);
                    output[position++] = source[i];
                }

                output[tagPosition] = (byte)tag;
            }

            return new ArraySegment<byte>(output, 0, position);
        }

        public ArraySegment<byte> Decompress(ArraySegment<byte> data)
        {
            var source = data.Array;
            var position = data.Offset;
            var end = data.Offset + data.Count;

            var length = ReadVarUInt(source, ref position, end);
            EnsureSize(ref _decompressed, length);
            var output = _decompressed;

            for (var block = 0; block < length; block += BLOCK_SIZE)
            {
                if (position >= end)
                    throw new InvalidDataException("A packed payload ends before its last block.");

                var tag = source[position++];

                var blockEnd = Math.Min(block + BLOCK_SIZE, length);
                for (var i = block; i < blockEnd; i++)
                {
                    if ((tag & (1 << (i - block))) == 0)
                    {
                        output[i] = 0;
                        continue;
                    }

                    if (position >= end)
                        throw new InvalidDataException("A packed payload ends in the middle of a block.");

                    output[i] = source[position++];
                }
            }

            // Leftover bytes mean the sender didn't pack this payload - most likely, it doesn't use the same compressor.
            if (position != end)
                throw new InvalidDataException("A packed payload has bytes past its end. Does every client in the room use the same compressor?");

            return new ArraySegment<byte>(output, 0, length);
        }

        private static int WriteVarUInt(byte[] buffer, int position, uint value)
        {
            while (value >= 0x80)
            {
                buffer[position++] = (byte)(value | 0x80);
                value >>= 7;
            }

            buffer[position++] = (byte)value;
            return position;
        }

        private static int ReadVarUInt(byte[] buffer, ref int position, int end)
        {
            var value = 0u;
            for (var shift = 0; shift < 7 * MAX_VARINT_SIZE; shift += 7)
            {
                if (position >= end)
                    throw new InvalidDataException("A packed payload ends in the middle of its length.");

                var next = buffer[position++];
                value |= (uint)(next & 0x7F) << shift;
                if (next >= 0x80)
                    continue;

                if (value > MAX_PAYLOAD_SIZE)
                    throw new InvalidDataException($"A packed payload claims {value} bytes, more than a payload can hold.");

                return (int)value;
            }

            throw new InvalidDataException("A packed payload's length runs past 5 bytes.");
        }

        private static void EnsureSize(ref byte[] buffer, int size)
        {
            if (buffer.Length < size)
                buffer = new byte[Math.Max(size, buffer.Length * 2)];
        }
    }
}
