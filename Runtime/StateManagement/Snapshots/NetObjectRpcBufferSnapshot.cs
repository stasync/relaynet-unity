using System.Collections.Generic;
using Keel.Networking;

namespace Keel.RelayNet.StateManagement.Snapshots
{
    public readonly struct NetObjectRpcBufferSnapshot
    {
        public readonly IReadOnlyList<NetObjectRpcStateSnapshot> PersistentBuffer;
        public readonly IReadOnlyList<NetObjectRpcStateSnapshot> TemporalBuffer;

        internal NetObjectRpcBufferSnapshot(NetObjectRpcBuffer state)
        {
            var persistentBuffer = new List<NetObjectRpcStateSnapshot>(state.PersistentBuffer.Count);
            foreach (var rpc in state.PersistentBuffer)
                persistentBuffer.Add(rpc.ToSnapshot());

            var temporalBuffer = new List<NetObjectRpcStateSnapshot>(state.TemporalBuffer.Count);
            foreach (var rpc in state.TemporalBuffer)
                temporalBuffer.Add(rpc.ToSnapshot());

            PersistentBuffer = persistentBuffer;
            TemporalBuffer = temporalBuffer;
        }

        internal NetObjectRpcBufferSnapshot(NetReader reader)
        {
            var persistentBufferLength = reader.ReadPackedUInt32();
            var persistentBuffer = new List<NetObjectRpcStateSnapshot>((int)persistentBufferLength);
            for (var index = 0; index < persistentBufferLength; index++)
                persistentBuffer.Add(new NetObjectRpcStateSnapshot(reader));
            
            var temporalBufferLength = reader.ReadPackedUInt32();
            var temporalBuffer = new List<NetObjectRpcStateSnapshot>((int)temporalBufferLength);
            for (var index = 0; index < temporalBufferLength; index++)
                temporalBuffer.Add(new NetObjectRpcStateSnapshot(reader));
            
            PersistentBuffer = persistentBuffer;
            TemporalBuffer = temporalBuffer;
        }

        internal void Serialize(NetWriter writer)
        {
            writer.WritePackedUInt32((uint)PersistentBuffer.Count);
            foreach (var rpcStateSnapshot in PersistentBuffer)
                rpcStateSnapshot.Serialize(writer);
            
            writer.WritePackedUInt32((uint)TemporalBuffer.Count);
            foreach (var rpcStateSnapshot in TemporalBuffer)
                rpcStateSnapshot.Serialize(writer);
        }
        
        public int GetSize()
        {
            var result = 0;
            foreach (var snapshot in PersistentBuffer)
                result += snapshot.GetSize();

            foreach (var snapshot in TemporalBuffer)
                result += snapshot.GetSize();

            return result;
        }
    }
}