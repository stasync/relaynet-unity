using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LROTech.Network.StateManagement.Snapshots;
using UnityEngine;

namespace LROTech.Network.StateManagement
{
    public sealed class NetObjectState
    {
        #region SYNCRONIZED STATE

        public NetObjectUid Uid { get; private set; }

        public uint PrefabUid { get; private set; }

        public bool IsSceneObject { get; private set; }

        public ClientUid OwnerUid { get; private set; }

        public NetObjectStateChangeRequestType StateChangeType { get; private set; }

        public float WorldTime { get; private set; }

        public readonly NetObjectRpcBuffer RpcBuffer;

        public readonly NetObjectDataCollection DataCollection;

        #endregion

        private readonly Queue<int> _persistentRpcToRemoveQueue = new();
        private readonly Func<ClientUid, NetWorldPlayer> _playerProvider;
        private readonly Queue<NetObjectRpcBuffer.NewRpcData> _rpcBufferUpdateQueue = new();
        private readonly Queue<RpcWithoutAuthorityRequest> _rpcWithoutAuthorityQueue = new();
        private readonly List<RpcWithoutAuthorityRequest> _rpcWithoutAuthorityRequests = new();
        private readonly Queue<NetObjectStateChangeRequestType> _stateChangeTypeQueue = new();

        public bool IsOwnedByLocalPlayer => Owner != null && Owner.IsLocal();

        public bool IsOwnedByMasterClient => Owner != null && Owner.IsMaster();

        public NetWorldPlayer Owner => _playerProvider(OwnerUid);

        public bool IsTagObjectForCurrentOwner => Owner != null && Owner.HasTagObject(Uid);

        internal IReadOnlyCollection<RpcWithoutAuthorityRequest> RpcWithoutAuthorityRequests => _rpcWithoutAuthorityRequests;
        public event Action OwnerChanged = delegate { };
        public event Action<NetObjectStateChangeRequestType> LocalUpdateRequest = delegate { };
        public event Action Invalidated = delegate { };

        internal NetObjectState(Func<ClientUid, NetWorldPlayer> playerProvider, NetObjectUid uid, uint prefabUid, bool isSceneObject, ClientUid owner)
        {
            _playerProvider = playerProvider;

            Uid = uid;
            PrefabUid = prefabUid;
            IsSceneObject = isSceneObject;
            OwnerUid = owner;

            StateChangeType = NetObjectStateChangeRequestType.UnreliableUpdate;

            RpcBuffer = new NetObjectRpcBuffer();
            DataCollection = new NetObjectDataCollection(validator: IsValid, dataWritePermission: () => IsOwnedByLocalPlayer);
        }

        internal NetObjectState(Func<ClientUid, NetWorldPlayer> playerProvider, NetObjectStateSnapshot snapshot) :
            this(playerProvider, snapshot.ObjectUid, snapshot.PrefabUid, snapshot.IsSceneObject, snapshot.OwnerUid) =>
            ChangeObjectRemotely(snapshot);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValid() =>
            Uid.IsValid;

        internal void Invalidate()
        {
            EnsureValid();

            Uid = NetObjectUid.Invalid;
            OwnerUid = ClientUid.Invalid;

            PrefabUid = 0;
            IsSceneObject = false;
            StateChangeType = NetObjectStateChangeRequestType.None;

            RpcBuffer.Clear();
            DataCollection.Invalidate();
            _rpcWithoutAuthorityRequests.Clear();

            _stateChangeTypeQueue.Clear();
            _rpcBufferUpdateQueue.Clear();
            _rpcWithoutAuthorityQueue.Clear();
            _persistentRpcToRemoveQueue.Clear();

            Invalidated();

            LocalUpdateRequest = null;
            Invalidated = null;
        }

        internal void ChangeObjectViewDataLocally(byte[] data)
        {
            if (!IsOwnedByLocalPlayer)
                throw new InvalidOperationException($"No authority to change '{Uid}' object with owner '{OwnerUid}'.");

            var viewData = DataCollection.GetOrCreateData<NetObjectViewStateData>();
            viewData.SetData(data);
        }

        internal void PrepareForSerialization(float worldTime)
        {
            if (!IsOwnedByLocalPlayer)
                throw new InvalidOperationException($"No authority to change '{Uid}' object with owner '{OwnerUid}'.");

            WorldTime = worldTime;
            LocalUpdateRequest(StateChangeType);
        }

        internal void ChangeObjectRemotely(NetObjectStateSnapshot snapshot)
        {
            var validateOwner = snapshot.StateChangeType < NetObjectStateChangeRequestType.ReliableUpdateOwnerChange;

            if (snapshot.StateChangeType == NetObjectStateChangeRequestType.ReliableUpdateOwnerChange)
                Debug.LogError($"_______OWNER CHANGED {snapshot.StateChangeType} {Uid} : {PrefabUid}");

            if (validateOwner && IsOwnedByLocalPlayer)
            {
                Debug.LogError($"Unable to restore local object with '{Uid}' : '{PrefabUid}' Change type: '{snapshot.StateChangeType}'. This can be normal in some cases.");
                return;
            }

            UpdateOwnerInternal(snapshot.OwnerUid);

            StateChangeType = snapshot.StateChangeType;
            WorldTime = snapshot.WorldTime;
            RpcBuffer.RestoreFormSnapshot(snapshot.RpcState);
            DataCollection.RestoreFromSnapShot(snapshot.DataState);
        }

        private void UpdateOwnerInternal(ClientUid newOwner)
        {
            var previousOwner = OwnerUid;
            OwnerUid = newOwner;

            if (previousOwner != OwnerUid)
                OwnerChanged();
        }

