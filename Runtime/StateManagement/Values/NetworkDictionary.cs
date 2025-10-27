using Core.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LROTech.Network.StateManagement.Values;

namespace LROTech.Network.StateManagement
{
    public class SerializableDictionary<TKey, TValue> : SerializableValueBase<Dictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>>,
        IDictionary<TKey, TValue>, ICollectionWithCount
    {
        public SerializableDictionary(
            Func<NetReader, bool, TKey> keyReader,
            Action<NetWriter, TKey> keyWriter,
            Func<NetReader, bool, TValue> valueReader,
            Action<NetWriter, TValue> valueWriter) :
            this(new Dictionary<TKey, TValue>(), keyReader, keyWriter, valueReader, valueWriter)
        {
        }

        public SerializableDictionary(
            Dictionary<TKey, TValue> defaultValue,
            Func<NetReader, bool, TKey> keyReader,
            Action<NetWriter, TKey> keyWriter,
            Func<NetReader, bool, TValue> valueReader,
            Action<NetWriter, TValue> valueWriter) :
            base(
                defaultValue,
                (r, f) => r.ReadDictionary(
                    cKeyReader => keyReader(cKeyReader, f),
                    cValueReader => valueReader(cValueReader, f)),
                (w, v) => w.WriteDictionary(v,
                    (cKey, cKeyWriter) => keyWriter(cKeyWriter, cKey),
                    (cValue, cValueWriter) => valueWriter(cValueWriter, cValue)))
        {
        }


        public override string ToString()
        {
            var result = string.Empty;
            foreach (var kvp in InternalValue)
                result += $"[{kvp.Key}:{kvp.Value}] ";

            return result.TrimEnd();

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
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change dictionary of type <{typeof(TValue).Name}, {typeof(TValue).Name}>");

            InternalValue.Add(item.Key, item.Value);
        }

        public void Clear()
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change dictionary of type <{typeof(TValue).Name}, {typeof(TValue).Name}>");

            if (InternalValue.Count == 0)
                return;

            InternalValue.Clear();
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

            return InternalValue.Remove(key);
        }

        public bool Remove(KeyValuePair<TKey, TValue> item) => Remove(item.Key);

        public bool TryGetValue(TKey key, out TValue value) => InternalValue.TryGetValue(key, out value);

        IEnumerator IEnumerable.GetEnumerator() => Value.GetEnumerator();

        #endregion
    }

    public class SerializableCollectionDictionary<TKey, TValue> : SerializableDictionary<TKey, List<TValue>>
    {
        public SerializableCollectionDictionary(
            Dictionary<TKey, List<TValue>> defaultValue,
            Func<NetReader, bool, TKey> keyReader,
            Action<NetWriter, TKey> keyWriter,
            Func<NetReader, bool, TValue> valueReader,
            Action<NetWriter, TValue> valueWriter) :
            base(
                defaultValue,
                keyReader,
                keyWriter,
                (vr, f) =>
                    vr.ReadList(r => valueReader(r, f)),
                (vw, collection) =>
                    vw.WriteCollection(collection, (v, w) => valueWriter(w, v)))
        {
        }
    }
}