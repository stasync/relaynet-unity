using Core.Networking;
using System;

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

    public bool IsInGameScene
    {
        get => m_IsInGameScene;
        set
        {
            if (!IsLocal)
                throw new Exception("Trying to change scene state on remote player, this is not allowed. Player id: " + UID);
            m_IsInGameScene = value;
        }
    }
    bool m_IsInGameScene;

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

    public NetObjectUID TagObjectUID
    {
        get => m_TagObjectUID;
        set
        {
            if (!IsLocal)
                throw new Exception("Trying to change tag object uid value without authority, this is not allowed. Player id: " + UID);
            m_TagObjectUID = value;
        }
    }
    NetObjectUID m_TagObjectUID;

    public IProperties Properties => m_Properties;
    readonly SerializablePropertiesImpl m_Properties;
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

        m_Properties = new SerializablePropertiesImpl(() => IsLocal);

        m_PlayerReader = new NetReader();
        m_PlayerWriter = new NetWriter();
    }

    public void RestoreFromSnapshot(NetWorldPlayerSnapshot snapshot)
    {
        UID = snapshot.PlayerUID;
        m_Name = snapshot.Name;
        m_IsInGameScene = snapshot.IsInGameScene;
        m_Platform = snapshot.Platform;
        m_RTT = snapshot.RTT;
        m_TagObjectUID = snapshot.TagObjectUID;

        m_Properties.SetData(snapshot.Properties);
    }

    public NetWorldPlayerSnapshot ToSnapshot() => NetWorldPlayerSnapshot.FormPlayer(this, m_Properties.Data);

    public override string ToString() => $"{UID}:{Name} l:{IsLocal} m:{IsMaster}";
    public int CompareTo(NetWorldPlayer other) => UID.CompareTo(other.UID);
    public bool Equals(NetWorldPlayer other) => UID.Equals(other.UID);

    public byte[] Serialize()
    {
        m_PlayerWriter.SeekZero();
        ToSnapshot().Serialize(m_PlayerWriter);
        return m_PlayerWriter.ToArray();
    }

    public override int GetHashCode()
    {
        return UID.GetHashCode();
    }
}