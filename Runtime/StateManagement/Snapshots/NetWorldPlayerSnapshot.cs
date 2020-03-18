using Core.Networking;

public struct NetWorldPlayerSnapshot
{
    public readonly ClientUID PlayerUID;
    public readonly string Name;
    public readonly bool IsInGameScene;
    public readonly GSDevice.Platform Platform;
    public readonly int RTT;
    public readonly NetObjectUID TagObjectUID;

    public NetWorldPlayerSnapshot(NetWorldPlayer player)
    {
        PlayerUID = player.UID;
        Name = player.Name;
        IsInGameScene = player.IsInGameScene;
        Platform = player.Platform;
        RTT = player.RTT;
        TagObjectUID = player.TagObjectUID;
    }

    public NetWorldPlayerSnapshot(NetReader reader)
    {
        PlayerUID = reader.ReadConnectionUID();
        Name = reader.ReadString();
        IsInGameScene = reader.ReadBoolean();
        Platform = (GSDevice.Platform)reader.ReadInt32();
        RTT = reader.ReadInt32();
        TagObjectUID = reader.ReadInstanceId();
    }

    public static NetWorldPlayerSnapshot FormPlayer(NetWorldPlayer player) => new NetWorldPlayerSnapshot(player);
    public static NetWorldPlayerSnapshot FormReader(NetReader reader) => new NetWorldPlayerSnapshot(reader);

    public void Serialize(NetWriter writer)
    {
        writer.WriteConnectionUID(PlayerUID);
        writer.Write(Name);
        writer.Write(IsInGameScene);
        writer.Write((int)Platform);
        writer.Write(RTT);
        writer.WriteInstanceId(TagObjectUID);
    }
}
