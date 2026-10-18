using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Keel.Networking;
using Keel.RelayNet.StateManagement.Values.Serialization;

namespace Keel.RelayNet.StateManagement.Values
{
    public class NetworkList<TValue> : NetworkValueManaged<List<TValue>, IReadOnlyList<TValue>>, IList<TValue>
        where TValue : unmanaged
    {
        private readonly List<TValue> _cachedValue = new();

        public NetworkList(List<TValue> defaultValue = null) : base(defaultValue ?? new List<TValue>())
        {
        }

        protected override List<TValue> OnDeserialize(NetReader reader)
        {
            _cachedValue.Clear();
            var length = reader.ReadPackedUInt32();
            for (var i = 0; i < length; i++)
                _cachedValue.Add(NetworkValueSerializationAPI<TValue>.Serializer.Deserialize(reader));

            return _cachedValue;
        }

        protected override void OnSerialize(NetWriter writer)
        {
            writer.WritePackedUInt32((uint)InternalValue.Count);
            foreach (var value in InternalValue)
                NetworkValueSerializationAPI<TValue>.Serializer.Serialize(writer, value);
        }

        #region IList

        public TValue this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Value[index];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => InternalValue.Count;
        }

        public bool IsReadOnly
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => !HasPermission;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(TValue item)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            InternalValue.Add(item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange(IEnumerable<TValue> collection)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            InternalValue.AddRange(collection);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            if (InternalValue.Count == 0)
                return;

            InternalValue.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TValue item) =>
            InternalValue.Contains(item);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(TValue[] array, int arrayIndex) =>
            InternalValue.CopyTo(array, arrayIndex);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf(TValue item) =>
            InternalValue.IndexOf(item);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Insert(int index, TValue item)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            InternalValue.Insert(index, item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(TValue item)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            return InternalValue.Remove(item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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