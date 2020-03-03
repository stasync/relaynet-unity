using Core.Networking;
using System;
using UnityEngine;

public enum ObjectStateChangeRequestType : byte
{
    None,
    UnreliableUpdate,
    ReliableUpdate,
    InitializationUpdate,
    RemoveUpdate,
}

public class NetObjectState : IComparable<NetObjectState>, IEquatable<NetObjectState>
{
    public event Action OnOwnerChanged = delegate { };
    public event Action<bool> LocalUpdateRequest = delegate { };

    readonly Func<ClientUID, NetWorldPlayer> m_PlayerProvider;

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

    NetWriter m_Writer;

    public NetObjectState(NetObjectUID uid, string prefabId, bool isSceneObject, ClientUID owner, Func<ClientUID, NetWorldPlayer> playerProvier)
    {
        m_PlayerProvider = playerProvier;

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
        //Return object to regular UDP pipeline;
        StateChangeType = ObjectStateChangeRequestType.UnreliableUpdate;
        ChangedLocaly = false;
    }

    public void InvalidateOwner() => OwnerUID = ClientUID.Invalid;

    public void ChangeObjectRemotely(ObjectUpdateSnapshot snapshot)
    {
        if (IsOwnedByLocalPlayer)
            throw new InvalidOperationException($"Unable to restore local object with {UID}");

        var previousOwner = OwnerUID;
        OwnerUID = snapshot.OwnerUID;
        StateChangeType = snapshot.StateChangeType;
        Data = snapshot.Data;

        if (previousOwner != snapshot.OwnerUID)
            OnOwnerChanged();
    }

    public ObjectUpdateSnapshot ToSnapshot() => ObjectUpdateSnapshot.FormState(this);

    public void ScheduleUpdate(ObjectStateChangeRequestType requestType)
    {
        if (!IsOwnedByLocalPlayer)
            throw new InvalidOperationException($"No authority to change {UID} object with owner {OwnerUID}");

        if (StateChangeType > requestType)
            Debug.LogError($"Unable to set request {requestType} because it has less priority than current request {StateChangeType}");
        else
            StateChangeType = requestType;
    }

    public void ScheduleOwnerChanged(ClientUID newOwner)
    {
        if (!IsSceneObject)
            throw new Exception("You can manage owners only for scene objects");

        if (StateChangeType > ObjectStateChangeRequestType.ReliableUpdate)
            Debug.LogError($"Unable to set request {ObjectStateChangeRequestType.ReliableUpdate} because it has less priority than current request {StateChangeType}");
        else
        {
            StateChangeType = ObjectStateChangeRequestType.ReliableUpdate;
            OwnerUID = newOwner;
        }
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
}