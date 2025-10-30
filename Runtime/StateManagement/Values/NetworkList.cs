using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Core.Networking;
using LROTech.Network.StateManagement.Values;
using LROTech.Network.StateManagement.Values.Serialization;

namespace LROTech.Network.StateManagement
{
    public class SerializableList<TValue> : NetworkValueBase<List<TValue>, IReadOnlyList<TValue>>, IList<TValue>
        where TValue : unmanaged
    {
        private readonly List<TValue> _cachedValue = new();
        
        public SerializableList(List<TValue> defaultValue = null) : base(defaultValue ?? new List<TValue>())
        {
        }

        protected override List<TValue> OnDeserialize(NetReader reader)
        {
            _cachedValue.Clear();
            var length = reader.ReadPackedUInt32();
            for (var i = 0; i < length; i++)
                _cachedValue.Add(SerializationAPI<TValue>.Serializer.Deserialize(reader));
            
            return _cachedValue;
        }

        protected override void OnSerialize(NetWriter writer)
        {
            writer.WritePackedUInt32((uint)InternalValue.Count);
            foreach (var value in InternalValue)
                SerializationAPI<TValue>.Serializer.Serialize(writer, value);
        }

        #region IList

        public TValue this[int index]
        {
            get => Value[index];
            set
            {
                if (!HasPermission)
                    throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

                var currentValue = InternalValue[index];
                if (currentValue.Equals(value))
                    return;

                InternalValue[index] = value;
            }
        }

        public int Count => Value.Count;

        public bool IsReadOnly => HasPermission;

        public void Add(TValue item)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            InternalValue.Add(item);
        }

        public void Clear()
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            if (InternalValue.Count == 0)
                return;

            InternalValue.Clear();
        }

        public bool Contains(TValue item) =>
            Value.Contains(item);

        public void CopyTo(TValue[] array, int arrayIndex) =>
            InternalValue.CopyTo(array, arrayIndex);

        public int IndexOf(TValue item) =>
            InternalValue.IndexOf(item);

        public void Insert(int index, TValue item)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            InternalValue.Insert(index, item);
        }

        public bool Remove(TValue item)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            return InternalValue.Remove(item);
        }

        public void RemoveAt(int index)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            InternalValue.RemoveAt(index);
        }

        public IEnumerator<TValue> GetEnumerator() =>
            Value.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() =>
            Value.GetEnumerator();

        #endregion

        public override string ToString() =>
            $"[{string.Join("; ", InternalValue)}]";
    }
}