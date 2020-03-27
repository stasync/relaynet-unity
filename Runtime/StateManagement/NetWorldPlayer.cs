using Core.Networking;
using System;

public class NetWorldPlayer : IComparable<NetWorldPlayer>, IEquatable<NetWorldPlayer>
{
    readonly NetWriter m_PlayerWriter;
    readonly NetReader m_PlayerReader;
    public bool Dirty => m_PlayerDirty || m_Properties.IsDirty;
    bool m_PlayerDirty;

    public void SetDitry()
    {
        if (!IsLocal)
            throw new InvalidOperationException();

        m_Properties.SetDirty();
        m_PlayerDirty = true;
    }

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

            if (m_Name.Equals(value))
                return;

            m_Name = value;
            m_PlayerDirty = true;
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

            if (m_IsInGameScene == value)
                return;

            m_IsInGameScene = value;
            m_PlayerDirty = true;
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
            m_PlayerDirty = true;
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

            if (m_RTT == value)
                return;

            m_RTT = value;
            m_PlayerDirty = true;
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

            if (m_TagObjectUID == value)
                return;

            m_TagObjectUID = value;
            m_PlayerDirty = true;
        }
    }
    NetObjectUID m_TagObjectUID;

    public IProperties Properties => m_Properties;
    readonly SerializablePropertiesImpl m_Properties;
    #endregion

    #endregion

    public bool IsValid => UID.IsValid;
    public bool IsLocal => UID == m_LocalPlayer();
    public bool IsMaster => UID == m_MasterPlayer();

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

    public NetWorldPlayerSnapshot ToSnapshot() => NetWorldPlayerSnapshot.FormPlayer(this, ((SerializablePropertiesImpl)m_Properties).Data);

    public override string ToString() => $"{UID}:{Name} l:{IsLocal} m:{IsMaster}";
    public int CompareTo(NetWorldPlayer other) => UID.CompareTo(other.UID);
    public bool Equals(NetWorldPlayer other) => UID.Equals(other.UID);

    public byte[] Serialize()
    {
        m_PlayerDirty = false;

        m_PlayerWriter.SeekZero();
        ToSnapshot().Serialize(m_PlayerWriter);
        return m_PlayerWriter.ToArray();
    }

    public override int GetHashCode()
    {
        return UID.GetHashCode();
    }
}