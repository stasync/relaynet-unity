using Core.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using LROTech.Network.StateManagement.Values;

namespace LROTech.Network.StateManagement
{
    public class SerializableHashSet<TValue> : NetworkValueBase<HashSet<TValue>, IReadOnlyCollection<TValue>>, IReadOnlyCollection<TValue> 
        where TValue : unmanaged
    {
        private readonly HashSet<TValue> m_CachedValue = new();
        
        public SerializableHashSet() : this(defaultValue: new HashSet<TValue>())
        {
        }

        public SerializableHashSet(HashSet<TValue> defaultValue) : base(defaultValue)
        {
        }

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

        public bool Contains(TValue value) => 
            InternalValue.Contains(value);

        public void Clear()
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change hash of type {typeof(TValue).Name}");

            if (InternalValue.Count == 0)
                return;

            InternalValue.Clear();
        }

        protected override HashSet<TValue> OnDeserialize(NetReader reader)
        {
            reader.ReadHashSetTo(m_CachedValue, ReadFunction);
            return m_CachedValue;
            
            TValue ReadFunction(NetReader arg) =>
                arg.ReadUnmanaged<TValue>();
        }
        
        protected override void OnSerialize(NetWriter writer)
        {
            writer.WriteCollection(InternalValue, WriteFunction);
            return;

            void WriteFunction(TValue value, NetWriter argument) =>
                argument.WriteUnmanaged(value);
        }
        
        public override string ToString() =>
            $"{InternalValue.Count} [{string.Join("; ", InternalValue)}]";

        public IEnumerator<TValue> GetEnumerator() => InternalValue.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => InternalValue.GetEnumerator();
    }
}