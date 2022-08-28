using Core.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class SerializableHashSet<TValue> : SerializableValueDefinition<HashSet<TValue>, IReadOnlyCollection<TValue>>, IReadOnlyCollection<TValue>, ICollectionWithCount
{
    public SerializableHashSet(
        HashSet<TValue> defaultValue,
        Func<bool> writePermission,
        Func<NetReader, bool, TValue> valueReader,
        Action<NetWriter, TValue> valueWriter) :
            base(
                defaultValue,
                writePermission,
                (r, f) => r.ReadHashSet(
                    cValueReader => valueReader(cValueReader, f)),
                (w, v) => w.WriteCollection(v,
                    (cValue, cValueWriter) => valueWriter(cValueWriter, cValue)))
    { }

    public int Count => InternalValue.Count;

    public bool Add(TValue value)
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to change hash of type {typeof(TValue).Name}");

        return InternalValue.Add(value);
    }

    public bool Remove(TValue value)
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to change hash of type {typeof(TValue).Name}");

        return InternalValue.Remove(value);
    }

    public bool Contains(TValue value) => InternalValue.Contains(value);

    public void Clear()
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to change hash of type {typeof(TValue).Name}");

        if (InternalValue.Count == 0)
            return;

        InternalValue.Clear();
    }

    public override string ToString() =>
        $"[{string.Join("; ", InternalValue)}]";

    public IEnumerator<TValue> GetEnumerator() => InternalValue.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => InternalValue.GetEnumerator();

    public override string ToJSONProperty() =>
        RealtimeJSONUtils.ToJSONCollection(Value.Select(v => RealtimeJSONUtils.ToJSONValue(v.ToString())).ToArray());
}