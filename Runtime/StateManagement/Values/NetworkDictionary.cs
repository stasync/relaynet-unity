using Core.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class SerializableDictionary<TKey, TValue> : ServializableValueDefinition<Dictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>>
{
    public SerializableDictionary(Dictionary<TKey, TValue> defaultValue, Func<bool> writePermission, Func<NetReader, bool, TValue> reader, Action<NetWriter, TValue> writer) :
        base(defaultValue, writePermission, 
            (r, f) => r.ReadDictionary(cKeyReader => reader(cKeyReader, f), cValueReader => reader(cValueReader, f)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value), 
            (w, v) => w.WriteCollection(v, (cValue, cValueWriter) => writer(cValueWriter, cValue)))
    {
    }

    public override string ToJSONString() => RealtimeJSONUtils.ToJSONCollection(Value.Select(v => v.ToString()).ToArray());
}
