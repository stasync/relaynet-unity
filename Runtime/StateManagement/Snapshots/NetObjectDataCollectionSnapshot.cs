using Core.Networking;
using System.Collections.Generic;

public struct NetObjectDataCollectionSnapshot
{
    static NetWriter s_Writer;

    public readonly byte[] Data;

    public NetObjectDataCollectionSnapshot(IReadOnlyCollection<NetObjectStateData> dataCollection)
    {
        if (s_Writer == null)
            s_Writer = new NetWriter();
        s_Writer.SeekZero();

        s_Writer.Write(dataCollection.Count);
        foreach (var data in dataCollection)
        {
            s_Writer.Write(data.GetType().FullName);
            data.Serialize(s_Writer);
        }

        Data = s_Writer.ToArray();
    }

    public NetObjectDataCollectionSnapshot(NetReader reader)
    {
        Data = reader.ReadBytesAndSize();
    }

    public static NetObjectDataCollectionSnapshot FromState(NetObjectDataCollection data) => data.ToSnapshot();

    public static NetObjectDataCollectionSnapshot FromReader(NetReader reader) => new NetObjectDataCollectionSnapshot(reader);

    public int GetSize() => Data.Length;

    public void Serialize(NetWriter writer) => writer.WriteBytesAndSize(Data);
}