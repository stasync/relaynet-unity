using System.Collections.Generic;
using Core.Networking;

namespace LROTech.Network.StateManagement.Snapshots
{
    public readonly struct NetObjectRpcBufferSnapshot
    {
        public readonly IReadOnlyCollection<NetObjectRpcStateSnapshot> PersistentBuffer;
        public readonly IReadOnlyCollection<NetObjectRpcStateSnapshot> TemporalBuffer;

        internal NetObjectRpcBufferSnapshot(NetObjectRpcBuffer state)
        {
            var persistentBuffer = new List<NetObjectRpcStateSnapshot>();
            foreach (var rpc in state.PersistentBuffer)
                persistentBuffer.Add(rpc.ToSnapshot());

            var temporalBuffer = new List<NetObjectRpcStateSnapshot>();
            foreach (var rpc in state.TemporalBuffer)
                temporalBuffer.Add(rpc.ToSnapshot());

            PersistentBuffer = persistentBuffer;
            TemporalBuffer = temporalBuffer;
        }

        internal NetObjectRpcBufferSnapshot(NetReader reader)
        {
            PersistentBuffer = reader.ReadList(netReader => new NetObjectRpcStateSnapshot(netReader));
            TemporalBuffer = reader.ReadList(netReader => new NetObjectRpcStateSnapshot(netReader));
        }

        internal void Serialize(NetWriter writer)
        {
            writer.WriteCollection(PersistentBuffer, SerializeRpcState);
            writer.WriteCollection(TemporalBuffer, SerializeRpcState);
        }

        private static void SerializeRpcState(NetObjectRpcStateSnapshot rpcStateSnapshot, NetWriter netWriter) =>
            rpcStateSnapshot.Serialize(netWriter);

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