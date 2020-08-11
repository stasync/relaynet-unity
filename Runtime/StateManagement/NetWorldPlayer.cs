using Core.Networking;
using System;
using System.Collections.Generic;

public class NetWorldPlayer : IComparable<NetWorldPlayer>, IEquatable<NetWorldPlayer>
{
    readonly NetWriter m_PlayerWriter;
    readonly NetReader m_PlayerReader;

    #region STATE
    public ClientUID UID { get; private set; }

    #region Values
    public string Name
    {
        get => m_Name;
        set
        {
            if (!IsLocal)
                throw new Exception("Trying to change remote player name, this is not allowed. Player id: " + UID);
            m_Name = value;
        }
    }
    string m_Name;

    public bool IsReady
    {
        get => m_IsReady;
        set
        {
            if (!IsLocal)
                throw new Exception("Trying to change is ready on remote player, this is not allowed. Player id: " + UID);
            m_IsReady = value;
        }
    }
    bool m_IsReady;

    public GSDevice.Platform Platform
    {
        get => m_Platform;
        set
        {
            if (!IsLocal)
                throw new Exception("Trying to change platform without authority, this is not allowed. Player id: " + UID);
            m_Platform = value;
        }
    }
    GSDevice.Platform m_Platform = GSDevice.Platform.Unknown;

    public int RTT
    {
        get => m_RTT;
        set
        {
            if (!IsLocal)
                throw new Exception("Trying to change RTT value without authority, this is not allowed. Player id: " + UID);
            m_RTT = value;
        }
    }
    int m_RTT;

    public IReadOnlyCollection<NetObjectUID> TagObjectUIDs => m_TagObjectUIDs;
    HashSet<NetObjectUID> m_TagObjectUIDs;
    public NetObjectUID MainTagObjectUID { get; private set; } = NetObjectUID.Invalid;

    public void RegisterTagObject(NetObjectUID UID, bool main = false)
    {
        if (!IsLocal)
            throw new InvalidOperationException("Trying to register tag object without authority, this is not allowed. Player id: " + UID);

        if (!UID.IsValid)
            throw new InvalidOperationException("Trying to register invalid object UID as tag object");

        m_TagObjectUIDs.Add(UID);
        if (main)
            MainTagObjectUID = UID;
    }

    public void UnregisterTagObject(NetObjectUID UID)
    {
        if (!IsLocal)
            throw new InvalidOperationException("Trying to unregister tag object without authority, this is not allowed. Player id: " + UID);

        if (!UID.IsValid)
            throw new InvalidOperationException("Trying to register invalid object UID as tag object");

        m_TagObjectUIDs.Remove(UID);
        if (MainTagObjectUID == UID)
            MainTagObjectUID = NetObjectUID.Invalid;
    }

    public IProperties Properties => m_Properties;
    readonly Properties m_Properties;
    #endregion

    #endregion

    public bool IsValid => UID.IsValid;
    public bool IsLocal => IsValid && UID.Equals(m_LocalPlayer());
    public bool IsMaster => IsValid && UID.Equals(m_MasterPlayer());

    readonly Func<ClientUID> m_LocalPlayer;
    readonly Func<ClientUID> m_MasterPlayer;

    public NetWorldPlayer(ClientUID uid, string name, Func<ClientUID> localPlayer, Func<ClientUID> masterPlayer)
    {
        UID = uid;
        m_Name = name;
        m_LocalPlayer = localPlayer;
        m_MasterPlayer = masterPlayer;

        m_Properties = new Properties(() => IsLocal);

        m_PlayerReader = new NetReader();
        m_PlayerWriter = new NetWriter();

        m_TagObjectUIDs = new HashSet<NetObjectUID>();
    }

    public void RestoreFromSnapshot(NetWorldPlayerSnapshot snapshot)
    {
        UID = snapshot.UID;
        m_Name = snapshot.Name;
        m_IsReady = snapshot.IsInGameScene;
        m_Platform = snapshot.Platform;
        m_RTT = snapshot.RTT;
        m_TagObjectUIDs = snapshot.TagObjectUIDs;

        m_Properties.SetDataFromSnapshot(snapshot.Properties);
    }

    public bool HasTagObject(NetObjectUID uid) => uid.IsValid && m_TagObjectUIDs.Contains(uid);
    public bool HasTagObject(NetObjectState state) => state != null && HasTagObject(state.UID);

    public NetWorldPlayerSnapshot ToSnapshot() => NetWorldPlayerSnapshot.FormPlayer(this);

    public override string ToString() => $"{UID}:{Name} l:{IsLocal} m:{IsMaster}";
    public int CompareTo(NetWorldPlayer other) => UID.CompareTo(other.UID);
    public bool Equals(NetWorldPlayer other) => UID.Equals(other.UID);

    public byte[] Serialize()
    {
        m_PlayerWriter.SeekZero();
        ToSnapshot().Serialize(m_PlayerWriter);
        return m_PlayerWriter.ToArray();
    }

    public override int GetHashCode() => UID.GetHashCode();
}