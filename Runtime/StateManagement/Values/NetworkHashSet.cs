using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Keel.Networking;
using LROTech.Network.StateManagement.Values.Serialization;

namespace LROTech.Network.StateManagement.Values
{
    public class NetworkHashSet<TValue> : NetworkValueManaged<HashSet<TValue>, IReadOnlyCollection<TValue>>, IReadOnlyCollection<TValue>
        where TValue : unmanaged
    {
        private readonly HashSet<TValue> _cachedValue = new();

        public NetworkHashSet(HashSet<TValue> defaultValue = null) : base(defaultValue ?? new HashSet<TValue>())
        {
        }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => InternalValue.Count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Add(TValue value)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change hash of type {typeof(TValue).Name}");

            return InternalValue.Add(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(TValue value)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change hash of type {typeof(TValue).Name}");

            return InternalValue.Remove(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TValue value) =>
            InternalValue.Contains(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        public IEnumerator<TValue> GetEnumerator() =>
            InternalValue.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() =>
            InternalValue.GetEnumerator();

        public override string ToString() =>
            $"{InternalValue.Count} [{string.Join("; ", InternalValue)}]";
    }
}