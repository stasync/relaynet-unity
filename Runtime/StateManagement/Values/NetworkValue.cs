using Core.Networking;
using System;

public class SerializableValue<TValue> : ServializableValueDefinition<TValue, TValue>
{
    public SerializableValue(TValue defaultValue, Func<bool> writePermission, Func<NetReader, bool, TValue> reader, Action<NetWriter, TValue> writer) :
        base(defaultValue, writePermission, reader, writer)
    {
        
    }
}
