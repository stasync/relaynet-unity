using Core.Networking;
using System;
using System.Collections.Generic;
using LROTech.Network.Sessions;
using LROTech.Network.StateManagement.Snapshots;
using UnityEngine;

namespace LROTech.Network.StateManagement
{
    public sealed class NetObjectState
    {
        public event Action OnOwnerChanged = delegate { };
        public event Action<NetObjectStateChangeRequestType> LocalUpdateRequest = delegate { };
        public event Action Invalidated = delegate { };

        private readonly Func<ClientUid, NetWorldPlayer> m_PlayerProvider;

        public bool IsValid => Uid.IsValid;

        #region SYNCRONIZED STATE

        public NetObjectUid Uid { get; private set; }

        /// <summary>
        /// TODO: Should be changed to long or uint
        /// </summary>
        public string PrefabId { get; private set; }

        public bool IsSceneObject { get; private set; }

        public ClientUid OwnerUid { get; private set; }

        public NetObjectStateChangeRequestType StateChangeType { get; private set; }

        public readonly NetObjectRpcBuffer RpcBuffer;

        public readonly NetObjectDataCollection DataCollection;

        #endregion

        public bool IsOwnedByLocalPlayer => Owner is { IsLocal: true };

        public bool IsOwnedByMasterClient => Owner is { IsMaster: true };
        
        public NetWorldPlayer Owner => m_PlayerProvider(OwnerUid);

        public bool IsOwnersTagObject => Owner != null && Owner.HasTagObject(Uid);

        internal IReadOnlyCollection<RpcWithoutAuthorityRequest> RpcWithoutAuthorityRequests => m_RpcWithoutAuthorityRequests;
        private readonly List<RpcWithoutAuthorityRequest> m_RpcWithoutAuthorityRequests = new();

        private readonly Queue<NetObjectStateChangeRequestType> m_StateChangeTypeQueue = new();
        private readonly Queue<NetObjectRpcBuffer.NewRpcData> m_RpcBufferUpdateQueue = new();
        private readonly Queue<RpcWithoutAuthorityRequest> m_RpcWithoutAuthorityQueue = new();
        private readonly Queue<int> m_PersistentRpcToRemoveQueue = new();

        private readonly NetWriter m_Writer = new();

        internal NetObjectState(NetObjectUid uid, string prefabId, bool isSceneObject, ClientUid owner,
            Func<ClientUid, NetWorldPlayer> playerProvider)
        {
            m_PlayerProvider = playerProvider;

            Uid = uid;
            PrefabId = prefabId ?? string.Empty;
            IsSceneObject = isSceneObject;
            OwnerUid = owner;

            StateChangeType = NetObjectStateChangeRequestType.UnreliableUpdate;

            RpcBuffer = new NetObjectRpcBuffer();
            DataCollection = new NetObjectDataCollection(() => IsOwnedByLocalPlayer);
        }

        internal NetObjectState(NetObjectStateSnapshot snapshot, Func<ClientUid, NetWorldPlayer> playerProvider) :
            this(snapshot.ObjectUID, snapshot.PrefabId, snapshot.IsSceneObject, snapshot.OwnerUID, playerProvider) =>
            ChangeObjectRemotely(snapshot);
        
        public bool IsOwnedBy(ClientUid uid) => 
            OwnerUid == uid;

        internal void InvalidateOwner() =>
            OwnerUid = ClientUid.Invalid;

        internal void Invalidate()
        {
            var wasValid = IsValid;

            Uid = NetObjectUid.Invalid;
            InvalidateOwner();

            PrefabId = string.Empty;
            IsSceneObject = false;
            StateChangeType = NetObjectStateChangeRequestType.None;

            RpcBuffer.Clear();
            DataCollection.Clear();
            m_RpcWithoutAuthorityRequests.Clear();

            m_StateChangeTypeQueue.Clear();
            m_RpcBufferUpdateQueue.Clear();
            m_RpcWithoutAuthorityQueue.Clear();
            m_PersistentRpcToRemoveQueue.Clear();

            if (wasValid)
                Invalidated();

            LocalUpdateRequest = null;
            Invalidated = null;
        }

        internal void ChangeObjectViewDataLocally(byte[] data, bool initialization = false)
        {
            if (!IsOwnedByLocalPlayer)
                throw new InvalidOperationException($"No authority to change '{Uid}' object with owner '{OwnerUid}'.");

            var viewData = DataCollection.GetOrCreateData<NetObjectViewStateData>();
            viewData.SetData(data, initialization);
        }

        internal bool PrepareForSerialization()
        {
            if (!IsOwnedByLocalPlayer)
                throw new InvalidOperationException($"No authority to change '{Uid}' object with owner '{OwnerUid}'.");

            var lastHashCode = GetVersionHash();
            LocalUpdateRequest(StateChangeType);
            var newHashCode = GetVersionHash();
            var changed = newHashCode != lastHashCode;
            return changed;
        }

        internal void ChangeObjectRemotely(NetObjectStateSnapshot snapshot)
        {
            var checkOwner = snapshot.StateChangeType < NetObjectStateChangeRequestType.ReliableUpdateOwnerChange;

            if (snapshot.StateChangeType == NetObjectStateChangeRequestType.ReliableUpdateOwnerChange)
                Debug.LogError($"_______ {snapshot.StateChangeType} {Uid} : {PrefabId}");

            if (checkOwner && IsOwnedByLocalPlayer)
            {
                Debug.LogError(
                    $"Unable to restore local object with '{Uid}' : '{PrefabId}' Change type: '{snapshot.StateChangeType}'. This can be normal in some cases.");
                return;
            }

            UpdateOwnerInternal(snapshot.OwnerUID);

            StateChangeType = snapshot.StateChangeType;
            RpcBuffer.RestoreFormSnapshot(snapshot.RpcState);
            DataCollection.RestoreFromSnapShot(snapshot.DataState);
        }

