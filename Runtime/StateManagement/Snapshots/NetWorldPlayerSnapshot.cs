using System.Collections.Generic;
using Core;
using Core.Networking;
using LROTech.Network.Sessions;

namespace LROTech.Network.StateManagement.Snapshots
{
    public readonly struct NetWorldPlayerSnapshot
    {
        public readonly ClientUid UID;
        public readonly string Name;
        public readonly bool IsInGameScene;
        public readonly UserDevice.Platform Platform;
        public readonly int RTTMs;
        public readonly IReadOnlyCollection<NetObjectUid> TagObjectUIDs;
        public readonly NetObjectUid MainTagObjectUID;
        public readonly NetworkPropertiesSnapshot Properties;

        internal NetWorldPlayerSnapshot(NetWorldPlayer player)
        {
            UID = player.Uid;
            Name = player.Name;
            IsInGameScene = player.IsReady;
            Platform = player.Platform;
            RTTMs = player.RTTMs;
            TagObjectUIDs = new HashSet<NetObjectUid>(player.TagObjectUidCollection);
            MainTagObjectUID = player.MainTagObjectUid;
            Properties = player.Properties.ToSnapshot();
        }

        internal NetWorldPlayerSnapshot(NetReader reader)
        {
            UID = reader.ReadClientUid();
            Name = reader.ReadString();
            IsInGameScene = reader.ReadBoolean();
            Platform = (UserDevice.Platform)reader.ReadInt32();
            RTTMs = reader.ReadInt32();
            TagObjectUIDs = reader.ReadHashSet(netReader => netReader.ReadInstanceId());
            MainTagObjectUID = reader.ReadInstanceId();
            Properties = new NetworkPropertiesSnapshot(reader.ReadBytesAndSize());
        }
        
        internal void Serialize(NetWriter writer)
        {
            writer.WriteClientUid(UID);
            writer.WriteString(Name);
            writer.WriteBool(IsInGameScene);
            writer.WriteInt32((int)Platform);
            writer.WriteSingle(RTTMs);
            writer.WriteCollection(TagObjectUIDs, (netObjectUid, netWriter) => netWriter.WriteInstanceId(netObjectUid));
            writer.WriteInstanceId(MainTagObjectUID);
            writer.WriteBytesAndSize(Properties.Data);
        }

        public int GetSize()
        {
            var result = 0;
            result += ClientUid.Size; // Uid
            result += Name.Length;
            result += sizeof(bool); // IsInGameScene
            result += sizeof(UserDevice.Platform); // Platform
            result += sizeof(int); // RTT

            result += NetObjectUid.Size * TagObjectUIDs.Count;

            result += NetObjectUid.Size; // Main tag object
            result += Properties.GetSize();

            return result;
        }
    }
}