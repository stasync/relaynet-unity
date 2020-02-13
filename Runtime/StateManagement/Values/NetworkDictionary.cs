using Core.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class SerializableDictionary<TKey, TValue> : SerializableValueDefinition<Dictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>>, IDictionary<TKey, TValue>
{
    protected override bool WriteWithDirtyCheck => true;

    public SerializableDictionary(
        Dictionary<TKey, TValue> defaultValue,
        Func<bool> writePermission,
        Func<NetReader, bool, TKey> keyReader,
        Action<NetWriter, TKey> keyWriter,
        Func<NetReader, bool, TValue> valueReader,
        Action<NetWriter, TValue> valueWriter) :
            base(
                defaultValue,
                writePermission,
                (r, f) => r.ReadDictionary(
                    cKeyReader => keyReader(cKeyReader, f), 
                    cValueReader => valueReader(cValueReader, f)),
                (w, v) => w.WriteDictionary(v, 
                    (cKey, cKeyWriter) => keyWriter(cKeyWriter, cKey), 
                    (cValue, cValueWriter) => valueWriter(cValueWriter, cValue)))
    {
    }

    #region IDictionary
    public TValue this[TKey key]
    {
        get => Value[key];
        set
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change dictionary of type <{typeof(TValue).Name}, {typeof(TValue).Name}>");

            InternalValue[key] = value;
            SetDirty();
        }
    }

    public ICollection<TKey> Keys => InternalValue.Keys;

    public ICollection<TValue> Values => InternalValue.Values;

    public int Count => Value.Count;

    public bool IsReadOnly => false;

    public void Add(TKey key, TValue value)
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to change dictionary of type <{typeof(TValue).Name}, {typeof(TValue).Name}>");

        InternalValue.Add(key, value);
        SetDirty();
    }

    public void Add(KeyValuePair<TKey, TValue> item)
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to change dictionary of type <{typeof(TValue).Name}, {typeof(TValue).Name}>");

        InternalValue.Add(item.Key, item.Value);
        SetDirty();
    }

    public void Clear()
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to change dictionary of type <{typeof(TValue).Name}, {typeof(TValue).Name}>");

        InternalValue.Clear();
        SetDirty();
    }

    public bool Contains(KeyValuePair<TKey, TValue> item) => InternalValue.Contains(item);

    public bool ContainsKey(TKey key) => InternalValue.ContainsKey(key);

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        if (array == null)
            throw new ArgumentNullException(nameof(array));

        if (arrayIndex < 0 || arrayIndex > array.Length)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));

        if (array.Length - arrayIndex < Count)
            throw new InvalidOperationException();

        for (var i = 0; i < Count; i++)
        {
            var kvp = this.ElementAt(i);
            if (kvp.GetHashCode() >= 0)
                array[arrayIndex++] = new KeyValuePair<TKey, TValue>(kvp.Key, kvp.Value);
        }
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => InternalValue.GetEnumerator();

    public bool Remove(TKey key)
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to change dictionary of type <{typeof(TValue).Name}, {typeof(TValue).Name}>");

        var removed = InternalValue.Remove(key);
        SetDirty();
        return removed;
    }

    public bool Remove(KeyValuePair<TKey, TValue> item) => Remove(item.Key);

    public bool TryGetValue(TKey key, out TValue value) => InternalValue.TryGetValue(key, out value);

    IEnumerator IEnumerable.GetEnumerator() => Value.GetEnumerator();
    #endregion

    public override string ToJSONProperty() =>
        RealtimeJSONUtils.ToJSONCollection(Value.Select(kvp => "{" + RealtimeJSONUtils.ToJSONValue(kvp.Key.ToString()) + ":" + RealtimeJSONUtils.ToJSONValue(kvp.Value.ToString()) + "}").ToArray());
}
