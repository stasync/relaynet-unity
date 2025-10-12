using Core.Networking;
using System.Collections.Generic;

public readonly struct NetWorldPlayerSnapshot
{
    public readonly ClientUID UID;
    public readonly string Name;
    public readonly bool IsInGameScene;
    public readonly GSDevice.Platform Platform;
    public readonly int RTTMs;
    public readonly HashSet<NetObjectUID> TagObjectUIDs;
    public readonly NetObjectUID MainTagObjectUID;
    public readonly PropertiesSnapshot Properties;

    private NetWorldPlayerSnapshot(NetWorldPlayer player)
    {
        UID = player.UID;
        Name = player.Name;
        IsInGameScene = player.IsReady;
        Platform = player.Platform;
        RTTMs = player.RTTMs;
        TagObjectUIDs = new HashSet<NetObjectUID>(player.TagObjectUIDs);
        MainTagObjectUID = player.MainTagObjectUID;
        Properties = PropertiesSnapshot.FromProperties(player.Properties);
    }

    private NetWorldPlayerSnapshot(NetReader reader)
    {
        UID = reader.ReadConnectionUID();
        Name = reader.ReadString();
        IsInGameScene = reader.ReadBoolean();
        Platform = (GSDevice.Platform)reader.ReadInt32();
        RTTMs = reader.ReadInt32();
        TagObjectUIDs = reader.ReadHashSet(r => r.ReadInstanceId());
        MainTagObjectUID = reader.ReadInstanceId();
        Properties = PropertiesSnapshot.FromReader(reader);
    }

    public static NetWorldPlayerSnapshot FormPlayer(NetWorldPlayer player) => new(player);
    public static NetWorldPlayerSnapshot FormReader(NetReader reader) => new(reader);

    public void Serialize(NetWriter writer)
    {
        writer.WriteConnectionUID(UID);
        writer.WriteString(Name);
        writer.WriteBool(IsInGameScene);
        writer.WriteInt32((int)Platform);
        writer.WriteSingle(RTTMs);
        writer.WriteCollection(TagObjectUIDs, (v, w) => w.WriteInstanceId(v));
        writer.WriteInstanceId(MainTagObjectUID);
        writer.WriteBytesAndSize(Properties.Data);
    }

    public int GetSize()
    {
        var result = 0;
        result += ClientUID.Size; // UID
        result += Name.Length;
        result += sizeof(bool); // IsInGameScene
        result += sizeof(GSDevice.Platform); // Platform
        result += sizeof(int); // RTT

        result += NetObjectUID.Size * TagObjectUIDs.Count;

        result += NetObjectUID.Size; // Main tag object
        result += Properties.GetSize();

        return result;
    }
}
