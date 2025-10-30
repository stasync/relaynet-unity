using System;
using System.Collections;
using System.Collections.Generic;
using Core.Networking;
using LROTech.Network.StateManagement.Values.Serialization;

namespace LROTech.Network.StateManagement.Values
{
    public class NetworkDictionary<TKey, TValue> : NetworkValueManaged<Dictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>>,
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

        public bool IsReadOnly => HasPermission;

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

        public bool ContainsKey(TKey key) =>
            InternalValue.ContainsKey(key);
        
        public bool Remove(TKey key)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change dictionary of type <{typeof(TValue).Name}, {typeof(TValue).Name}>");

            return InternalValue.Remove(key);
        }

        public bool TryGetValue(TKey key, out TValue value) =>
            InternalValue.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() =>
            InternalValue.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() =>
            Value.GetEnumerator();

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item) =>
            throw new NotImplementedException();

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) =>
            throw new NotImplementedException();

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) =>
            throw new NotImplementedException();

        #endregion
        
        public override string ToString()
        {
            var result = string.Empty;
            foreach (var kvp in InternalValue)
                result += $"[{kvp.Key}:{kvp.Value}] ";

            return result.TrimEnd();
        }
    }
}