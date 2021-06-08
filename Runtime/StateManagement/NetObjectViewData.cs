using Core.Networking;

public interface IReadOnlyNetObjectViewData
{
    bool Initialized { get; }
    byte[] UnstructuredData { get; }

    NetObjectViewDataSnapshot ToSnapshot();
}

public class NetObjectViewData : IReadOnlyNetObjectViewData
{
    public bool Initialized { get; private set; }
    public byte[] UnstructuredData { get; private set; }

    public NetObjectViewData()
    {
        Clear();
    }

    public override int GetHashCode()
    {
        var hash = 13;

        hash += Initialized.GetHashCode();
        foreach (var d in UnstructuredData)
            hash = (hash * 7) + d.GetHashCode();

        return hash;
    }

    public void SetData(byte[] data, bool initialization = false)
    {
        UnstructuredData = data;

        if (initialization)
            Initialized = true;
    }

    public void Clear()
    {
        Initialized = false;
        UnstructuredData = new byte[] { };
    }

    public void RestoreFormSnapshot(NetObjectViewDataSnapshot snapshot)
    {
        Initialized = snapshot.Initalized;
        UnstructuredData = snapshot.UnstructuredData;
    }

    public NetObjectViewDataSnapshot ToSnapshot() => NetObjectViewDataSnapshot.FromState(this);
}

public struct NetObjectViewDataSnapshot
{
    public readonly bool Initalized;
    public readonly byte[] UnstructuredData;

    public NetObjectViewDataSnapshot(IReadOnlyNetObjectViewData state)
    {
        Initalized = state.Initialized;
        UnstructuredData = state.UnstructuredData;
    }

    public NetObjectViewDataSnapshot(NetReader reader)
    {
        Initalized = reader.ReadBoolean();
        UnstructuredData = reader.ReadBytesAndSize();
    }

    public void Serialize(NetWriter writer)
    {
        writer.Write(Initalized);
        writer.WriteBytesAndSize(UnstructuredData);
    }

    public static NetObjectViewDataSnapshot FromState(NetObjectViewData data) => new NetObjectViewDataSnapshot(data);

    public static NetObjectViewDataSnapshot FromReader(NetReader reader) => new NetObjectViewDataSnapshot(reader);

    public int GetSize()
    {
        var result = 0;
        result += sizeof(bool);
        result += UnstructuredData.Length;
        return result;
    }
}
