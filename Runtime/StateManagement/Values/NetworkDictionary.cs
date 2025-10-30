using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Core.Networking;
using LROTech.Network.StateManagement.Values.Serialization;

namespace LROTech.Network.StateManagement.Values
{
    public class NetworkDictionary<TKey, TValue> : NetworkValueBase<Dictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>>,
        IDictionary<TKey, TValue>
        where TKey : unmanaged
        where TValue : unmanaged
    {
        private readonly Dictionary<TKey, TValue> _cachedValue = new();

        public NetworkDictionary(Dictionary<TKey, TValue> defaultValue = null) : base(defaultValue ?? new Dictionary<TKey, TValue>())
        {
        }

        protected override Dictionary<TKey, TValue> OnDeserialize(NetReader reader)
        {
            _cachedValue.Clear();
            var length = reader.ReadPackedUInt32();
            for (var i = 0; i < length; i++)
            {
                var key = NetworkValueSerializationAPI<TKey>.Serializer.Deserialize(reader);
                var value = NetworkValueSerializationAPI<TValue>.Serializer.Deserialize(reader);
                _cachedValue.Add(key, value);
            }

            return _cachedValue;
        }

        protected override void OnSerialize(NetWriter writer)
        {
            writer.WritePackedUInt32((uint)InternalValue.Count);
            foreach (var kvp in InternalValue)
            {
                NetworkValueSerializationAPI<TKey>.Serializer.Serialize(writer, kvp.Key);
                NetworkValueSerializationAPI<TValue>.Serializer.Serialize(writer, kvp.Value);
            }
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
                if (kvp.Key.GetHashCode() >= 0)
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
}