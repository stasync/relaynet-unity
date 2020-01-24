using Core.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class SerializableList<TValue> : ServializableValueDefinition<List<TValue>, IReadOnlyList<TValue>>, IList<TValue>
{
    public SerializableList(List<TValue> defaultValue, Func<bool> writePermission, Func<NetReader, bool, TValue> reader, Action<NetWriter, TValue> writer) :
        base(defaultValue, writePermission, 
            (r, f) => r.ReadCollection(cValueReader => reader(cValueReader, f)).ToList(), 
            (w, v) => w.WriteCollection(v, (cValue, cValueWriter) => writer(cValueWriter, cValue)))
    {
    }

    #region IList
    public TValue this[int index]
    {
        get => Value[index];
        set
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            InternalValue[index] = value;
            SetDirty();
        }
    }

    public int Count => Value.Count;

    public bool IsReadOnly => false;

    public void Add(TValue item)
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

        InternalValue.Add(item);
        SetDirty();
    }

    public void Clear()
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

        InternalValue.Clear();
        SetDirty();
    }

    public bool Contains(TValue item) => Value.Contains(item);

    public void CopyTo(TValue[] array, int arrayIndex) => InternalValue.CopyTo(array, arrayIndex);

    public int IndexOf(TValue item) => InternalValue.IndexOf(item);

    public void Insert(int index, TValue item)
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

        InternalValue.Insert(index, item);
        SetDirty();
    }

    public bool Remove(TValue item)
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

        var removed = InternalValue.Remove(item);
        if (removed)
            SetDirty();

        return removed;
    }

    public void RemoveAt(int index)
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

        InternalValue.RemoveAt(index);
        SetDirty();
    }

    public IEnumerator<TValue> GetEnumerator() => Value.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Value.GetEnumerator();
    #endregion

    public override string ToJSONString() => RealtimeJSONUtils.ToJSONCollection(Value.Select(v => v.ToString()).ToArray());
}
