using Core.Networking;

public sealed class NetObjectViewStateData : NetObjectStateData
{
    public bool ViewInitialized { get; private set; }
    public byte[] UnstructuredData { get; private set; }

    public NetObjectViewStateData(System.Func<bool> writePermission) : base(writePermission)
    {
        ViewInitialized = false;
        UnstructuredData = new byte[] { };
    }

    public override void Serialize(NetWriter writer)
    {
        base.Serialize(writer);

        writer.WriteBool(Initialized);
        writer.WriteBytesAndSize(UnstructuredData);
    }

    public override void Deserialize(NetReader reader, bool firstTime)
    {
        base.Deserialize(reader, firstTime);

        ViewInitialized = reader.ReadBoolean();
        UnstructuredData = reader.ReadBytesAndSize();
    }

    public void SetData(byte[] data, bool initialization = false)
    {
        if (!WritePermission())
            throw new System.InvalidOperationException();

        UnstructuredData = data;

        if (initialization)
            ViewInitialized = true;
    }

    public override int GetHashCode()
    {
        var hash = base.GetHashCode();

        hash += ViewInitialized.GetHashCode();
        foreach (var d in UnstructuredData)
            hash = hash * 7 + d.GetHashCode();
        return hash;
    }
    
#if UNITY_EDITOR
    public override System.Collections.Generic.IEnumerable<string> GetValuesInfo_Editor()
    {
        foreach (var pair in base.GetValuesInfo_Editor())
            yield return pair;

        yield return $"{nameof(ViewInitialized)}:{ViewInitialized}";
        yield return $"{nameof(UnstructuredData)}:{UnstructuredData.Length}";
    }
#endif
}
