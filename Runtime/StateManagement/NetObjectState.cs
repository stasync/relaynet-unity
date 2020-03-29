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
    #endregion

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
        return hash;
    }

    public bool IsOwnedByLocalPlayer => Owner != null && Owner.IsLocal;
    public bool IsOwnedByMasterClient => Owner != null && Owner.IsMaster;

    public bool IsOwnedBy(ClientUID uid) => OwnerUID == uid;
    public NetWorldPlayer Owner => m_PlayerProvider(OwnerUID);

    public IReadOnlyCollection<RPCWithoutAuthorityRequest> RPCRequests => m_RPCRequests;
    List<RPCWithoutAuthorityRequest> m_RPCRequests = new List<RPCWithoutAuthorityRequest>();

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
    }

    public NetObjectState(ObjectUpdateSnapshot snapshot, Func<ClientUID, NetWorldPlayer> playerProvier) :
        this(snapshot.ObjectUID, snapshot.PrefabId, snapshot.IsSceneObject, snapshot.OwnerUID, playerProvier) => ChangeObjectRemotely(snapshot);

    public bool ChangedLocaly { get; private set; }
    public void ChangeObjectDataLocaly(byte[] data)
    {
        if (!IsOwnedByLocalPlayer)
            throw new InvalidOperationException($"No authority to change {UID} object with owner {OwnerUID}");

        var hashCode = GetHashCode();
        Data = data;
        ChangedLocaly = hashCode != GetHashCode();
    }

    public void RequestUpdate()
    {
        if (!IsOwnedByLocalPlayer)
            throw new InvalidOperationException($"No authority to change {UID} object with owner {OwnerUID}");

        LocalUpdateRequest(StateChangeType == ObjectStateChangeRequestType.InitializationUpdate);
    }

    public void FinalizeIteration()
    {
        m_RPCRequests.Clear();
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

        if (previousOwner != snapshot.OwnerUID)
            OnOwnerChanged();
    }

    public ObjectUpdateSnapshot ToSnapshot() => ObjectUpdateSnapshot.FormState(this);

    void ScheduleStateChange(ObjectStateChangeRequestType requestType) => ScheduleOnUpdate(() =>
    {
        if (StateChangeType > requestType)
            Debug.LogError($"Unable to set request {requestType} because it has less priority than current request {StateChangeType}");
        else
            StateChangeType = requestType;
    });

    void ScheduleOnUpdate(Action a) => m_UpdateQueue.Enqueue(a);

    public void OnBeforeUpdateWorldState(float worldDelta)
    {
        //Return object to regular UDP pipeline;
        StateChangeType = ObjectStateChangeRequestType.UnreliableUpdate;
        ChangedLocaly = false;

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

        ObjectUpdateSnapshot
            .FormState(this)
            .Serialize(m_Writer);

        return m_Writer.ToArray();
    }

    public override string ToString() => $"{UID}:{PrefabId} " +
            $"s:{IsSceneObject} " +
            $"st:{StateChangeType} " +
            $"own:{OwnerUID}:{(Owner == null ? "null" : Owner.ToString())} " +
            $"l:{IsOwnedByLocalPlayer} " +
            $"m:{IsOwnedByMasterClient}";


    public int CompareTo(NetObjectState other) => UID.CompareTo(other.UID);
    public bool Equals(NetObjectState other) => UID.Equals(other.UID);

    public void ScheduleRPCWithoutAuthority(RPCType type, int hashCode, byte[] data, DeliveryIntent deliveryIntent) =>
        m_RPCRequests.Add(new RPCWithoutAuthorityRequest(type, hashCode, data, deliveryIntent));

}

public struct RPCWithoutAuthorityRequest
{
    public readonly RPCType Type;
    public readonly int HashCode;
    public readonly byte[] Data;
    public readonly DeliveryIntent DeliveryIntent;

    public RPCWithoutAuthorityRequest(RPCType type, int hashCode, byte[] data, DeliveryIntent deliveryIntent)
    {
        Type = type;
        HashCode = hashCode;
        Data = data;
        DeliveryIntent = deliveryIntent;
    }
}