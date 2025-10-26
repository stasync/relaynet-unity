using LROTech.Network.StateManagement.Snapshots;

namespace LROTech.Network.StateManagement
{
    public readonly struct NetObjectRpcState
    {
        public readonly int Hash;
        public readonly uint Uid;
        public readonly byte[] Payload;

        public bool IsValid => !Hash.Equals(0) && Payload != null;

        internal NetObjectRpcState(int hash, uint uid, byte[] payload)
        {
            Hash = hash;
            Uid = uid;
            Payload = payload;
        }
        
        internal NetObjectRpcState(NetObjectRpcStateSnapshot snapshot)
        {
            Hash = snapshot.Hash;
            Uid = snapshot.Uid;
            Payload = snapshot.Payload;
        }
        
        public NetObjectRpcStateSnapshot ToSnapshot() =>
            new(data: this);

        internal int GetVersionHash()
        {
            var hash = 13;
            hash = hash * 7 + Hash.GetHashCode();
            hash = hash * 7 + Uid.GetHashCode();
            foreach (var b in Payload)
                hash = hash * 7 + b.GetHashCode();
            return hash;
        }
    }
}