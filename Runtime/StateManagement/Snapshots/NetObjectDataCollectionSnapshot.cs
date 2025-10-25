using Core.Networking;
using System.Collections.Generic;

namespace LROTech.Network.StateManagement
{
    public readonly struct NetObjectDataCollectionSnapshot
    {
        private static readonly NetWriter s_Writer = new();

        public readonly byte[] Data;

        public NetObjectDataCollectionSnapshot(IReadOnlyCollection<NetObjectStateData> dataCollection)
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

        private NetObjectDataCollectionSnapshot(NetReader reader) =>
            Data = reader.ReadBytesAndSize();

        public static NetObjectDataCollectionSnapshot FromState(NetObjectDataCollection data) =>
            data.ToSnapshot();

        public static NetObjectDataCollectionSnapshot FromReader(NetReader reader) =>
            new(reader);

        public int GetSize() =>
            Data.Length;

        public void Serialize(NetWriter writer) =>
            writer.WriteBytesAndSize(Data);
    }
}