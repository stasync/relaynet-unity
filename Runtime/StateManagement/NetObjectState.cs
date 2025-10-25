using Core.Networking;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LROTech.Network.StateManagement
{
    public enum RPCBufferChangeMethod : byte
    {
        Replace,
        Add,
    }

    public enum ObjectStateChangeRequestType : byte
    {
        None,
        UnreliableUpdate,
        ReliableUpdate,
        ReliableUpdateOwnerChange,
        InitializationUpdate,
        RemoveUpdateSoft,
        RemoveUpdateHard,
    }

    public readonly struct RPCWithoutAuthorityState
    {
        public readonly NetObjectUid ObjectUID;
        public readonly NoAuthorityRPCType Type;
        public readonly NetObjectRPCState RPCState;

        public bool IsValid => ObjectUID.IsValid && RPCState.IsValid;

        public RPCWithoutAuthorityState(NetObjectUid objectUID, NoAuthorityRPCType type, int hashCode, byte[] payload)
        {
            ObjectUID = objectUID;
            Type = type;
            RPCState = new NetObjectRPCState(hashCode, NetObjectRPCUtils.UIDAllocator.NextUid(), payload);
        }

        private RPCWithoutAuthorityState(NetReader reader)
        {
            ObjectUID = reader.ReadInstanceId();
            Type = (NoAuthorityRPCType)reader.ReadByte();
            RPCState = new NetObjectRPCState(new NetObjectRPCStateSnapshot(reader));
        }

        private void Serialize(NetWriter writer)
        {
            writer.WriteInstanceId(ObjectUID);
            writer.WriteByte((byte)Type);
            RPCState.ToSnapshot().Serialize(writer);
        }

        public byte[] ToData()
        {
            var writer = new NetWriter();
            Serialize(writer);
            return writer.ToArray();
        }

        public static RPCWithoutAuthorityState FromReader(NetReader reader) =>
            new(reader);
    }

    public readonly struct RPCWithoutAuthorityRequest
    {
        public readonly RPCWithoutAuthorityState State;
        public readonly DeliveryMethod DeliveryMethod;

        public RPCWithoutAuthorityRequest(NetObjectUid objectUID, NoAuthorityRPCType type, int hashCode, byte[] payload,
            DeliveryMethod deliveryMethod)
        {
            State = new RPCWithoutAuthorityState(objectUID, type, hashCode, payload);
            DeliveryMethod = deliveryMethod;
        }
    }

    public sealed class NetObjectState
    {
        public event Action OnOwnerChanged = delegate { };
        public event Action<ObjectStateChangeRequestType> LocalUpdateRequest = delegate { };
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

        public ObjectStateChangeRequestType StateChangeType { get; private set; }

        public readonly NetObjectRPCBuffer RPCBuffer;

        public readonly NetObjectDataCollection DataCollection;

        #endregion

        public bool IsOwnedByLocalPlayer =>
            Owner is { IsLocal: true };

        public bool IsOwnedByMasterClient =>
            Owner is { IsMaster: true };

        public bool IsOwnedBy(ClientUid uid) => OwnerUid == uid;

        public NetWorldPlayer Owner => m_PlayerProvider(OwnerUid);

        public bool IsOwnersTagObject => Owner != null && Owner.HasTagObject(Uid);

        public IReadOnlyCollection<RPCWithoutAuthorityRequest> RPCWithoutAuthorityRequests => m_RPCWithoutAuthorityRequests;
        private readonly List<RPCWithoutAuthorityRequest> m_RPCWithoutAuthorityRequests = new();

        private readonly Queue<ObjectStateChangeRequestType> m_StateChangeTypeQueue = new();
        private readonly Queue<NetObjectRPCBuffer.NewPRCData> m_RPCBufferUpdateQueue = new();
        private readonly Queue<RPCWithoutAuthorityRequest> m_RPCWithoutAuthorityQueue = new();
        private readonly Queue<int> m_PersistentRPCToRemoveQueue = new();

        private readonly NetWriter m_Writer = new();

        internal NetObjectState(NetObjectUid uid, string prefabId, bool isSceneObject, ClientUid owner,
            Func<ClientUid, NetWorldPlayer> playerProvider)
        {
            m_PlayerProvider = playerProvider;

            Uid = uid;
            PrefabId = prefabId ?? string.Empty;
            IsSceneObject = isSceneObject;
            OwnerUid = owner;

            StateChangeType = ObjectStateChangeRequestType.UnreliableUpdate;

            RPCBuffer = new NetObjectRPCBuffer();
            DataCollection = new NetObjectDataCollection(() => IsOwnedByLocalPlayer);
        }

        internal NetObjectState(NetObjectStateSnapshot snapshot, Func<ClientUid, NetWorldPlayer> playerProvider) :
            this(snapshot.ObjectUID, snapshot.PrefabId, snapshot.IsSceneObject, snapshot.OwnerUID, playerProvider) =>
            ChangeObjectRemotely(snapshot);

        internal void InvalidateOwner() =>
            OwnerUid = ClientUid.Invalid;

        internal void Invalidate()
        {
            var wasValid = IsValid;

            Uid = NetObjectUid.Invalid;
            InvalidateOwner();

            PrefabId = string.Empty;
            IsSceneObject = false;
            StateChangeType = ObjectStateChangeRequestType.None;

            RPCBuffer.Clear();
            DataCollection.Clear();
            m_RPCWithoutAuthorityRequests.Clear();

            m_StateChangeTypeQueue.Clear();
            m_RPCBufferUpdateQueue.Clear();
            m_RPCWithoutAuthorityQueue.Clear();
            m_PersistentRPCToRemoveQueue.Clear();

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
            var checkOwner = snapshot.StateChangeType < ObjectStateChangeRequestType.ReliableUpdateOwnerChange;

            if (snapshot.StateChangeType == ObjectStateChangeRequestType.ReliableUpdateOwnerChange)
                Debug.LogError($"_______ {snapshot.StateChangeType} {Uid} : {PrefabId}");

            if (checkOwner && IsOwnedByLocalPlayer)
            {
                Debug.LogError(
                    $"Unable to restore local object with '{Uid}' : '{PrefabId}' Change type: '{snapshot.StateChangeType}'. This can be normal in some cases.");
                return;
            }

            UpdateOwnerInternal(snapshot.OwnerUID);

            StateChangeType = snapshot.StateChangeType;
            RPCBuffer.RestoreFormSnapshot(snapshot.RPCState);
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
            m_RPCWithoutAuthorityRequests.Clear();

            // Clear temporal rpc buffer.
            RPCBuffer.ClearTemporal();

            // Return object to regular UDP pipeline.
            StateChangeType = ObjectStateChangeRequestType.UnreliableUpdate;

            // New RPC requests.
            while (m_RPCBufferUpdateQueue.TryDequeue(out var newRpcState))
                RPCBuffer.AddOrUpdate(newRpcState);

            // Persistent RPC remove request.
            while (m_PersistentRPCToRemoveQueue.TryDequeue(out var rpcHash))
                RPCBuffer.RemovePersistent(rpcHash);

            // Update no-authority RPC requests. Note that is not a part of the state.
            while (m_RPCWithoutAuthorityQueue.TryDequeue(out var rpcWithoutAuthorityRequest))
                m_RPCWithoutAuthorityRequests.Add(rpcWithoutAuthorityRequest);

            // State change type requests.
            while (m_StateChangeTypeQueue.TryDequeue(out var newRequestType))
            {
                if (StateChangeType < newRequestType)
                    StateChangeType = newRequestType;
            }
        }

        internal void ScheduleUpdate(ObjectStateChangeRequestType requestType)
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
                m_StateChangeTypeQueue.Enqueue(ObjectStateChangeRequestType.ReliableUpdateOwnerChange);
            }
            else
                Debug.LogError($"Unable to change owner, invalid id {newOwner}");
        }

        public byte[] Serialize()
        {
            m_Writer.SeekZero();
            ToSnapshot().Serialize(m_Writer);

            return m_Writer.ToArray();
        }

        public override string ToString() =>
            $"{Uid}:{PrefabId} " +
            $"s:{IsSceneObject} " +
            $"st:{StateChangeType} " +
            $"own:{OwnerUid}:{(Owner == null ? "null" : Owner.ToString())} " +
            $"l:{IsOwnedByLocalPlayer} " +
            $"m:{IsOwnedByMasterClient} " +
            $"rpc: t:{RPCBuffer.TemporalBuffer.Count} p:{RPCBuffer.PersistentBuffer.Count}";

        internal void ScheduleRPCWithoutAuthority(NoAuthorityRPCType type, int hashCode, byte[] data, DeliveryMethod deliveryMethod) =>
            m_RPCWithoutAuthorityQueue.Enqueue(new RPCWithoutAuthorityRequest(Uid, type, hashCode, data, deliveryMethod));

        internal void ScheduleRPC(int hashCode, byte[] data, bool persistent, RPCBufferChangeMethod bufferChangeMethod, DeliveryMethod deliveryMethod)
        {
            m_RPCBufferUpdateQueue.Enqueue(new NetObjectRPCBuffer.NewPRCData
            {
                HashCode = hashCode,
                Data = data,
                Persistent = persistent,
                BufferChangeMethod = bufferChangeMethod,
            });

            if (deliveryMethod == DeliveryMethod.Reliable)
                ScheduleUpdate(ObjectStateChangeRequestType.ReliableUpdate);
        }

        internal void ScheduleRemovePersistentRPC(int hashCode) =>
            m_PersistentRPCToRemoveQueue.Enqueue(hashCode);
        
        internal int GetVersionHash()
        {
            var hash = 13;
            hash = hash * 7 + Uid.GetHashCode();
            hash = hash * 7 + PrefabId.GetHashCode();
            hash = hash * 7 + IsSceneObject.GetHashCode();
            hash = hash * 7 + OwnerUid.GetHashCode();
            hash = hash * 7 + StateChangeType.GetHashCode();
            hash = hash * 7 + RPCBuffer.GetVersionHash();
            hash = hash * 7 + DataCollection.GetVersionHash();
            return hash;
        }
    }
}