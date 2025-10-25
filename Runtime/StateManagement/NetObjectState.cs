using Core.Networking;
using System;
using System.Collections.Generic;
using UnityEngine;

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
    public readonly NetObjectUID ObjectUID;
    public readonly NoAuthorityRPCType Type;
    public readonly RPCState RPCState;

    public bool IsValid => ObjectUID.IsValid && RPCState.IsValid;

    public RPCWithoutAuthorityState(NetObjectUID objectUID, NoAuthorityRPCType type, int hashCode, byte[] payload)
    {
        ObjectUID = objectUID;
        Type = type;
        RPCState = new RPCState(hashCode, RPCStateUIDAllocator.NextUid(), payload);
    }

    private RPCWithoutAuthorityState(NetReader reader)
    {
        ObjectUID = reader.ReadInstanceId();
        Type = (NoAuthorityRPCType)reader.ReadByte();
        RPCState = RPCState.FromSnapshot(RPCStateSnapshot.FromReader(reader));
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

    public RPCWithoutAuthorityRequest(NetObjectUID objectUID, NoAuthorityRPCType type, int hashCode, byte[] payload, DeliveryMethod deliveryMethod)
    {
        State = new RPCWithoutAuthorityState(objectUID, type, hashCode, payload);
        DeliveryMethod = deliveryMethod;
    }
}

public class NetObjectState : IComparable<NetObjectState>, IEquatable<NetObjectState>
{
    public event Action OnOwnerChanged = delegate { };
    public event Action<ObjectStateChangeRequestType> LocalUpdateRequest = delegate { };
    public event Action Invalidated = delegate { };

    private readonly Func<ClientUID, NetWorldPlayer> m_PlayerProvider;

    public bool IsValid => UID.IsValid;

    #region SYNCRONIZED STATE

    public NetObjectUID UID { get; private set; }

    public string PrefabId { get; private set; }

    public bool IsSceneObject { get; private set; }

    public ClientUID OwnerUID { get; private set; }

    public ObjectStateChangeRequestType StateChangeType { get; private set; }

    public NetObjectRPCState RPCBuffer => m_RPCBuffer;
    private readonly NetObjectRPCState m_RPCBuffer;

    public NetObjectDataCollection DataCollection => m_DataCollection;
    private readonly NetObjectDataCollection m_DataCollection;

    #endregion

    public override int GetHashCode() =>
        HashCode.Combine(UID, PrefabId, IsSceneObject, OwnerUID, StateChangeType, m_RPCBuffer, m_DataCollection);

    public bool IsOwnedByLocalPlayer =>
        Owner is { IsLocal: true };

    public bool IsOwnedByMasterClient =>
        Owner is { IsMaster: true };

    public bool IsOwnedBy(ClientUID uid) => OwnerUID == uid;

    public NetWorldPlayer Owner => m_PlayerProvider(OwnerUID);

    public bool IsOwnersTagObject => Owner != null && Owner.HasTagObject(UID);

    public IReadOnlyCollection<RPCWithoutAuthorityRequest> RPCWithoutAuthorityRequests => m_RPCWithoutAuthorityRequests;
    private readonly List<RPCWithoutAuthorityRequest> m_RPCWithoutAuthorityRequests = new();

    private readonly Queue<ObjectStateChangeRequestType> m_StateChangeTypeQueue = new();
    private readonly Queue<NetObjectRPCState.NewPRCData> m_RPCBufferUpdateQueue = new();
    private readonly Queue<RPCWithoutAuthorityRequest> m_RPCWithoutAuthorityQueue = new();
    private readonly Queue<int> m_PersistentRPCToRemoveQueue = new();

    private readonly NetWriter m_Writer = new();

    internal NetObjectState(NetObjectUID uid, string prefabId, bool isSceneObject, ClientUID owner, Func<ClientUID, NetWorldPlayer> playerProvider)
    {
        m_PlayerProvider = playerProvider;

        UID = uid;
        PrefabId = prefabId ?? string.Empty;
        IsSceneObject = isSceneObject;
        OwnerUID = owner;

        StateChangeType = ObjectStateChangeRequestType.UnreliableUpdate;

        m_RPCBuffer = new NetObjectRPCState();
        m_DataCollection = new NetObjectDataCollection(() => IsOwnedByLocalPlayer);
    }

    internal NetObjectState(NetObjectStateSnapshot snapshot, Func<ClientUID, NetWorldPlayer> playerProvider) :
        this(snapshot.ObjectUID, snapshot.PrefabId, snapshot.IsSceneObject, snapshot.OwnerUID, playerProvider) =>
        ChangeObjectRemotely(snapshot);

    internal void InvalidateOwner() =>
        OwnerUID = ClientUID.Invalid;

