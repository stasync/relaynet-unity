using System.Collections.Generic;

namespace LROTech.Network.StateManagement.Snapshots
{
    public readonly struct NetWorldSnapshot
    {
        public readonly float DeltaTime;
        public readonly NetWorldId Uid;
        public readonly NetWorldId Id;
        public readonly float Time;
        public readonly float LocalTime;
        public readonly IReadOnlyDictionary<NetObjectUid, NetObjectStateSnapshot> Objects;
        public readonly NetWorldPlayersCollectionsSnapshot Players;
        public readonly NetworkPropertiesSnapshot Properties;

        internal NetWorldSnapshot(float deltaTime, NetWorld world)
        {
            DeltaTime = deltaTime;
            Uid = world.Uid;
            Id = world.Id;
            Time = world.Time;
            LocalTime = world.LocalTime;

            var objectStates = new Dictionary<NetObjectUid, NetObjectStateSnapshot>(world.ObjectStates.Count);
            foreach (var kvp in world.ObjectStates)
                objectStates.Add(kvp.Key, kvp.Value.ToSnapshot());

            Objects = objectStates;

            Players = world.Players.ToSnapshot();
            Properties = world.Properties.ToSnapshot();
        }

        public int GetSize()
        {
            var size = 0;
            size += sizeof(float); // DeltaTime
            size += NetWorldId.SIZE; // Uid
            size += NetWorldId.SIZE; // ID
            size += sizeof(float); // Time
            size += sizeof(float); // Local time

            foreach (var netObject in Objects.Values)
            {
                size += NetObjectUid.SIZE; // NetObjectUid
                size += netObject.GetSize();
            }

            size += Players.GetSize();
            size += Properties.GetSize();
            return size;
        }
    }
}