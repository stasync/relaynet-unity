using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Core.Networking;
using LROTech.Network.StateManagement.Values;

namespace LROTech.Network.StateManagement
{
    public class SerializableList<TValue> : SerializableValueBase<List<TValue>, IReadOnlyList<TValue>>, IList<TValue>
        where TValue : unmanaged
    {
        private readonly List<TValue> m_CachedValue = new();

        public SerializableList() : this(defaultValue: new List<TValue>())
        {
        }

        public SerializableList(List<TValue> defaultValue) : base(defaultValue)
        {
        }

        protected override List<TValue> OnDeserialize(NetReader reader, bool firstTime)
        {
            reader.ReadListTo(m_CachedValue, ReadFunction);
            return InternalValue;

            static TValue ReadFunction(NetReader arg) =>
                arg.ReadUnmanaged<TValue>();
        }

        protected override void OnSerialize(NetWriter writer)
        {
            writer.WriteCollection(InternalValue, WriteFunction);
            return;

            static void WriteFunction(TValue value, NetWriter writer) =>
                writer.WriteUnmanaged(value);
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