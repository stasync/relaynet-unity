using System.Collections.Generic;

namespace LROTech.Network.StateManagement.Snapshots
{
    public readonly struct NetWorldSnapshot
    {
        public readonly float DeltaTime;
        public readonly NetWorldID Uid;
        public readonly NetWorldID Id;
        public readonly float Time;
        public readonly float LocalTime;
        public readonly IReadOnlyDictionary<NetObjectUid, NetObjectStateSnapshot> Objects;
        public readonly NetWorldPlayersCollectionsSnapshot Players;
        public readonly NetworkPropertiesSnapshot Properties;

        internal NetWorldSnapshot(float deltaTime, NetWorld world)
        {
            DeltaTime = deltaTime;
            Uid = world.UID;
            Id = world.ID;
            Time = world.Time;
            LocalTime = world.LocalTime;

            var objectStates = new Dictionary<NetObjectUid, NetObjectStateSnapshot>();
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
            size += NetWorldID.Size; // Uid
            size += NetWorldID.Size; // ID
            size += sizeof(float); // Time
            size += sizeof(float); // Local time

            foreach (var netObject in Objects.Values)
            {
                size += NetObjectUid.Size; // NetObjectUid
                size += netObject.GetSize();
            }

            size += Players.GetSize();
            size += Properties.GetSize();
            return size;
        }
    }
}