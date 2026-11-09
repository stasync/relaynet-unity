# Compression

A session can compress the payloads it sends: the world updates, player states and RPCs that the other clients receive. Compression is off by default. This page shows how to turn it on, how the built-in compressor works, and how to write your own.

- [Turn it on](#turn-it-on)
- [How NetZeroPackCompressor works](#how-netzeropackcompressor-works)
- [Write your own compressor](#write-your-own-compressor)
- [Measure what a compressor saves](#measure-what-a-compressor-saves)
- [Delta compression](#delta-compression)

## Turn it on

Pass a compressor when you create the session:

```csharp
using Keel.RelayNet.Compression;

var session = new NetSession(address, port, token, compressor: new NetZeroPackCompressor());
```

Every client in the room must use the same compressor. A client without one, or with a different one, misreads the others' packets, and nothing checks for it yet (see the [roadmap](roadmap.md#5-correctness-and-robustness)). Without a compressor, the wire format is the same as before compression existed.

Only the payload of each packet is compressed. The relay reads the rest, such as the message type and the receivers, to route the packet, so that part stays as it is and the relay needs no changes.

## How NetZeroPackCompressor works

`NetZeroPackCompressor` leaves out zero bytes. It splits a payload into blocks of 8 bytes, and writes each block as a tag byte, with a bit set for every byte that isn't zero, followed by those bytes. A length goes in front, so the receiver knows where the last block ends.

For example, an `int` of 1 followed by a `float` of 1.0 takes 8 bytes:

```
01 00 00 00 00 00 80 3F   ->   C1 01 80 3F
```

Bytes 0, 6 and 7 aren't zero, so the tag is `C1` (bits 0, 6 and 7), and only those three bytes follow it. A block of zeros becomes one byte. A block with no zeros grows by one, so the worst case is 1 byte in 8, plus the length.

State updates are full of zeros: the high bytes of small numbers and ids, the velocity of an object that doesn't move, the unused axes of a rotation, flags that are false. On payloads modeled on transform-synced objects, it saved:

| Payload | Before | After | Saved |
|---|---|---|---|
| An object that doesn't move | 72 bytes | 47 bytes | 35% |
| A walking character | 92 bytes | 73 bytes | 21% |
| A physics body | 72 bytes | 59 bytes | 18% |
| A player's state | 61 bytes | 50 bytes | 18% |
| A mix of 20 still objects, 8 characters and 2 physics bodies | 75 bytes | 54 bytes | 29% |

The packet headers around the payload aren't compressed, so the saving on the wire is a little smaller.

Each payload is packed on its own. The compressor keeps no state, so a lost or late packet affects nothing else. Packing and unpacking an update takes one to two microseconds.

## Write your own compressor

Implement `INetCompressor`:

```csharp
public interface INetCompressor
{
    ArraySegment<byte> Compress(ArraySegment<byte> data);
    ArraySegment<byte> Decompress(ArraySegment<byte> data);
}
```

- `Decompress` gets exactly what `Compress` returned on the sending client.
- Either method may return a segment of a buffer it owns and reuses. The session copies the result right away, so it only has to stay valid until the next call. Reusing a buffer keeps the session from allocating.
- Throw when `Decompress` gets data it can't read. The session logs the exception and drops the payload.
- Treat each payload on its own. Packets get lost and arrive out of order, and `Decompress` isn't told who sent the payload, so a compressor can't refer back to an earlier one.
- A compressed payload must fit in 65,535 bytes, like an uncompressed one.

For example, this compressor uses Deflate from `System.IO.Compression`. On RelayNet's small state updates, Deflate saves less than `NetZeroPackCompressor` (14–21% on the payloads above), but it does better on large payloads, such as long strings:

```csharp
using System;
using System.IO;
using System.IO.Compression;
using Keel.RelayNet.Compression;

public sealed class DeflateCompressor : INetCompressor
{
    private readonly MemoryStream _compressed = new();
    private readonly MemoryStream _decompressed = new();

    public ArraySegment<byte> Compress(ArraySegment<byte> data)
    {
        _compressed.SetLength(0);
        using (var deflate = new DeflateStream(_compressed, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(data.Array, data.Offset, data.Count);

        return new ArraySegment<byte>(_compressed.GetBuffer(), 0, (int)_compressed.Length);
    }

    public ArraySegment<byte> Decompress(ArraySegment<byte> data)
    {
        _decompressed.SetLength(0);
        using (var deflate = new DeflateStream(new MemoryStream(data.Array, data.Offset, data.Count), CompressionMode.Decompress))
            deflate.CopyTo(_decompressed);

        return new ArraySegment<byte>(_decompressed.GetBuffer(), 0, (int)_decompressed.Length);
    }
}
```

## Measure what a compressor saves

`session.Statistics` counts the bytes the session sends and receives after compression. The **Network Statistics** section of **Tools > RelayNet > Debug Window** shows the same numbers as rates. Compare them with and without a compressor.

An offline session sends nothing over the network, but it still counts what it would send. So `NetSession.CreateOffline(new NetZeroPackCompressor())` is a quick way to measure a compressor on your own game, without a relay.

## Delta compression

Most of what's left after packing repeats from one update to the next: an object's id, its prefab, and every value that didn't change. Sending only what changed would save much more, about 60–75% in the same tests. That's planned, but it needs to know which earlier state each receiver has, so it can't be a stateless compressor. See [Bandwidth](roadmap.md#4-bandwidth) in the roadmap.
