using Core.Networking;
using System;
using System.Collections;
using System.Collections.Generic;

namespace LROTech.Network.StateManagement
{
    public sealed class NetObjectDataCollection : IReadOnlyCollection<NetObjectStateData>
    {
        private readonly NetReader m_Reader;
        private readonly Dictionary<int, NetObjectStateData> m_InternalCollection;
        private readonly Func<bool> m_DataWritePermission;

        public int Count => m_InternalCollection.Count;

        public NetObjectDataCollection(Func<bool> dataWritePermission)
        {
            m_Reader = new NetReader();
            m_InternalCollection = new Dictionary<int, NetObjectStateData>();
            m_DataWritePermission = dataWritePermission;
        }

        public T GetOrCreateData<T>() where T : NetObjectStateData
        {
            var typIndex = NetObjectStateData.GetTypeIndex<T>();
            if (!m_InternalCollection.TryGetValue(typIndex, out var data))
            {
                data = NetObjectStateData.Provide<T>(m_DataWritePermission);
                if (data == null)
                    throw new InvalidOperationException();

                m_InternalCollection.Add(typIndex, data);
            }

            return data as T;
        }

        public bool HasData<T>() where T : NetObjectStateData =>
            m_InternalCollection.ContainsKey(NetObjectStateData.GetTypeIndex<T>());

        public bool TryGetData<T>(out T data) where T : NetObjectStateData
        {
            data = null;
            var typeHashCode = NetObjectStateData.GetTypeIndex<T>();
            var dataExists = m_InternalCollection.TryGetValue(typeHashCode, out var internalData);
            if (dataExists)
                data = internalData as T;

            return dataExists;
        }

        public T GetData<T>() where T : NetObjectStateData
        {
            var typeHashCode = NetObjectStateData.GetTypeIndex<T>();
            return m_InternalCollection.TryGetValue(typeHashCode, out var internalData) ? internalData as T : null;
        }

        internal void Clear() =>
            m_InternalCollection.Clear();

        public NetObjectDataCollectionSnapshot ToSnapshot() =>
            new(m_InternalCollection.Values);

        public void RestoreFromSnapShot(NetObjectDataCollectionSnapshot snapshot)
        {
            m_Reader.Replace(snapshot.Data);

            var dataCount = m_Reader.ReadInt32();
            for (var i = 0; i < dataCount; i++)
            {
                var typeHashCode = m_Reader.ReadInt32();
                if (!NetObjectStateData.TryGetType((byte)typeHashCode, out var type))
                    throw new InvalidOperationException($"Unknown type with hash code '{typeHashCode}'.");

                if (!m_InternalCollection.TryGetValue(typeHashCode, out var data))
                {
                    data = NetObjectStateData.Provide(type, m_DataWritePermission);
                    m_InternalCollection.Add(typeHashCode, data);
                }

                data.DeserializeInternal(m_Reader, firstTime: false);
            }
        }

        internal int GetVersionHash()
        {
            var hash = 13;
            hash = hash * 7 + m_InternalCollection.Count.GetHashCode();
            foreach (var kvp in m_InternalCollection)
            {
                hash = hash * 7 + kvp.Key.GetHashCode();
                hash = hash * 7 + kvp.Value.GetVersionHash();
            }

            return hash;
        }

        public IEnumerator<NetObjectStateData> GetEnumerator() =>
            m_InternalCollection.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() =>
            m_InternalCollection.Values.GetEnumerator();
    }
}