using System;
using System.Collections.Generic;
using Keel.RelayNet.StateManagement.Snapshots;

namespace Keel.RelayNet.StateManagement
{
    /// <summary>
    /// The owner's RPCs of the current update. They travel with the object's state and run once on the receivers.
    /// </summary>
    public sealed class NetObjectRpcBuffer
    {
        internal struct NewRpcData
        {
            internal int HashCode;
            internal byte[] Data;
        }

        public IReadOnlyCollection<NetObjectRpcState> TemporalBuffer => _temporalBuffer;

        private readonly List<NetObjectRpcState> _temporalBuffer = new();

        internal void Add(NewRpcData newRpcData)
        {
            var uid = NetObjectRpcUtils.UidAllocator.NextUid();
            _temporalBuffer.Add(new NetObjectRpcState(newRpcData.HashCode, uid, newRpcData.Data));
        }

        internal void ExecuteRemotely(Action<int, byte[]> handler)
        {
            foreach (var rpcState in _temporalBuffer)
                handler(rpcState.Hash, rpcState.Payload);
        }

        internal void Clear() =>
            _temporalBuffer.Clear();

        internal void RestoreFromSnapshot(NetObjectRpcBufferSnapshot snapshot)
        {
            _temporalBuffer.Clear();
            foreach (var rpcSnapshot in snapshot.TemporalBuffer)
                _temporalBuffer.Add(new NetObjectRpcState(rpcSnapshot));
        }

        public NetObjectRpcBufferSnapshot ToSnapshot() =>
            new(state: this);

        internal int GetVersionHash()
        {
            var hash = 13;
            foreach (var rpc in _temporalBuffer)
                hash = hash * 7 + rpc.GetVersionHash();
            return hash;
        }
    }
}
