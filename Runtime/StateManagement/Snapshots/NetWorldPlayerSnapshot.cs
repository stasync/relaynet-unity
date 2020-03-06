using Core.Networking;

public struct NetWorldPlayerSnapshot
{
    public readonly ClientUID UID;
    public readonly string Name;
    public readonly bool IsInGameScene;
    public readonly GSDevice.Platform Platform;
    public readonly int RTT;

    public NetWorldPlayerSnapshot(NetWorldPlayer player)
    {
        UID = player.UID;
        Name = player.Name;
        IsInGameScene = player.IsInGameScene;
        Platform = player.Platform;
        RTT = player.RTT;
    }

    public NetWorldPlayerSnapshot(NetReader reader)
    {
        UID = reader.ReadConnectionUID();
        Name = reader.ReadString();
        IsInGameScene = reader.ReadBoolean();
        Platform = (GSDevice.Platform)reader.ReadInt32();
        RTT = reader.ReadInt32();
    }

    public static NetWorldPlayerSnapshot FormPlayer(NetWorldPlayer player) => new NetWorldPlayerSnapshot(player);
    public static NetWorldPlayerSnapshot FormReader(NetReader reader) => new NetWorldPlayerSnapshot(reader);

    public void Serialize(NetWriter writer)
    {
        writer.WriteConnectionUID(UID);
        writer.Write(Name);
        writer.Write(IsInGameScene);
        writer.Write((int)Platform);
        writer.Write(RTT);
    }
}
