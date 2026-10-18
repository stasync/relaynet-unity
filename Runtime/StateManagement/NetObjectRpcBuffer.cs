using System;
using System.Collections.Generic;
using Keel.RelayNet.StateManagement.Snapshots;

namespace Keel.RelayNet.StateManagement
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

        public IReadOnlyCollection<NetObjectRpcState> PersistentBuffer => _persistentBuffer;
        public IReadOnlyCollection<NetObjectRpcState> TemporalBuffer => _temporalBuffer;

        private readonly List<NetObjectRpcState> _persistentBuffer = new();
        private readonly List<NetObjectRpcState> _temporalBuffer = new();
        private readonly HashSet<uint> _receiveCollisions = new();

        internal void AddOrUpdate(NewRpcData mutator)
        {
            var uid = NetObjectRpcUtils.UidAllocator.NextUid();
            var data = new NetObjectRpcState(mutator.HashCode, uid, mutator.Data);

            PushTo(_temporalBuffer, data, mutator.BufferChangeMethod);
            if (mutator.Persistent)
                PushTo(_persistentBuffer, data, mutator.BufferChangeMethod);
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

        internal void ExecuteRemotely(bool executePersistent, Action<int, byte[]> handler)
        {
            _receiveCollisions.Clear();

            if (executePersistent)
            {
                foreach (var rpcState in _persistentBuffer)
                {
                    handler(rpcState.Hash, rpcState.Payload);
                    _receiveCollisions.Add(rpcState.Uid);
                }
            }

            foreach (var rpcState in _temporalBuffer)
            {
                if (_receiveCollisions.Contains(rpcState.Uid))
                    continue;

                handler(rpcState.Hash, rpcState.Payload);
                _receiveCollisions.Add(rpcState.Uid);
            }
        }

        private void ClearPersistent() =>
            _persistentBuffer.Clear();

        internal void ClearTemporal() =>
            _temporalBuffer.Clear();

        internal void Clear()
        {
            ClearPersistent();
            ClearTemporal();
        }

        internal void RemovePersistent(int hashCode)
        {
            for (var index = _persistentBuffer.Count - 1; index >= 0; index--)
            {
                var state = _persistentBuffer[index];
                if (state.Hash.Equals(hashCode))
                    _persistentBuffer.RemoveAt(index);
            }
        }

        internal void RestoreFormSnapshot(NetObjectRpcBufferSnapshot snapshot)
        {
            _persistentBuffer.Clear();
            foreach (var rpcSnapshot in snapshot.PersistentBuffer)
                _persistentBuffer.Add(new NetObjectRpcState(rpcSnapshot));

            _temporalBuffer.Clear();
            foreach (var rpcSnapshot in snapshot.TemporalBuffer)
                _temporalBuffer.Add(new NetObjectRpcState(rpcSnapshot));
        }

        public NetObjectRpcBufferSnapshot ToSnapshot() =>
            new(state: this);

        internal int GetVersionHash()
        {
            var hash = 13;
            foreach (var rpc in _persistentBuffer)
                hash = hash * 7 + rpc.GetVersionHash();
            foreach (var rpc in _temporalBuffer)
                hash = hash * 7 + rpc.GetVersionHash();
            return hash;
        }
    }
}