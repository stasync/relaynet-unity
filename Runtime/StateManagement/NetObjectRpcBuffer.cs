using System;
using System.Collections.Generic;
using LROTech.Network.StateManagement.Snapshots;

namespace LROTech.Network.StateManagement
{
    public sealed class NetObjectRpcBuffer
    {
        internal struct NewRpcData
        {
            internal int HashCode;
            internal byte[] Data;
            internal bool Persistent;
            internal RpcBufferChangeMethod BufferChangeMethod;
        }

        public IReadOnlyCollection<NetObjectRpcState> PersistentBuffer => m_PersistentBuffer;
        public IReadOnlyCollection<NetObjectRpcState> TemporalBuffer => m_TemporalBuffer;

        private readonly List<NetObjectRpcState> m_PersistentBuffer = new();
        private readonly List<NetObjectRpcState> m_TemporalBuffer = new();
        private readonly HashSet<uint> m_ReceiveCollisions = new();

        internal void AddOrUpdate(NewRpcData mutator)
        {
            var uid = NetObjectRpcUtils.UidAllocator.NextUid();
            var data = new NetObjectRpcState(mutator.HashCode, uid, mutator.Data);

            PushTo(m_TemporalBuffer, data, mutator.BufferChangeMethod);
            if (mutator.Persistent)
                PushTo(m_PersistentBuffer, data, mutator.BufferChangeMethod);
        }

        private static void PushTo(List<NetObjectRpcState> targetBuffer, NetObjectRpcState newData, RpcBufferChangeMethod changeMethod)
        {
            if (changeMethod == RpcBufferChangeMethod.Replace)
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
                    m_ReceiveCollisions.Add(rpcState.Uid);
                }
            }

            foreach (var rpcState in m_TemporalBuffer)
            {
                if (m_ReceiveCollisions.Contains(rpcState.Uid))
                    continue;

                handler(rpcState.Hash, rpcState.Payload);
                m_ReceiveCollisions.Add(rpcState.Uid);
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

        internal void RestoreFormSnapshot(NetObjectRpcBufferSnapshot snapshot)
        {
            m_PersistentBuffer.Clear();
            foreach (var rpcSnapshot in snapshot.PersistentBuffer)
                m_PersistentBuffer.Add(new NetObjectRpcState(rpcSnapshot));

            m_TemporalBuffer.Clear();
            foreach (var rpcSnapshot in snapshot.TemporalBuffer)
                m_TemporalBuffer.Add(new NetObjectRpcState(rpcSnapshot));
        }

        public NetObjectRpcBufferSnapshot ToSnapshot() =>
            new(state: this);

        internal int GetVersionHash()
        {
            var hash = 13;
            foreach (var rpc in m_PersistentBuffer)
                hash = hash * 7 + rpc.GetVersionHash();
            foreach (var rpc in m_TemporalBuffer)
                hash = hash * 7 + rpc.GetVersionHash();
            return hash;
        }
    }
}