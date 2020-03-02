using System;

public class NetWorldPlayer
{
    public readonly ClientUID UID;
    public readonly string Name;
    public bool IsLocal => UID == m_LocalPlayer();
    public bool IsMaster => UID == m_MasterPlayer();

    readonly Func<ClientUID> m_LocalPlayer;
    readonly Func<ClientUID> m_MasterPlayer;

    public NetWorldPlayer(ClientUID uid, string name, Func<ClientUID> localPlayer, Func<ClientUID> masterPlayer)
    {
        UID = uid;
        Name = name;
        m_LocalPlayer = localPlayer;
        m_MasterPlayer = masterPlayer;
    }

    public override string ToString() => $"{UID}:{Name} l:{IsLocal} m:{IsMaster}";
}