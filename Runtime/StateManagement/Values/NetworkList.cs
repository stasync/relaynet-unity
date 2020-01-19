using Core.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class SerializableList<TSerializedValue> : ServializableValueDefenition<IList<TSerializedValue>, TSerializedValue>, IList<TSerializedValue>
{
    public SerializableList(IList<TSerializedValue> defaultValue, Func<bool> writePermission, Func<NetReader, bool, TSerializedValue> reader, Action<NetWriter, TSerializedValue> writer) :
        base(defaultValue, writePermission, reader, writer)
    {
    }

    #region IList
    public TSerializedValue this[int index]
    {
        get => Value[index];
        set
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TSerializedValue).Name}");

            Value[index] = value;
            SetDirty();
        }
    }

    public int Count => Value.Count;

    public bool IsReadOnly => Value.IsReadOnly;

    public void Add(TSerializedValue item)
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to change list of type {typeof(TSerializedValue).Name}");

        Value.Add(item);
        SetDirty();
    }

    public void Clear()
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to change list of type {typeof(TSerializedValue).Name}");

        Value.Clear();
        SetDirty();
    }

    public bool Contains(TSerializedValue item) => Value.Contains(item);

    public void CopyTo(TSerializedValue[] array, int arrayIndex) => Value.CopyTo(array, arrayIndex);

    public int IndexOf(TSerializedValue item) => Value.IndexOf(item);

    public void Insert(int index, TSerializedValue item)
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to change list of type {typeof(TSerializedValue).Name}");

        Value.Insert(index, item);
        SetDirty();
    }

    public bool Remove(TSerializedValue item)
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to change list of type {typeof(TSerializedValue).Name}");

        var removed = Value.Remove(item);
        if (removed)
            SetDirty();

        return removed;
    }

    public void RemoveAt(int index)
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to change list of type {typeof(TSerializedValue).Name}");

        Value.RemoveAt(index);
        SetDirty();
    }

    public IEnumerator<TSerializedValue> GetEnumerator() => Value.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Value.GetEnumerator();
    #endregion

    protected override IList<TSerializedValue> OnDeserialize(NetReader reader, bool firstTime) => reader
        .ReadCollection(r => m_Reader(r, firstTime))
        .ToList();

    protected override void OnSerializeWithPermission(NetWriter writer) => writer
        .WriteCollection(Value, (v, w) => m_Writer(w, v));
}
