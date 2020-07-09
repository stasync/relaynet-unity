using Core.Networking;
using System.Collections.Generic;
using System.Linq;

public struct NetWorldPlayerSnapshot
{
    public readonly ClientUID PlayerUID;
    public readonly string Name;
    public readonly bool IsInGameScene;
    public readonly GSDevice.Platform Platform;
    public readonly int RTT;
    public readonly HashSet<NetObjectUID> TagObjectUIDs;
    public readonly NetObjectUID MainTagObjectUID;
    public readonly Dictionary<uint, byte[]> Properties;

    public NetWorldPlayerSnapshot(NetWorldPlayer player, IReadOnlyDictionary<uint, byte[]> properties)
    {
        PlayerUID = player.UID;
        Name = player.Name;
        IsInGameScene = player.IsReady;
        Platform = player.Platform;
        RTT = player.RTT;
        TagObjectUIDs = new HashSet<NetObjectUID>(player.TagObjectUIDs);
        MainTagObjectUID = player.MainTagObjectUID;
        Properties = new Dictionary<uint, byte[]>();
        foreach (var d in properties)
            Properties.Add(d.Key, d.Value.ToArray());
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
        Properties = reader.ReadDictionary(kr => kr.ReadPackedUInt32(), vr => vr.ReadBytesAndSize());
    }

    public static NetWorldPlayerSnapshot FormPlayer(NetWorldPlayer player, IReadOnlyDictionary<uint, byte[]> properties) => new NetWorldPlayerSnapshot(player, properties);
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
        writer.WriteDictionary(Properties, (k, w) => w.WritePackedUInt32(k), (v, w) => w.WriteBytesAndSize(v));
    }

    public int GetSize()
    {
        var result = 0;
        result += sizeof(uint); // PlayerUID
        result += Name.Length;
        result += sizeof(bool); // IsInGameScene
        result += 1; // Platform
        result += sizeof(int); // RTT
        result += sizeof(uint); // TagObjectUID
        foreach(var kvp in Properties)
        {
            result += sizeof(uint); // Key
            result += kvp.Value.Length; // Value
        }

        return result;
    }
}
