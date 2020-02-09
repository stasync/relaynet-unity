using Core.Networking;
using System;

public class SerializableValue<TValue> : SerializableValueDefinition<TValue, TValue>
{
    public SerializableValue(
        TValue defaultValue, 
        Func<bool> writePermission, 
        Func<NetReader, bool, TValue> reader, 
        Action<NetWriter, TValue> writer) :
            base(defaultValue, writePermission, reader, writer)
    {
    }

    public override string ToJSONProperty() => 
        RealtimeJSONUtils.ToJSONValue(InternalValue.ToString());
}
