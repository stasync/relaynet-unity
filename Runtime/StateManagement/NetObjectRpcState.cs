using LROTech.Network.StateManagement.Snapshots;

namespace LROTech.Network.StateManagement
{
    public readonly struct NetObjectRPCState
    {
        public readonly int Hash;
        public readonly uint UID;
        public readonly byte[] Payload;

        public bool IsValid => !Hash.Equals(0) && Payload != null;

        internal NetObjectRPCState(int hash, uint uid, byte[] payload)
        {
            Hash = hash;
            UID = uid;
            Payload = payload;
        }
        
        internal NetObjectRPCState(NetObjectRPCStateSnapshot snapshot)
        {
            Hash = snapshot.Hash;
            UID = snapshot.UID;
            Payload = snapshot.Payload;
        }
        
        public NetObjectRPCStateSnapshot ToSnapshot() =>
            new(data: this);

        internal int GetVersionHash()
        {
            var hash = 13;
            hash = hash * 7 + Hash.GetHashCode();
            hash = hash * 7 + UID.GetHashCode();
            foreach (var b in Payload)
                hash = hash * 7 + b.GetHashCode();
            return hash;
        }
    }
}