using Core.Networking;
using System;
using System.Collections.Generic;
using UnityEngine;

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

//TODO;
public interface IReadOnlyNetObjectState
{
    bool IsValid { get; }
    IReadOnlyNetWorldPlayer Owner { get; }
    bool IsOwnedByLocalPlayer { get; }
    bool IsOwnedByMasterClient { get; }
    bool IsOwnersTagObject { get; }
    bool IsOwnedBy(ClientUID uid);
    IReadOnlyCollection<RPCWithoutAuthorityRequest> RPCWithoutAuthorityRequests { get; }
    NetObjectStateSnapshot ToSnapshot();
    int GetHashCode();
    string ToString();
}

public class NetObjectState : IReadOnlyNetObjectState, IComparable<NetObjectState>, IEquatable<NetObjectState>
{
    public event Action OnOwnerChanged = delegate { };
    public event Action<ObjectStateChangeRequestType> LocalUpdateRequest = delegate { };
    public event Action Invalidated = delegate { };

    readonly Func<ClientUID, NetWorldPlayer> m_PlayerProvider;
    readonly Queue<Action<float>> m_UpdateQueue;

    public bool IsValid => UID.IsValid;

    #region SYNCRONIZED STATE
    public NetObjectUID UID { get; private set; }
    public string PrefabId { get; private set; }
    public bool IsSceneObject { get; private set; }
    public ClientUID OwnerUID { get; private set; }
    public ObjectStateChangeRequestType StateChangeType { get; private set; }
    public IReadOnlyNetObjectRPCState RPCBuffer => m_RPCBuffer;
    readonly NetObjectRPCState m_RPCBuffer;
    public IReadOnlyNetObjectDataCollection DataCollection => m_DataCollection;
    readonly NetObjectDataCollection m_DataCollection;
    #endregion

    public override int GetHashCode() =>
        HashCode.Combine(UID, PrefabId, IsSceneObject, OwnerUID, StateChangeType, m_RPCBuffer, m_DataCollection);

    public bool IsOwnedByLocalPlayer => Owner != null && Owner.IsLocal;
    public bool IsOwnedByMasterClient => Owner != null && Owner.IsMaster;

    public bool IsOwnedBy(ClientUID uid) => OwnerUID == uid;
    IReadOnlyNetWorldPlayer IReadOnlyNetObjectState.Owner => Owner;
    public NetWorldPlayer Owner => m_PlayerProvider(OwnerUID);

    public bool IsOwnersTagObject => Owner != null && Owner.HasTagObject(UID);

    public IReadOnlyCollection<RPCWithoutAuthorityRequest> RPCWithoutAuthorityRequests => m_RPCWithoutAuthorityRequests;

    readonly List<RPCWithoutAuthorityRequest> m_RPCWithoutAuthorityRequests;

    NetWriter m_Writer;

    public NetObjectState(NetObjectUID uid, string prefabId, bool isSceneObject, ClientUID owner, Func<ClientUID, NetWorldPlayer> playerProvier)
    {
        m_PlayerProvider = playerProvier;
        m_UpdateQueue = new Queue<Action<float>>();

        UID = uid;
        PrefabId = prefabId ?? string.Empty;
        IsSceneObject = isSceneObject;
        OwnerUID = owner;

        StateChangeType = ObjectStateChangeRequestType.UnreliableUpdate;

        m_RPCBuffer = new NetObjectRPCState();
        m_DataCollection = new NetObjectDataCollection(() => IsOwnedByLocalPlayer);
        m_RPCWithoutAuthorityRequests = new List<RPCWithoutAuthorityRequest>();
    }

    public NetObjectState(NetObjectStateSnapshot snapshot, Func<ClientUID, NetWorldPlayer> playerProvier) :
        this(snapshot.ObjectUID, snapshot.PrefabId, snapshot.IsSceneObject, snapshot.OwnerUID, playerProvier) => ChangeObjectRemotely(snapshot);

    public void InvalidateOwner() => OwnerUID = ClientUID.Invalid;

    public void Invalidate()
    {
        var wasValid = IsValid;

        UID = NetObjectUID.Invalid;
        InvalidateOwner();

        PrefabId = string.Empty;
        IsSceneObject = false;
        StateChangeType = ObjectStateChangeRequestType.None;

        m_UpdateQueue.Clear();

        m_RPCBuffer.Clear();
        m_DataCollection.Clear();
        m_RPCWithoutAuthorityRequests.Clear();

        if (wasValid)
            Invalidated();

        Invalidated = null;
        LocalUpdateRequest = null;
        Invalidated = null;
    }

    public void ChangeObjectViewDataLocaly(byte[] data, bool initialization = false)
    {
        if (!IsOwnedByLocalPlayer)
            throw new InvalidOperationException($"No authority to change {UID} object with owner {OwnerUID}");

        var viewData = DataCollection.GetOrCreateData<NetObjectViewStateData>();
        viewData.SetData(data, initialization);
    }

    public bool PrepareForSerialization()
    {
        if (!IsOwnedByLocalPlayer)
            throw new InvalidOperationException($"No authority to change {UID} object with owner {OwnerUID}");

        var lastHashCode = GetHashCode();
        LocalUpdateRequest(StateChangeType);
        var newHashCode = GetHashCode();
        var changed = newHashCode != lastHashCode;
        return changed;
    }

