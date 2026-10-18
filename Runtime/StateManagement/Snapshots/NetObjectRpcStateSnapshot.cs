using Keel.Networking;

namespace LROTech.Network.StateManagement.Snapshots
{
    public readonly struct NetObjectRpcStateSnapshot
    {
        public readonly int Hash;
        public readonly uint Uid;
        public readonly byte[] Payload;

        internal NetObjectRpcStateSnapshot(NetObjectRpcState data)
        {
            Hash = data.Hash;
            Uid = data.Uid;
            Payload = data.Payload;
        }

        internal NetObjectRpcStateSnapshot(NetReader reader)
        {
            Hash = reader.ReadInt32();
            Uid = reader.ReadPackedUInt32();
            Payload = reader.ReadBytesAndSize();
        }

        internal void Serialize(NetWriter writer)
        {
            writer.WriteInt32(Hash);
            writer.WritePackedUInt32(Uid);
            writer.WriteBytesAndSize(Payload);
        }

        public int GetSize()
        {
            var result = 0;
            result += sizeof(int); // Hash
            result += sizeof(uint); // Uid
            result += Payload.Length;
            return result;
        }
    }
}