        public NetObjectStateSnapshot ToSnapshot()
        {
            EnsureValid();
            return new NetObjectStateSnapshot(this);
        }

        internal void OnBeforeUpdateWorldState(float worldDelta)
        {
            // Clear rpc request without authority.
            _rpcWithoutAuthorityRequests.Clear();

            // Clear temporal rpc buffer.
            RpcBuffer.ClearTemporal();

            // Return object to regular UDP pipeline.
            StateChangeType = NetObjectStateChangeRequestType.UnreliableUpdate;

            // New RPC requests.
            while (_rpcBufferUpdateQueue.TryDequeue(out var newRpcState))
                RpcBuffer.AddOrUpdate(newRpcState);

            // Persistent RPC remove request.
            while (_persistentRpcToRemoveQueue.TryDequeue(out var rpcHash))
                RpcBuffer.RemovePersistent(rpcHash);

            // Update no-authority RPC requests. Note that it is not a part of the state.
            while (_rpcWithoutAuthorityQueue.TryDequeue(out var rpcWithoutAuthorityRequest))
                _rpcWithoutAuthorityRequests.Add(rpcWithoutAuthorityRequest);

            // State change type requests.
            while (_stateChangeTypeQueue.TryDequeue(out var newRequestType))
            {
                if (StateChangeType < newRequestType)
                    StateChangeType = newRequestType;
            }
        }

        internal void ScheduleUpdate(NetObjectStateChangeRequestType requestType)
        {
            if (!IsOwnedByLocalPlayer)
                throw new InvalidOperationException($"No authority to change '{Uid}' object with owner '{OwnerUid}'.");

            if (StateChangeType < requestType)
                _stateChangeTypeQueue.Enqueue(requestType);
        }

        public void RequestRemoval()
        {
            EnsureValid();

            if (!IsOwnedByLocalPlayer)
                throw new InvalidOperationException($"No authority to change '{Uid}' object with owner '{OwnerUid}'.");

            ScheduleUpdate(NetObjectStateChangeRequestType.RemoveSoft);
        }

        public bool IsOwnedBy(ClientUid uid)
        {
            EnsureValid();
            return OwnerUid == uid;
        }

        internal void ChangedOwner(ClientUid newOwner)
        {
            if (!IsSceneObject)
                throw new InvalidOperationException("You can manage owners only for scene objects");

            if (newOwner.IsValid)
            {
                UpdateOwnerInternal(newOwner);
                _stateChangeTypeQueue.Enqueue(NetObjectStateChangeRequestType.ReliableUpdateOwnerChange);
            }
            else
                Debug.LogError($"Unable to change owner, invalid id {newOwner}");
        }

        internal void InvalidateOwner() =>
            OwnerUid = ClientUid.Invalid;

        internal void ScheduleRpcWithoutAuthority(NoAuthorityRpcType type, int hashCode, byte[] data, DeliveryMethod deliveryMethod) =>
            _rpcWithoutAuthorityQueue.Enqueue(new RpcWithoutAuthorityRequest(Uid, type, hashCode, data, deliveryMethod));

        internal void ScheduleRpc(int hashCode, byte[] data, bool persistent, RpcBufferChangeMethod bufferChangeMethod, DeliveryMethod deliveryMethod)
        {
            _rpcBufferUpdateQueue.Enqueue(new NetObjectRpcBuffer.NewRpcData
            {
                HashCode = hashCode,
                Data = data,
                Persistent = persistent,
                BufferChangeMethod = bufferChangeMethod
            });

            if (deliveryMethod == DeliveryMethod.Reliable)
                ScheduleUpdate(NetObjectStateChangeRequestType.ReliableUpdate);
        }

        internal void ScheduleRemovePersistentRpc(int hashCode) =>
            _persistentRpcToRemoveQueue.Enqueue(hashCode);

        internal int GetVersionHash()
        {
            var hash = 13;
            hash = hash * 7 + Uid.GetHashCode();
            hash = hash * 7 + PrefabUid.GetHashCode();
            hash = hash * 7 + IsSceneObject.GetHashCode();
            hash = hash * 7 + OwnerUid.GetHashCode();
            hash = hash * 7 + StateChangeType.GetHashCode();
            hash = hash * 7 + WorldTime.GetHashCode();
            hash = hash * 7 + RpcBuffer.GetVersionHash();
            hash = hash * 7 + DataCollection.GetVersionHash();
            return hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureValid()
        {
            if (!IsValid())
                throw new InvalidOperationException($"Object of type '{GetType().FullName}' is invalid.");
        }

        public override string ToString()
        {
            EnsureValid();

            return $"{Uid}:{PrefabUid} " +
                   $"s:{IsSceneObject} " +
                   $"st:{StateChangeType} " +
                   $"own:{OwnerUid}:{(Owner == null ? "null" : Owner.ToString())} " +
                   $"l:{IsOwnedByLocalPlayer} " +
                   $"m:{IsOwnedByMasterClient} " +
                   $"rpc: t:{RpcBuffer.TemporalBuffer.Count} p:{RpcBuffer.PersistentBuffer.Count}";
        }

        internal readonly struct RpcWithoutAuthorityRequest
        {
            public readonly RpcWithoutAuthorityState State;
            public readonly DeliveryMethod DeliveryMethod;

            internal RpcWithoutAuthorityRequest(NetObjectUid objectUid, NoAuthorityRpcType type, int hashCode, byte[] payload, DeliveryMethod deliveryMethod)
            {
                State = new RpcWithoutAuthorityState(objectUid, type, hashCode, payload);
                DeliveryMethod = deliveryMethod;
            }
        }
    }
}