    public void ChangeObjectRemotely(NetObjectStateSnapshot snapshot)
    {
        var checkOwner = snapshot.StateChangeType < ObjectStateChangeRequestType.ReliableUpdateOwnerChange;

        if(snapshot.StateChangeType == ObjectStateChangeRequestType.ReliableUpdateOwnerChange)
            Debug.LogError($"_______ {snapshot.StateChangeType} {UID} : {PrefabId}");

        if (checkOwner && IsOwnedByLocalPlayer)
        {
            Debug.LogError($"Unable to restore local object with {UID} : {PrefabId} Change type: {snapshot.StateChangeType}. This can be normal in some cases");
            return;
        }

        UpdateOwnerInternal(snapshot.OwnerUID);

        StateChangeType = snapshot.StateChangeType;
        m_RPCBuffer.RestoreFormSnapshot(snapshot.RPCState);
        m_DataCollection.RestoreFromSnapShot(snapshot.DataState);
    }

    void UpdateOwnerInternal(ClientUID newOwner)
    {
        var previousOwner = OwnerUID;
        OwnerUID = newOwner;

        if (previousOwner != OwnerUID)
            OnOwnerChanged();
    }

    public NetObjectStateSnapshot ToSnapshot() => NetObjectStateSnapshot.FormState(this);

    void ScheduleStateChange(ObjectStateChangeRequestType requestType) => 
        ScheduleOnUpdate(() =>
        {
            if (StateChangeType < requestType)
                StateChangeType = requestType;
        });

    void ScheduleOnUpdate(Action a) =>
        ScheduleOnUpdate(delta => a());

    void ScheduleOnUpdate(Action<float> a) =>
        m_UpdateQueue.Enqueue(a);

    public void OnBeforeUpdateWorldState(float worldDelta)
    {
        //Clear rpc request without authority;
        m_RPCWithoutAuthorityRequests.Clear();

        //Clear temporal rpc buffer;
        m_RPCBuffer.ClearTemporal();

        //Return object to regular UDP pipeline;
        StateChangeType = ObjectStateChangeRequestType.UnreliableUpdate;

        //Invoke update queue;
        while (m_UpdateQueue.Count > 0)
            m_UpdateQueue.Dequeue()(worldDelta);
    }

    public void ScheduleUpdate(ObjectStateChangeRequestType requestType)
    {
        if (!IsOwnedByLocalPlayer)
            throw new InvalidOperationException($"No authority to change {UID} object with owner {OwnerUID}");

        if (StateChangeType > requestType)
            Debug.LogError($"Unable to set request {requestType} because it has less priority than current request {StateChangeType}");
        else
            ScheduleStateChange(requestType);
    }

    public void ScheduleOwnerChanged(ClientUID newOwner)
    {
        if (!IsSceneObject)
            throw new Exception("You can manage owners only for scene objects");

        if (newOwner.IsValid)
        {
            UpdateOwnerInternal(newOwner);
            ScheduleStateChange(ObjectStateChangeRequestType.ReliableUpdateOwnerChange);
        }
        else
            Debug.LogError($"Unable to change owner, invalid id {newOwner}");
    }

    public byte[] Serialize()
    {
        if (m_Writer == null)
            m_Writer = new NetWriter();

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


    public int CompareTo(NetObjectState other) => UID.CompareTo(other.UID);
    public bool Equals(NetObjectState other) => UID.Equals(other.UID);

    public void ScheduleRPCWithoutAuthority(NoAuthorityRPCType type, int hashCode, byte[] data, DeliveryIntent deliveryIntent) =>
        ScheduleOnUpdate(() => m_RPCWithoutAuthorityRequests.Add(new RPCWithoutAuthorityRequest(UID, type, hashCode, data, deliveryIntent)));
        
    public void ScheduleRPC(int hashCode, byte[] data, bool persistent, RPCBufferChangeMethod bufferChangeMethod, DeliveryIntent deliveryIntent) =>
        ScheduleOnUpdate(() =>
        {
            m_RPCBuffer.AddOrUpdate(hashCode, data, persistent, bufferChangeMethod);
            if (deliveryIntent == DeliveryIntent.Reliable)
                ScheduleUpdate(ObjectStateChangeRequestType.ReliableUpdate);
        });

    public void ScheduleRemovePersistentRPC(int hashCode) =>
        ScheduleOnUpdate(() => m_RPCBuffer.RemovePersistent(hashCode));
}

public struct RPCWithoutAuthorityState
{
    public readonly NetObjectUID ObjectUID;
    public readonly NoAuthorityRPCType Type;
    public readonly RPCState RPCState;

    public bool IsValid => ObjectUID.IsValid && RPCState.IsValid;

    public RPCWithoutAuthorityState(NetObjectUID objectUID, NoAuthorityRPCType type, int hashCode, byte[] payload)
    {
        ObjectUID = objectUID;
        Type = type;
        RPCState = new RPCState(hashCode, RPCStateUIDAllocator.AllocateUID(), payload);
    }

    public RPCWithoutAuthorityState(NetReader reader)
    {
        ObjectUID = reader.ReadInstanceId();
        Type = (NoAuthorityRPCType)reader.ReadByte();
        RPCState = RPCState.FromSnapshot(RPCStateSnapshot.FromReader(reader));
    }

    public void Serialize(NetWriter writer)
    {
        writer.WriteInstanceId(ObjectUID);
        writer.Write((byte)Type);
        RPCState.ToSnapshot().Serialize(writer);
    }

    public byte[] ToData()
    {
        var writer = NetWriter.Get();
        Serialize(writer);
        return writer.ToArray();
    }

    public static RPCWithoutAuthorityState FromReader(NetReader reader) =>
        new(reader);
}

public struct RPCWithoutAuthorityRequest
{
    public readonly RPCWithoutAuthorityState State;
    public readonly DeliveryIntent DeliveryIntent;

    public RPCWithoutAuthorityRequest(NetObjectUID objectUID, NoAuthorityRPCType type, int hashCode, byte[] payload, DeliveryIntent deliveryIntent)
    {
        State = new RPCWithoutAuthorityState(objectUID, type, hashCode, payload);
        DeliveryIntent = deliveryIntent;
    }
}
