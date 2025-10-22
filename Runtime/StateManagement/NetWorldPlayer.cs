using Core.Networking;
using System;
using System.Collections.Generic;

public interface IReadOnlyNetWorldPlayer
{
    ClientUID UID { get; }
    string Name { get; }
    bool IsReady { get; }
    bool IsValid { get; }
    bool IsLocal { get; }
    bool IsMaster { get; }
    NetObjectUID MainTagObjectUID { get; }
    IReadOnlyCollection<NetObjectUID> TagObjectUIDs { get; }
    NetWorldPlayerSnapshot ToSnapshot();
    int GetHashCode();
    string ToString();
}

public class NetWorldPlayer : IReadOnlyNetWorldPlayer, IComparable<NetWorldPlayer>, IEquatable<NetWorldPlayer>
{
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
    private string m_Name;

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
    private bool m_IsReady;

    public UserDeviceType.Platform Platform
    {
        get => m_Platform;
        set
        {
            if (!IsLocal)
                throw new Exception("Trying to change platform without authority, this is not allowed. Player id: " + UID);
            m_Platform = value;
        }
    }
    private UserDeviceType.Platform m_Platform = UserDeviceType.Platform.Unknown;

    public int RTTMs
    {
        get => m_RTTMS;
        set
        {
            if (!IsLocal)
                throw new Exception("Trying to change RTT value without authority, this is not allowed. Player id: " + UID);
            m_RTTMS = value;
        }
    }
    private int m_RTTMS;
    
    public IProperties Properties => m_Properties;
    private readonly Properties m_Properties;

    public IReadOnlyCollection<NetObjectUID> TagObjectUIDs => m_TagObjectUIDs;
    private HashSet<NetObjectUID> m_TagObjectUIDs;
    
    public NetObjectUID MainTagObjectUID { get; private set; } = NetObjectUID.Invalid;
    #endregion

    public void RegisterTagObject(NetObjectUID uid, bool main = false)
    {
        if (!IsLocal)
            throw new InvalidOperationException("Trying to register tag object without authority, this is not allowed. Player id: " + uid);

        if (!uid.IsValid)
            throw new InvalidOperationException("Trying to register invalid object UID as tag object");

        m_TagObjectUIDs.Add(uid);
        if (main)
            MainTagObjectUID = uid;
    }

    public void UnregisterTagObject(NetObjectUID uid)
    {
        if (!IsLocal)
            throw new InvalidOperationException("Trying to unregister tag object without authority, this is not allowed. Player id: " + uid);

        if (!uid.IsValid)
            throw new InvalidOperationException("Trying to register invalid object UID as tag object");

        m_TagObjectUIDs.Remove(uid);
        if (MainTagObjectUID == uid)
            MainTagObjectUID = NetObjectUID.Invalid;
    }
    
    public bool IsValid => UID.IsValid;
    public bool IsLocal => IsValid && UID.Equals(m_LocalPlayer());
    public bool IsMaster => IsValid && UID.Equals(m_MasterPlayer());

    private readonly Func<ClientUID> m_LocalPlayer;
    private readonly Func<ClientUID> m_MasterPlayer;
    private readonly NetWriter m_Writer = new ();

    public NetWorldPlayer(ClientUID uid, string name, Func<ClientUID> localPlayer, Func<ClientUID> masterPlayer)
    {
        UID = uid;
        m_Name = name;
        m_LocalPlayer = localPlayer;
        m_MasterPlayer = masterPlayer;

        m_Properties = new Properties(writePermission: () => IsLocal);
        m_TagObjectUIDs = new HashSet<NetObjectUID>();
    }

    public void RestoreFromSnapshot(NetWorldPlayerSnapshot snapshot)
    {
        UID = snapshot.UID;
        m_Name = snapshot.Name;
        m_IsReady = snapshot.IsInGameScene;
        m_Platform = snapshot.Platform;
        m_RTTMS = snapshot.RTTMs;
        m_TagObjectUIDs = snapshot.TagObjectUIDs;

        m_Properties.SetDataFromSnapshot(snapshot.Properties);
    }

    public bool HasTagObject(NetObjectUID uid) => uid.IsValid && m_TagObjectUIDs.Contains(uid);
    public bool HasTagObject(NetObjectState state) => state != null && HasTagObject(state.UID);

    public NetWorldPlayerSnapshot ToSnapshot() => NetWorldPlayerSnapshot.FormPlayer(this);

    public override string ToString() => $"{UID}:{Name} l:{IsLocal} m:{IsMaster}";
    public int CompareTo(NetWorldPlayer other) => UID.CompareTo(other.UID);
    public bool Equals(NetWorldPlayer other) => other != null && UID.Equals(other.UID);

    public byte[] Serialize()
    {
        m_Writer.SeekZero();
        ToSnapshot().Serialize(m_Writer);
        return m_Writer.ToArray();
    }

    public override int GetHashCode() => 
        UID.GetHashCode();
}