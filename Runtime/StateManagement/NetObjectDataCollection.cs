using Core.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LROTech.Network.StateManagement.Snapshots;

namespace LROTech.Network.StateManagement
{
    public sealed class NetObjectDataCollection : IReadOnlyCollection<NetObjectStateData>
    {
        private readonly NetReader _reader;
        private readonly Dictionary<int, NetObjectStateData> _internalCollection;
        private readonly Func<bool> _validator;
        private readonly Func<bool> _dataWritePermission;

        public int Count => _internalCollection.Count;

        internal NetObjectDataCollection(Func<bool> validator, Func<bool> dataWritePermission)
        {
            _reader = new NetReader();
            _internalCollection = new Dictionary<int, NetObjectStateData>();
            _validator = validator;
            _dataWritePermission = dataWritePermission;
        }

        public T GetOrCreateData<T>() where T : NetObjectStateData, new()
        {
            //if (!_dataWritePermission())
            //    throw new InvalidOperationException("No authority to modify date collection.");
            
            var typIndex = NetObjectStateData.GetTypeIndex<T>();
            if (_internalCollection.TryGetValue(typIndex, out var data))
                return data as T;

            data = NetObjectStateData.Provide<T>(_dataWritePermission);
            if (data == null)
                throw new InvalidOperationException();

            _internalCollection.Add(typIndex, data);
            return (T)data;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasData<T>() where T : NetObjectStateData, new() =>
            _internalCollection.ContainsKey(NetObjectStateData.GetTypeIndex<T>());

        public bool TryGetData<T>(out T data) where T : NetObjectStateData, new()
        {
            data = null;
            var typeHashCode = NetObjectStateData.GetTypeIndex<T>();
            var dataExists = _internalCollection.TryGetValue(typeHashCode, out var internalData);
            if (dataExists)
                data = internalData as T;

            return dataExists;
        }

        public T GetData<T>() where T : NetObjectStateData, new()
        {
            var typeHashCode = NetObjectStateData.GetTypeIndex<T>();
            return _internalCollection.TryGetValue(typeHashCode, out var internalData) ? internalData as T : null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public NetObjectDataCollectionSnapshot ToSnapshot() =>
            new(_internalCollection.Values);

        internal void RestoreFromSnapShot(NetObjectDataCollectionSnapshot snapshot)
        {
            _reader.Replace(snapshot.Data);

            var dataCount = _reader.ReadInt32();
            for (var i = 0; i < dataCount; i++)
            {
                var typeHashCode = _reader.ReadInt32();
                if (!NetObjectStateData.TryGetType((byte)typeHashCode, out var type))
                    throw new InvalidOperationException($"Unknown type with hash code '{typeHashCode}'.");

                if (!_internalCollection.TryGetValue(typeHashCode, out var data))
                {
                    data = NetObjectStateData.Provide(type, _dataWritePermission);
                    _internalCollection.Add(typeHashCode, data);
                }

                data.DeserializeInternal(_reader);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Clear() =>
            _internalCollection.Clear();

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

        public IEnumerator<NetObjectStateData> GetEnumerator() =>
            _internalCollection.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() =>
            _internalCollection.Values.GetEnumerator();

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