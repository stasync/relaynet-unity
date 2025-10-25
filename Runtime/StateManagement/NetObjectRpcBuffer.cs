using System;
using System.Collections.Generic;

namespace LROTech.Network.StateManagement
{
    public sealed class NetObjectRPCBuffer
    {
        internal struct NewPRCData
        {
            internal int HashCode;
            internal byte[] Data;
            internal bool Persistent;
            internal RPCBufferChangeMethod BufferChangeMethod;
        }

        public IReadOnlyCollection<NetObjectRPCState> PersistentBuffer => m_PersistentBuffer;
        public IReadOnlyCollection<NetObjectRPCState> TemporalBuffer => m_TemporalBuffer;

        private readonly List<NetObjectRPCState> m_PersistentBuffer = new();
        private readonly List<NetObjectRPCState> m_TemporalBuffer = new();
        private readonly HashSet<uint> m_ReceiveCollisions = new();

        internal void AddOrUpdate(NewPRCData mutator)
        {
            var uid = NetObjectRPCUtils.UIDAllocator.NextUid();
            var data = new NetObjectRPCState(mutator.HashCode, uid, mutator.Data);

            PushTo(m_TemporalBuffer, data, mutator.BufferChangeMethod);
            if (mutator.Persistent)
                PushTo(m_PersistentBuffer, data, mutator.BufferChangeMethod);
        }

        private static void PushTo(List<NetObjectRPCState> targetBuffer, NetObjectRPCState newData, RPCBufferChangeMethod changeMethod)
        {
            if (changeMethod == RPCBufferChangeMethod.Replace)
            {
                for (var i = targetBuffer.Count - 1; i >= 0; i--)
                {
                    if (targetBuffer[i].Hash == newData.Hash)
                        targetBuffer.RemoveAt(i);
                }
            }

            targetBuffer.Add(newData);
        }

        public void ExecuteRemotely(bool executePersistent, Action<int, byte[]> handler)
        {
            m_ReceiveCollisions.Clear();

            if (executePersistent)
            {
                foreach (var rpcState in m_PersistentBuffer)
                {
                    handler(rpcState.Hash, rpcState.Payload);
                    m_ReceiveCollisions.Add(rpcState.UID);
                }
            }

            foreach (var rpcState in m_TemporalBuffer)
            {
                if (m_ReceiveCollisions.Contains(rpcState.UID))
                    continue;

                handler(rpcState.Hash, rpcState.Payload);
                m_ReceiveCollisions.Add(rpcState.UID);
            }
        }

        private void ClearPersistent() =>
            m_PersistentBuffer.Clear();

        internal void ClearTemporal() =>
            m_TemporalBuffer.Clear();

        internal void Clear()
        {
            ClearPersistent();
            ClearTemporal();
        }

        internal void RemovePersistent(int hashCode) =>
            m_PersistentBuffer.RemoveAll(d => d.Hash.Equals(hashCode));

        internal void RestoreFormSnapshot(NetObjectRPCStateSnapshot snapshot)
        {
            m_PersistentBuffer.Clear();
            foreach (var rpcSnapshot in snapshot.PersistentBuffer)
                m_PersistentBuffer.Add(NetObjectRPCState.FromSnapshot(rpcSnapshot));

            m_TemporalBuffer.Clear();
            foreach (var rpcSnapshot in snapshot.TemporalBuffer)
                m_TemporalBuffer.Add(NetObjectRPCState.FromSnapshot(rpcSnapshot));
        }

        public NetObjectRPCStateSnapshot ToSnapshot() =>
            NetObjectRPCStateSnapshot.FromState(this);

        public override int GetHashCode()
        {
            var hash = 13;
            foreach (var rpc in m_PersistentBuffer)
                hash = hash * 7 + rpc.GetHashCode();
            foreach (var rpc in m_TemporalBuffer)
                hash = hash * 7 + rpc.GetHashCode();
            return hash;
        }
    }
}