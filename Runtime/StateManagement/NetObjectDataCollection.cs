using Keel.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LROTech.Network.StateManagement.Snapshots;

namespace LROTech.Network.StateManagement
{
    public sealed class NetObjectDataCollection : IReadOnlyCollection<NetObjectStateData>
    {
        public int Count => _internalCollection.Count;

        private readonly NetReader _reader = new();
        private readonly Dictionary<int, NetObjectStateData> _internalCollection = new();
        private readonly Func<bool> _validator;
        private readonly Func<bool> _dataWritePermission;

        // Helper scratch buffers.
        private readonly HashSet<int> _restoredTypes = new();
        private readonly List<int> _typesToRemove = new();

        internal NetObjectDataCollection(Func<bool> validator, Func<bool> dataWritePermission)
        {
            _validator = validator;
            _dataWritePermission = dataWritePermission;
        }

        public T GetOrCreateData<T>() where T : NetObjectStateData, new()
        {
            EnsureValid();
            if (!_dataWritePermission())
                throw new InvalidOperationException("No authority to modify the data collection.");

            var typIndex = NetObjectStateData.GetTypeIndex<T>();
            if (_internalCollection.TryGetValue(typIndex, out var data))
                return data as T;

            data = NetObjectStateData.Provide<T>(_validator, _dataWritePermission);
            if (data == null)
                throw new InvalidOperationException();

            _internalCollection.Add(typIndex, data);
            return (T)data;
        }

        public bool RemoveData<T>() where T : NetObjectStateData, new()
        {
            EnsureValid();
            if (!_dataWritePermission())
                throw new InvalidOperationException("No authority to modify the data collection.");

            var removed = _internalCollection.Remove(NetObjectStateData.GetTypeIndex<T>(), out var data);
            if (removed)
                data.Invalidate();

            return removed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasData<T>() where T : NetObjectStateData, new()
        {
            EnsureValid();
            return _internalCollection.ContainsKey(NetObjectStateData.GetTypeIndex<T>());
        }

        public bool TryGetData<T>(out T data) where T : NetObjectStateData, new()
        {
            EnsureValid();

            data = null;
            var typeHashCode = NetObjectStateData.GetTypeIndex<T>();
            var dataExists = _internalCollection.TryGetValue(typeHashCode, out var internalData);
            if (dataExists)
                data = internalData as T;

            return dataExists;
        }

        public T GetData<T>() where T : NetObjectStateData, new()
        {
            EnsureValid();

            var typeHashCode = NetObjectStateData.GetTypeIndex<T>();
            return _internalCollection.TryGetValue(typeHashCode, out var internalData) ? internalData as T : null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal NetObjectDataCollectionSnapshot ToSnapshot() =>
            new(_internalCollection.Values);

        internal void RestoreFromSnapShot(NetObjectDataCollectionSnapshot snapshot)
        {
            _reader.Replace(snapshot.Data);
            _restoredTypes.Clear();

            var dataCount = _reader.ReadInt32();
            for (var i = 0; i < dataCount; i++)
            {
                var typeHashCode = _reader.ReadInt32();
                if (!NetObjectStateData.TryGetType((byte)typeHashCode, out var type))
                    throw new InvalidOperationException($"Unknown type with hash code '{typeHashCode}'.");

                if (!_internalCollection.TryGetValue(typeHashCode, out var data))
                {
                    data = NetObjectStateData.Provide(type, _validator, _dataWritePermission);
                    _internalCollection.Add(typeHashCode, data);
                }

                _restoredTypes.Add(typeHashCode);
                data.DeserializeInternal(_reader);
            }

            // The snapshot carries the whole collection, so everything missing from it was removed by the owner.
            if (_internalCollection.Count == _restoredTypes.Count)
                return;

            _typesToRemove.Clear();
            foreach (var typeHashCode in _internalCollection.Keys)
            {
                if (!_restoredTypes.Contains(typeHashCode))
                    _typesToRemove.Add(typeHashCode);
            }

            foreach (var typeHashCode in _typesToRemove)
            {
                if (_internalCollection.Remove(typeHashCode, out var data))
                    data.Invalidate();
            }
        }

        internal void Invalidate()
        {
            foreach (var data in _internalCollection.Values)
                data.Invalidate();

            _internalCollection.Clear();
        }

        internal int GetVersionHash()
        {
            var hash = 13;
            hash = hash * 7 + _internalCollection.Count.GetHashCode();
            foreach (var kvp in _internalCollection)
            {
                hash = hash * 7 + kvp.Key.GetHashCode();
                hash = hash * 7 + kvp.Value.GetVersionHash();
            }

            return hash;
        }

        public IEnumerator<NetObjectStateData> GetEnumerator()
        {
            EnsureValid();
            return _internalCollection.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            EnsureValid();
            return _internalCollection.Values.GetEnumerator();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValid() =>
            _validator != null && _validator();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureValid()
        {
            if (!IsValid())
                throw new InvalidOperationException($"Object of type '{GetType().FullName}' is invalid.");
        }
    }
}