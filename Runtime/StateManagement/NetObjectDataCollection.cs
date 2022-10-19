using Core.Networking;
using System;
using System.Collections;
using System.Collections.Generic;

public interface IReadOnlyNetObjectDataCollection : IReadOnlyCollection<NetObjectStateData>
{
    NetObjectDataCollectionSnapshot ToSnapshot();
    T GetOrCreateData<T>() where T : NetObjectStateData;
    bool TryGetData<T>(out T data) where T : NetObjectStateData;
    T GetData<T>() where T : NetObjectStateData;
    bool HasData<T>() where T : NetObjectStateData;
}

public class NetObjectDataCollection : IReadOnlyNetObjectDataCollection
{
    readonly NetReader m_Reader;
    readonly Dictionary<int, NetObjectStateData> m_InternalCollection;
    readonly Func<bool> m_DataWritePemission;

    public int Count => m_InternalCollection.Count;

    public NetObjectDataCollection(Func<bool> dataWritePermission)
    {
        m_Reader = new NetReader();
        m_InternalCollection = new Dictionary<int, NetObjectStateData>();
        m_DataWritePemission = dataWritePermission;
    }

    public T GetOrCreateData<T>() where T : NetObjectStateData
    {
        var typeHashCode = NetObjectStateData.GetTypeHashCode<T>();
        if (!m_InternalCollection.TryGetValue(typeHashCode, out var data))
        {
            data = NetObjectStateData.Provide<T>(m_DataWritePemission);
            if (data == null)
                throw new InvalidOperationException();

            m_InternalCollection.Add(typeHashCode, data);
        }

        return data as T;
    }

    public bool HasData<T>() where T : NetObjectStateData =>
        m_InternalCollection.ContainsKey(NetObjectStateData.GetTypeHashCode<T>());

    public bool TryGetData<T>(out T data) where T : NetObjectStateData
    {
        data = null;
        var typeHashCode = NetObjectStateData.GetTypeHashCode<T>();
        var dataExists = m_InternalCollection.TryGetValue(typeHashCode, out var internalData);
        if (dataExists)
            data = internalData as T;

        return dataExists;
    }

    public T GetData<T>() where T : NetObjectStateData
    {
        var typeHashCode = NetObjectStateData.GetTypeHashCode<T>();
        return m_InternalCollection.TryGetValue(typeHashCode, out var internalData) ? internalData as T : null;
    }

    public void Clear() => m_InternalCollection.Clear();

    public NetObjectDataCollectionSnapshot ToSnapshot() => new(m_InternalCollection.Values);

    public void RestoreFromSnapShot(NetObjectDataCollectionSnapshot snapshot)
    {
        m_Reader.Replace(snapshot.Data);

        var dataCount = m_Reader.ReadInt32();
        for (var i = 0; i < dataCount; i++)
        {
            var typeHashCode = m_Reader.ReadInt32();
            if (!NetObjectStateData.TryGetType(typeHashCode, out var type))
                throw new InvalidOperationException();

            if (!m_InternalCollection.TryGetValue(typeHashCode, out var data))
            {
                data = NetObjectStateData.Provide(type, m_DataWritePemission);
                m_InternalCollection.Add(typeHashCode, data);
            }

            data.Deserialize(m_Reader, false);
        }
    }

    public override int GetHashCode()
    {
        var hash = 13;
        foreach (var kvp in m_InternalCollection)
        {
            hash = (hash * 7) + kvp.Key.GetHashCode();
            hash = (hash * 7) + kvp.Value.GetHashCode();
        }
        return hash;
    }

    public IEnumerator<NetObjectStateData> GetEnumerator() => m_InternalCollection.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => m_InternalCollection.Values.GetEnumerator();
}