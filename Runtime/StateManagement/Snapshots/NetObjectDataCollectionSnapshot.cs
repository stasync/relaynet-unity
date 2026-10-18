using System.Collections.Generic;
using Keel.Networking;

namespace Keel.RelayNet.StateManagement.Snapshots
{
    public readonly struct NetObjectDataCollectionSnapshot
    {
        private static readonly NetWriter s_writer = new();

        public readonly byte[] Data;

        internal NetObjectDataCollectionSnapshot(IReadOnlyCollection<NetObjectStateData> dataCollection)
        {
            s_writer.SeekZero();

            s_writer.WriteInt32(dataCollection.Count);
            foreach (var data in dataCollection)
            {
                s_writer.WriteInt32(data.TypeHashCode);
                data.SerializeInternal(s_writer);
            }

            Data = s_writer.ToArray();
        }

        internal NetObjectDataCollectionSnapshot(NetReader reader) =>
            Data = reader.ReadBytesAndSize();
        
        internal void Serialize(NetWriter writer) =>
            writer.WriteBytesAndSize(Data);
        
        public int GetSize() =>
            Data.Length;
    }
}