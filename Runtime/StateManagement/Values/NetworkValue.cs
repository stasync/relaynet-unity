using Core.Networking;
using System;

public class SerializableValue<TSerializedValue> : ServializableValueDefenition<TSerializedValue, TSerializedValue>
{
    public SerializableValue(TSerializedValue defaultValue, Func<bool> writePermission, Func<NetReader, bool, TSerializedValue> reader, Action<NetWriter, TSerializedValue> writer) :
        base(defaultValue, writePermission, reader, writer)
    {
    }

    protected override TSerializedValue OnDeserialize(NetReader reader, bool firstTime) => m_Reader(reader, firstTime);
    protected override void OnSerializeWithPermission(NetWriter writer) => m_Writer(writer, Value);
}
