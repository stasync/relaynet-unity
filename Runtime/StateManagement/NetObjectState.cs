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
    RemoveUpdate,
}

public class NetObjectState : IComparable<NetObjectState>, IEquatable<NetObjectState>
{
    public event Action OnOwnerChanged = delegate { };
    public event Action<bool> LocalUpdateRequest = delegate { };

    readonly Func<ClientUID, NetWorldPlayer> m_PlayerProvider;
    readonly Queue<Action> m_UpdateQueue;

    #region SYNCRONIZED STATE
    public readonly NetObjectUID UID;
    public readonly string PrefabId;
    public readonly bool IsSceneObject;
    public ClientUID OwnerUID { get; private set; }
    public ObjectStateChangeRequestType StateChangeType { get; private set; }
    public bool RequireRemoving => StateChangeType == ObjectStateChangeRequestType.RemoveUpdate;
    public byte[] Data { get; private set; }
    public IReadOnlyNetObjectRPCState RPCBuffer => m_RPCBuffer;
    NetObjectRPCState m_RPCBuffer;
    #endregion

    int m_LastHashCode;

    public override int GetHashCode()
    {
        var hash = 13;
        hash = (hash * 7) + UID.GetHashCode();
        hash = (hash * 7) + PrefabId.GetHashCode();
        hash = (hash * 7) + IsSceneObject.GetHashCode();
        hash = (hash * 7) + OwnerUID.GetHashCode();
        hash = (hash * 7) + StateChangeType.GetHashCode();
        foreach (var d in Data)
            hash = (hash * 7) + d.GetHashCode();
        hash = (hash * 7) + m_RPCBuffer.GetHashCode();
        return hash;
    }

    public bool IsOwnedByLocalPlayer => Owner != null && Owner.IsLocal;
    public bool IsOwnedByMasterClient => Owner != null && Owner.IsMaster;

    public bool IsOwnedBy(ClientUID uid) => OwnerUID == uid;
    public NetWorldPlayer Owner => m_PlayerProvider(OwnerUID);

    public IReadOnlyCollection<RPCWithoutAuthorityRequest> RPCWithoutAuthorityRequests => m_RPCWithoutAuthorityRequests;
    List<RPCWithoutAuthorityRequest> m_RPCWithoutAuthorityRequests = new List<RPCWithoutAuthorityRequest>();

    NetWriter m_Writer;

    public NetObjectState(NetObjectUID uid, string prefabId, bool isSceneObject, ClientUID owner, Func<ClientUID, NetWorldPlayer> playerProvier)
    {
        m_PlayerProvider = playerProvier;
        m_UpdateQueue = new Queue<Action>();

        UID = uid;
        PrefabId = prefabId;
        IsSceneObject = isSceneObject;
        OwnerUID = owner;

        StateChangeType = ObjectStateChangeRequestType.UnreliableUpdate;
        Data = new byte[] { };

        m_RPCBuffer = new NetObjectRPCState();
    }

    public NetObjectState(ObjectUpdateSnapshot snapshot, Func<ClientUID, NetWorldPlayer> playerProvier) :
        this(snapshot.ObjectUID, snapshot.PrefabId, snapshot.IsSceneObject, snapshot.OwnerUID, playerProvier) => ChangeObjectRemotely(snapshot);

    public void ChangeObjectDataLocaly(byte[] data)
    {
        if (!IsOwnedByLocalPlayer)
            throw new InvalidOperationException($"No authority to change {UID} object with owner {OwnerUID}");

        Data = data;
    }

    public bool PrepareForSerialization()
    {
        if (!IsOwnedByLocalPlayer)
            throw new InvalidOperationException($"No authority to change {UID} object with owner {OwnerUID}");

        LocalUpdateRequest(StateChangeType == ObjectStateChangeRequestType.InitializationUpdate);

        var newHashCode = GetHashCode();
        var changed = !newHashCode.Equals(m_LastHashCode);
        m_LastHashCode = newHashCode;

        return changed;
    }

    public void InvalidateOwner() => OwnerUID = ClientUID.Invalid;

    public void ChangeObjectRemotely(ObjectUpdateSnapshot snapshot)
    {
        var checkOwner = snapshot.StateChangeType < ObjectStateChangeRequestType.ReliableUpdateOwnerChange;

        if(snapshot.StateChangeType == ObjectStateChangeRequestType.ReliableUpdateOwnerChange)
            Debug.LogError($"_______ {snapshot.StateChangeType} {UID} : {PrefabId}");

        if (checkOwner && IsOwnedByLocalPlayer)
        {
            Debug.LogError($"Unable to restore local object with {UID} : {PrefabId} Change type: {snapshot.StateChangeType}. This can be normal in some cases");
            return;
        }

        var previousOwner = OwnerUID;
        OwnerUID = snapshot.OwnerUID;
        StateChangeType = snapshot.StateChangeType;
        Data = snapshot.Data;
        m_RPCBuffer.RestoreFormSnapshot(snapshot.RPCState);

        if (previousOwner != snapshot.OwnerUID)
            OnOwnerChanged();
    }

    public ObjectUpdateSnapshot ToSnapshot() => ObjectUpdateSnapshot.FormState(this);

    void ScheduleStateChange(ObjectStateChangeRequestType requestType) => 
        ScheduleOnUpdate(() =>
        {
            if (StateChangeType > requestType)
                Debug.LogError($"Unable to set request {requestType} because it has less priority than current request {StateChangeType}");
            else
                StateChangeType = requestType;
        });

    void ScheduleOnUpdate(Action a) => m_UpdateQueue.Enqueue(a);

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
            m_UpdateQueue.Dequeue()();
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

        if(newOwner.IsValid)
        {
            OwnerUID = newOwner;
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

    public bool IsValid => ObjectUID.IsValid && !RPCState.Hash.Equals(0) && RPCState.Payload != null;

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
        var writer = new NetWriter();
        Serialize(writer);
        return writer.ToArray();
    }

    public static RPCWithoutAuthorityState FromReader(NetReader reader) => new RPCWithoutAuthorityState(reader);
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