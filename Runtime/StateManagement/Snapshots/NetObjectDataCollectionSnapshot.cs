using System.Collections.Generic;
using Core.Networking;

namespace LROTech.Network.StateManagement.Snapshots
{
    public readonly struct NetObjectDataCollectionSnapshot
    {
        private static readonly NetWriter s_Writer = new();

        public readonly byte[] Data;

        internal NetObjectDataCollectionSnapshot(IReadOnlyCollection<NetObjectStateData> dataCollection)
        {
            s_Writer.SeekZero();

            s_Writer.WriteInt32(dataCollection.Count);
            foreach (var data in dataCollection)
            {
                s_Writer.WriteInt32(data.TypeHashCode);
                data.SerializeInternal(s_Writer);
            }

            Data = s_Writer.ToArray();
        }

        internal NetObjectDataCollectionSnapshot(NetReader reader) =>
            Data = reader.ReadBytesAndSize();
        
        internal void Serialize(NetWriter writer) =>
            writer.WriteBytesAndSize(Data);
        
        public int GetSize() =>
            Data.Length;
    }
}