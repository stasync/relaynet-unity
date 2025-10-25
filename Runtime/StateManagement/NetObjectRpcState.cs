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
        
        public RPCStateSnapshot ToSnapshot() =>
            RPCStateSnapshot.FromState(this);

        public static NetObjectRPCState FromSnapshot(RPCStateSnapshot snapshot) =>
            new(snapshot.Hash, snapshot.UID, snapshot.Payload);

        public override int GetHashCode()
        {
            var hash = 13;
            hash = hash * 7 + Hash.GetHashCode();
            hash = hash * 7 + UID.GetHashCode();
            foreach (var d in Payload)
                hash = hash * 7 + d.GetHashCode();
            return hash;
        }
    }
}