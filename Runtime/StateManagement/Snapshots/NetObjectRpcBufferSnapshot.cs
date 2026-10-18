using System.Collections.Generic;
using Keel.Networking;

namespace Keel.RelayNet.StateManagement.Snapshots
{
    public readonly struct NetObjectRpcBufferSnapshot
    {
        public readonly IReadOnlyList<NetObjectRpcStateSnapshot> TemporalBuffer;

        internal NetObjectRpcBufferSnapshot(NetObjectRpcBuffer state)
        {
            var temporalBuffer = new List<NetObjectRpcStateSnapshot>(state.TemporalBuffer.Count);
            foreach (var rpc in state.TemporalBuffer)
                temporalBuffer.Add(rpc.ToSnapshot());

            TemporalBuffer = temporalBuffer;
        }

        internal NetObjectRpcBufferSnapshot(NetReader reader)
        {
            var temporalBufferLength = reader.ReadPackedUInt32();
            var temporalBuffer = new List<NetObjectRpcStateSnapshot>((int)temporalBufferLength);
            for (var index = 0; index < temporalBufferLength; index++)
                temporalBuffer.Add(new NetObjectRpcStateSnapshot(reader));

            TemporalBuffer = temporalBuffer;
        }

        internal void Serialize(NetWriter writer)
        {
            writer.WritePackedUInt32((uint)TemporalBuffer.Count);
            foreach (var rpcStateSnapshot in TemporalBuffer)
                rpcStateSnapshot.Serialize(writer);
        }

        public int GetSize()
        {
            var result = 0;
            foreach (var snapshot in TemporalBuffer)
                result += snapshot.GetSize();

            return result;
        }
    }
}
