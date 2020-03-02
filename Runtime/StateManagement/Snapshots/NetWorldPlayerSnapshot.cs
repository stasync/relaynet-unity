public struct NetWorldPlayerSnapshot
{
    public readonly ClientUID UID;
    public readonly string Name;

    public NetWorldPlayerSnapshot(NetWorldPlayer player)
    {
        UID = player.UID;
        Name = player.Name;
    }

    public static NetWorldPlayerSnapshot FormPlayer(NetWorldPlayer player) => new NetWorldPlayerSnapshot(player);
}
