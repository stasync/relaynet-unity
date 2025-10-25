using Core.Networking;
using System.Collections.Generic;

namespace LROTech.Network.StateManagement
{
    public readonly struct NetObjectRPCBufferSnapshot
    {
        public readonly IReadOnlyCollection<NetObjectRPCStateSnapshot> PersistentBuffer;
        public readonly IReadOnlyCollection<NetObjectRPCStateSnapshot> TemporalBuffer;

        internal NetObjectRPCBufferSnapshot(NetObjectRPCBuffer state)
        {
            var persistentBuffer = new List<NetObjectRPCStateSnapshot>();
            foreach (var rpc in state.PersistentBuffer)
                persistentBuffer.Add(rpc.ToSnapshot());

            var temporalBuffer = new List<NetObjectRPCStateSnapshot>();
            foreach (var rpc in state.TemporalBuffer)
                temporalBuffer.Add(rpc.ToSnapshot());

            PersistentBuffer = persistentBuffer;
            TemporalBuffer = temporalBuffer;
        }

        internal NetObjectRPCBufferSnapshot(NetReader reader)
        {
            PersistentBuffer = reader.ReadList(netReader => new NetObjectRPCStateSnapshot(netReader));
            TemporalBuffer = reader.ReadList(netReader => new NetObjectRPCStateSnapshot(netReader));
        }

        internal void Serialize(NetWriter writer)
        {
            writer.WriteCollection(PersistentBuffer, SerializeRpcState);
            writer.WriteCollection(TemporalBuffer, SerializeRpcState);
        }

        private static void SerializeRpcState(NetObjectRPCStateSnapshot rpcStateSnapshot, NetWriter netWriter) =>
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