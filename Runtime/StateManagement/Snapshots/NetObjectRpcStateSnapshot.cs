using Core.Networking;

namespace LROTech.Network.StateManagement
{
    public readonly struct NetObjectRPCStateSnapshot
    {
        public readonly int Hash;
        public readonly uint UID;
        public readonly byte[] Payload;

        internal NetObjectRPCStateSnapshot(NetObjectRPCState data)
        {
            Hash = data.Hash;
            UID = data.UID;
            Payload = data.Payload;
        }

        internal NetObjectRPCStateSnapshot(NetReader reader)
        {
            Hash = reader.ReadInt32();
            UID = reader.ReadPackedUInt32();
            Payload = reader.ReadBytesAndSize();
        }

        internal void Serialize(NetWriter writer)
        {
            writer.WriteInt32(Hash);
            writer.WritePackedUInt32(UID);
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