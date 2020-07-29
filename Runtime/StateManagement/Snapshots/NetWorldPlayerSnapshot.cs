using Core.Networking;
using System.Collections.Generic;

public struct NetWorldPlayerSnapshot
{
    public readonly ClientUID PlayerUID;
    public readonly string Name;
    public readonly bool IsInGameScene;
    public readonly GSDevice.Platform Platform;
    public readonly int RTT;
    public readonly HashSet<NetObjectUID> TagObjectUIDs;
    public readonly NetObjectUID MainTagObjectUID;
    public readonly PropertiesSnapshot Properties;

    public NetWorldPlayerSnapshot(NetWorldPlayer player)
    {
        PlayerUID = player.UID;
        Name = player.Name;
        IsInGameScene = player.IsReady;
        Platform = player.Platform;
        RTT = player.RTT;
        TagObjectUIDs = new HashSet<NetObjectUID>(player.TagObjectUIDs);
        MainTagObjectUID = player.MainTagObjectUID;
        Properties = new PropertiesSnapshot(player.Properties);
    }

    public NetWorldPlayerSnapshot(NetReader reader)
    {
        PlayerUID = reader.ReadConnectionUID();
        Name = reader.ReadString();
        IsInGameScene = reader.ReadBoolean();
        Platform = (GSDevice.Platform)reader.ReadInt32();
        RTT = reader.ReadInt32();
        TagObjectUIDs = reader.ReadHashSet(r => r.ReadInstanceId());
        MainTagObjectUID = reader.ReadInstanceId();
        Properties = PropertiesSnapshot.FromReader(reader);
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
        writer.WriteCollection(TagObjectUIDs, (v, w) => w.WriteInstanceId(v));
        writer.WriteInstanceId(MainTagObjectUID);
        Properties.Serialize(writer);
    }

    public int GetSize()
    {
        var result = 0;
        result += sizeof(uint); // PlayerUID
        result += Name.Length;
        result += sizeof(bool); // IsInGameScene
        result += sizeof(GSDevice.Platform); // Platform
        result += sizeof(int); // RTT
        result += sizeof(uint); // TagObjectUID
        result += Properties.GetSize();

        return result;
    }
}
