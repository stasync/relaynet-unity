using System;
using System.Collections.Generic;

namespace LROTech.Network.StateManagement
{
    internal static class RPCStateUIDAllocator
    {
        public static uint NextUid() => ++m_Counter;
        private static uint m_Counter;
    }

    public readonly struct RPCState
    {
        public readonly int Hash;
        public readonly uint UID;
        public readonly byte[] Payload;

        public bool IsValid => !Hash.Equals(0) && Payload != null;

        public RPCState(int hash, uint uid, byte[] payload)
        {
            Hash = hash;
            UID = uid;
            Payload = payload;
        }

        public RPCStateSnapshot ToSnapshot() =>
            RPCStateSnapshot.FromState(this);

        public static RPCState FromSnapshot(RPCStateSnapshot snapshot) =>
            new(snapshot.Hash, snapshot.UID, snapshot.Payload);

        public override int GetHashCode()
        {
            var hash = 13;
            hash = hash * 7 + Hash.GetHashCode();
            hash = hash * 7 + UID.GetHashCode();
            foreach (var d in Payload)
                hash = hash * 7 + d.GetHashCode();
            return hash;
        }
    }

    public sealed class NetObjectRPCState
    {
        internal struct NewPRCData
        {
            internal int HashCode;
            internal byte[] Data;
            internal bool Persistent;
            internal RPCBufferChangeMethod BufferChangeMethod;
        }

        public IReadOnlyCollection<RPCState> PersistentBuffer => m_PersistentBuffer;
        public IReadOnlyCollection<RPCState> TemporalBuffer => m_TemporalBuffer;

        private readonly List<RPCState> m_PersistentBuffer = new();
        private readonly List<RPCState> m_TemporalBuffer = new();
        private readonly HashSet<uint> m_ReceiveCollisions = new();

        internal void AddOrUpdate(NewPRCData mutator)
        {
            var uid = RPCStateUIDAllocator.NextUid();
            var data = new RPCState(mutator.HashCode, uid, mutator.Data);

            PushTo(m_TemporalBuffer, data, mutator.BufferChangeMethod);
            if (mutator.Persistent)
                PushTo(m_PersistentBuffer, data, mutator.BufferChangeMethod);
        }

        private static void PushTo(List<RPCState> targetBuffer, RPCState newData, RPCBufferChangeMethod changeMethod)
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
                m_PersistentBuffer.Add(RPCState.FromSnapshot(rpcSnapshot));

            m_TemporalBuffer.Clear();
            foreach (var rpcSnapshot in snapshot.TemporalBuffer)
                m_TemporalBuffer.Add(RPCState.FromSnapshot(rpcSnapshot));
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