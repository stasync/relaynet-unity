using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Core.Networking;
using LROTech.Network.StateManagement.Values;

namespace LROTech.Network.StateManagement
{
    public class SerializableList<TValue> : SerializableValueBaseNew<List<TValue>, IReadOnlyList<TValue>>, IList<TValue>, ICollectionWithCount
        where TValue : unmanaged
    {
        private readonly List<TValue> m_CachedValue = new();

        public SerializableList() : this(defaultValue: new List<TValue>())
        {
        }
        
        public SerializableList(List<TValue> defaultValue) : base(defaultValue)
        {
        }

        public void SetAt(int index, TValue value)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            if (index >= Count)
                InternalValue.AddRange(Enumerable.Repeat<TValue>(default, index + 1 - Count));

            SetAtUnsafe(index, value);
        }

        void SetAtUnsafe(int index, TValue value)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            var curretnValue = InternalValue[index];
            if (curretnValue.Equals(value))
                return;

            InternalValue[index] = value;
        }

        #region IList

        public TValue this[int index]
        {
            get => Value[index];
            set => SetAtUnsafe(index, value);
        }

        public int Count => Value.Count;

        public bool IsReadOnly => false;

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

        public bool Contains(TValue item) => Value.Contains(item);

        public void CopyTo(TValue[] array, int arrayIndex) => InternalValue.CopyTo(array, arrayIndex);

        public int IndexOf(TValue item) => InternalValue.IndexOf(item);

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

        public IEnumerator<TValue> GetEnumerator() => Value.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => Value.GetEnumerator();

        #endregion

        protected override List<TValue> OnDeserialize(NetReader reader, bool firstTime)
        {
            reader.ReadListTo<TValue>(m_CachedValue, Reader);
            return InternalValue;
        }

        private TValue Reader(NetReader arg) =>
            arg.ReadUnmanaged<TValue>();

        protected override void OnSerialize(NetWriter writer) =>
            writer.WriteCollection(InternalValue, ElementWrite);
        
        private void ElementWrite(TValue value, NetWriter writer) =>
            writer.WriteUnmanaged(value);

        public override string ToString() =>
            $"[{string.Join("; ", InternalValue)}]";
    }
}