        private void UpdateOwnerInternal(ClientUid newOwner)
        {
            var previousOwner = OwnerUid;
            OwnerUid = newOwner;

            if (previousOwner != OwnerUid)
                OnOwnerChanged();
        }

        public NetObjectStateSnapshot ToSnapshot() =>
            new(state: this);

        internal void OnBeforeUpdateWorldState(float worldDelta)
        {
            // Clear rpc request without authority.
            m_RpcWithoutAuthorityRequests.Clear();

            // Clear temporal rpc buffer.
            RpcBuffer.ClearTemporal();

            // Return object to regular UDP pipeline.
            StateChangeType = NetObjectStateChangeRequestType.UnreliableUpdate;

            // New RPC requests.
            while (m_RpcBufferUpdateQueue.TryDequeue(out var newRpcState))
                RpcBuffer.AddOrUpdate(newRpcState);

            // Persistent RPC remove request.
            while (m_PersistentRpcToRemoveQueue.TryDequeue(out var rpcHash))
                RpcBuffer.RemovePersistent(rpcHash);

            // Update no-authority RPC requests. Note that is not a part of the state.
            while (m_RpcWithoutAuthorityQueue.TryDequeue(out var rpcWithoutAuthorityRequest))
                m_RpcWithoutAuthorityRequests.Add(rpcWithoutAuthorityRequest);

            // State change type requests.
            while (m_StateChangeTypeQueue.TryDequeue(out var newRequestType))
            {
                if (StateChangeType < newRequestType)
                    StateChangeType = newRequestType;
            }
        }

        internal void ScheduleUpdate(NetObjectStateChangeRequestType requestType)
        {
            if (!IsOwnedByLocalPlayer)
                throw new InvalidOperationException($"No authority to change {Uid} object with owner {OwnerUid}");

            if (StateChangeType > requestType)
                Debug.LogError($"Unable to set request {requestType} because it has less priority than current request {StateChangeType}");
            else
                m_StateChangeTypeQueue.Enqueue(requestType);
        }

        internal void ChangedOwner(ClientUid newOwner)
        {
            if (!IsSceneObject)
                throw new Exception("You can manage owners only for scene objects");

            if (newOwner.IsValid)
            {
                UpdateOwnerInternal(newOwner);
                m_StateChangeTypeQueue.Enqueue(NetObjectStateChangeRequestType.ReliableUpdateOwnerChange);
            }
            else
                Debug.LogError($"Unable to change owner, invalid id {newOwner}");
        }

        public override string ToString() =>
            $"{Uid}:{PrefabId} " +
            $"s:{IsSceneObject} " +
            $"st:{StateChangeType} " +
            $"own:{OwnerUid}:{(Owner == null ? "null" : Owner.ToString())} " +
            $"l:{IsOwnedByLocalPlayer} " +
            $"m:{IsOwnedByMasterClient} " +
            $"rpc: t:{RpcBuffer.TemporalBuffer.Count} p:{RpcBuffer.PersistentBuffer.Count}";

        internal void ScheduleRpcWithoutAuthority(NoAuthorityRpcType type, int hashCode, byte[] data, DeliveryMethod deliveryMethod) =>
            m_RpcWithoutAuthorityQueue.Enqueue(new RpcWithoutAuthorityRequest(Uid, type, hashCode, data, deliveryMethod));

        internal void ScheduleRpc(int hashCode, byte[] data, bool persistent, RpcBufferChangeMethod bufferChangeMethod, DeliveryMethod deliveryMethod)
        {
            m_RpcBufferUpdateQueue.Enqueue(new NetObjectRpcBuffer.NewRpcData
            {
                HashCode = hashCode,
                Data = data,
                Persistent = persistent,
                BufferChangeMethod = bufferChangeMethod,
            });

            if (deliveryMethod == DeliveryMethod.Reliable)
                ScheduleUpdate(NetObjectStateChangeRequestType.ReliableUpdate);
        }

        internal void ScheduleRemovePersistentRpc(int hashCode) =>
            m_PersistentRpcToRemoveQueue.Enqueue(hashCode);

        internal int GetVersionHash()
        {
            var hash = 13;
            hash = hash * 7 + Uid.GetHashCode();
            hash = hash * 7 + PrefabId.GetHashCode();
            hash = hash * 7 + IsSceneObject.GetHashCode();
            hash = hash * 7 + OwnerUid.GetHashCode();
            hash = hash * 7 + StateChangeType.GetHashCode();
            hash = hash * 7 + RpcBuffer.GetVersionHash();
            hash = hash * 7 + DataCollection.GetVersionHash();
            return hash;
        }

        internal readonly struct RpcWithoutAuthorityRequest
        {
            public readonly RpcWithoutAuthorityState State;
            public readonly DeliveryMethod DeliveryMethod;

            internal RpcWithoutAuthorityRequest(NetObjectUid objectUID, NoAuthorityRpcType type, int hashCode, byte[] payload,
                DeliveryMethod deliveryMethod)
            {
                State = new RpcWithoutAuthorityState(objectUID, type, hashCode, payload);
                DeliveryMethod = deliveryMethod;
            }
        }
    }
}