    internal void Invalidate()
    {
        var wasValid = IsValid;

        UID = NetObjectUID.Invalid;
        InvalidateOwner();

        PrefabId = string.Empty;
        IsSceneObject = false;
        StateChangeType = ObjectStateChangeRequestType.None;

        m_RPCBuffer.Clear();
        m_DataCollection.Clear();
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
            throw new InvalidOperationException($"No authority to change '{UID}' object with owner '{OwnerUID}'.");

        var viewData = DataCollection.GetOrCreateData<NetObjectViewStateData>();
        viewData.SetData(data, initialization);
    }

    internal bool PrepareForSerialization()
    {
        if (!IsOwnedByLocalPlayer)
            throw new InvalidOperationException($"No authority to change '{UID}' object with owner '{OwnerUID}'.");

        var lastHashCode = GetHashCode();
        LocalUpdateRequest(StateChangeType);
        var newHashCode = GetHashCode();
        var changed = newHashCode != lastHashCode;
        return changed;
    }

    internal void ChangeObjectRemotely(NetObjectStateSnapshot snapshot)
    {
        var checkOwner = snapshot.StateChangeType < ObjectStateChangeRequestType.ReliableUpdateOwnerChange;

        if (snapshot.StateChangeType == ObjectStateChangeRequestType.ReliableUpdateOwnerChange)
            Debug.LogError($"_______ {snapshot.StateChangeType} {UID} : {PrefabId}");

        if (checkOwner && IsOwnedByLocalPlayer)
        {
            Debug.LogError(
                $"Unable to restore local object with '{UID}' : '{PrefabId}' Change type: '{snapshot.StateChangeType}'. This can be normal in some cases.");
            return;
        }

        UpdateOwnerInternal(snapshot.OwnerUID);

        StateChangeType = snapshot.StateChangeType;
        m_RPCBuffer.RestoreFormSnapshot(snapshot.RPCState);
        m_DataCollection.RestoreFromSnapShot(snapshot.DataState);
    }

    private void UpdateOwnerInternal(ClientUID newOwner)
    {
        var previousOwner = OwnerUID;
        OwnerUID = newOwner;

        if (previousOwner != OwnerUID)
            OnOwnerChanged();
    }

    public NetObjectStateSnapshot ToSnapshot() =>
        NetObjectStateSnapshot.FormState(this);

    internal void OnBeforeUpdateWorldState(float worldDelta)
    {
        // Clear rpc request without authority.
        m_RPCWithoutAuthorityRequests.Clear();

        // Clear temporal rpc buffer.
        m_RPCBuffer.ClearTemporal();

        // Return object to regular UDP pipeline.
        StateChangeType = ObjectStateChangeRequestType.UnreliableUpdate;

        // New RPC requests.
        while (m_RPCBufferUpdateQueue.TryDequeue(out var newRpcState))
            m_RPCBuffer.AddOrUpdate(newRpcState);

        // Persistent RPC remove request.
        while (m_PersistentRPCToRemoveQueue.TryDequeue(out var rpcHash))
            m_RPCBuffer.RemovePersistent(rpcHash);

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
            throw new InvalidOperationException($"No authority to change {UID} object with owner {OwnerUID}");

        if (StateChangeType > requestType)
            Debug.LogError($"Unable to set request {requestType} because it has less priority than current request {StateChangeType}");
        else
            m_StateChangeTypeQueue.Enqueue(requestType);
    }

    internal void ChangedOwner(ClientUID newOwner)
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

    public override string ToString() => $"{UID}:{PrefabId} " +
                                         $"s:{IsSceneObject} " +
                                         $"st:{StateChangeType} " +
                                         $"own:{OwnerUID}:{(Owner == null ? "null" : Owner.ToString())} " +
                                         $"l:{IsOwnedByLocalPlayer} " +
                                         $"m:{IsOwnedByMasterClient} " +
                                         $"rpc: t:{m_RPCBuffer.TemporalBuffer.Count} p:{m_RPCBuffer.PersistentBuffer.Count}";

    public int CompareTo(NetObjectState other) =>
        UID.CompareTo(other?.UID ?? NetObjectUID.Invalid);

    public bool Equals(NetObjectState other) =>
        UID.Equals(other?.UID ?? NetObjectUID.Invalid);

    internal void ScheduleRPCWithoutAuthority(NoAuthorityRPCType type, int hashCode, byte[] data, DeliveryMethod deliveryMethod) =>
        m_RPCWithoutAuthorityQueue.Enqueue(new RPCWithoutAuthorityRequest(UID, type, hashCode, data, deliveryMethod));

    internal void ScheduleRPC(int hashCode, byte[] data, bool persistent, RPCBufferChangeMethod bufferChangeMethod, DeliveryMethod deliveryMethod)
    {
        m_RPCBufferUpdateQueue.Enqueue(new NetObjectRPCState.NewPRCData
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
}