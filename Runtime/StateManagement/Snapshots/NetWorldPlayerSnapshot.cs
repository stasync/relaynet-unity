using System.Collections.Generic;
using Keel.Networking;

namespace Keel.RelayNet.StateManagement.Snapshots
{
    public readonly struct NetWorldPlayerSnapshot
    {
        public readonly ClientUid Uid;
        public readonly string Name;
        public readonly bool IsInGameScene;
        public readonly DevicePlatform Platform;
        public readonly int RTTMs;
        public readonly IReadOnlyCollection<NetObjectUid> TagObjects;
        public readonly NetObjectUid MainTagObject;
        public readonly NetworkPropertiesSnapshot Properties;
        public readonly float WorldTime;

        internal NetWorldPlayerSnapshot(NetWorldPlayer player)
        {
            Uid = player.Uid;
            Name = player.Name;
            IsInGameScene = player.IsReady;
            Platform = player.Platform;
            RTTMs = player.RttMs;
            TagObjects = new List<NetObjectUid>(player.TagObjectUidCollection);
            MainTagObject = player.MainTagObjectUid;
            Properties = player.Properties.ToSnapshot();
            WorldTime = player.WorldTime;
        }

        internal NetWorldPlayerSnapshot(NetReader reader)
        {
            Uid = reader.ReadClientUid();
            Name = reader.ReadString();
            IsInGameScene = reader.ReadBoolean();
            Platform = (DevicePlatform)reader.ReadInt32();
            RTTMs = reader.ReadInt32();

            var targetObjectCount = reader.ReadPackedUInt32();
            var newTagObjects = new List<NetObjectUid>((int)targetObjectCount);
            for (var i = 0; i < targetObjectCount; i++)
                newTagObjects.Add(reader.ReadNetObjectUid());

            TagObjects = newTagObjects;
            MainTagObject = reader.ReadNetObjectUid();
            Properties = new NetworkPropertiesSnapshot(reader.ReadBytesAndSize());
            WorldTime = reader.ReadSingle();
        }

        internal void Serialize(NetWriter writer)
        {
            writer.WriteClientUid(Uid);
            writer.WriteString(Name);
            writer.WriteBoolean(IsInGameScene);
            writer.WriteInt32((int)Platform);
            writer.WriteInt32(RTTMs);

            writer.WritePackedUInt32((uint)TagObjects.Count);
            foreach (var tagObjectUid in TagObjects)
                writer.WriteNetObjectUid(tagObjectUid);

            writer.WriteNetObjectUid(MainTagObject);
            writer.WriteBytesAndSize(Properties.Data);
            writer.WriteSingle(WorldTime);
        }

        public int GetSize()
        {
            var result = 0;
            result += ClientUid.SIZE; // Uid
            result += Name.Length;
            result += sizeof(bool); // IsInGameScene
            result += sizeof(DevicePlatform); // Platform
            result += sizeof(int); // RTT

            result += NetObjectUid.SIZE * TagObjects.Count;

            result += NetObjectUid.SIZE; // Main tag object
            result += Properties.GetSize();
            result += sizeof(float); // World Time.

            return result;
        }
    }
}