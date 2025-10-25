using Core.Networking;
using System;
using System.Collections.Generic;
using UnityEngine;

public abstract class NetObjectStateData
{
    public bool HasWritePermission => WritePermission();
    public readonly int TypeHashCode;

    protected readonly Func<bool> WritePermission;

    public IReadOnlyDictionary<string, SerializableValue> SerializableValues => m_InternalValueCollection;
    private readonly Dictionary<string, SerializableValue> m_InternalValueCollection = new ();

    protected NetObjectStateData(Func<bool> writePermission)
    {
        WritePermission = writePermission;
        TypeHashCode = GetTypeIndex(GetType());

        // Collect serializable values.
        foreach (var filedInfo in SerializableValue.GetFields(GetType()))
        {
            if (filedInfo.GetValue(this) is SerializableValue value)
                AddValueManual(filedInfo.Name, value);
            else
                Debug.LogError($"'{GetType().FullName}.{filedInfo.Name}' field is not assigned and will be skipped.");
        }
    }

    public override int GetHashCode()
    {
        var hash = TypeHashCode;
        foreach (var value in m_InternalValueCollection.Values)
            hash = hash * 7 + value.GetHashCode();

        return hash;
    }

    internal virtual void SerializeInternal(NetWriter writer)
    {
        foreach (var kvp in m_InternalValueCollection)
            kvp.Value.Serialize(writer);

        OnSerialize(writer);
    }

    protected virtual void OnSerialize(NetWriter writer)
    {
    }

    internal virtual void DeserializeInternal(NetReader reader, bool firstTime)
    {
        foreach (var value in m_InternalValueCollection.Values)
            value.DeserializeAndApply(reader, firstTime);
        
        OnDeserialize(reader, firstTime);
    }
    
    protected virtual void OnDeserialize(NetReader reader, bool firstTime)
    {
    }

    public override string ToString()
    {
        var result = $"'{GetType().FullName}'";
        foreach (var kvp in m_InternalValueCollection)
            result += $" {kvp.Key}:{kvp.Value}";

        return result;
    }

    protected void AddValueManual(string valueName, SerializableValue value)
    {
        if (m_InternalValueCollection.ContainsKey(valueName))
            throw new InvalidOperationException($"'{GetType().FullName}' already contains value with name '{valueName}'.");

        SerializableValue.WritePermissionFieldInfo.SetValue(value, WritePermission);
        m_InternalValueCollection.Add(valueName, value);
    }

    public static NetObjectStateData Provide(Type type, Func<bool> writePermission)
    {
        var dataType = typeof(NetObjectStateData);
        if (!dataType.IsAssignableFrom(type))
            throw new InvalidOperationException($"'{type.FullName} must derive '{dataType.FullName}'.");

        // TODO: we need to avoid having to implement default constructor. https://stackoverflow.com/questions/40700550/set-field-or-property-value-before-constructor
        return Activator.CreateInstance(type, writePermission) as NetObjectStateData;
    }

    internal static T Provide<T>(Func<bool> writePermission) where T : NetObjectStateData
    {
        if (Provide(typeof(T), writePermission) is not T instance)
            throw new InvalidOperationException($"Unable to produce instance of data of type '{typeof(T).FullName}'.");

        return instance;
    }
    
    private static byte GetTypeIndex(Type dataType) =>
        Core.Utils.Types.Lookup<NetObjectStateData>.IndexOf(dataType);

    internal static int GetTypeIndex<TData>() where TData : NetObjectStateData =>
        GetTypeIndex(typeof(TData));

    internal static bool TryGetType(byte index, out Type type) =>
        Core.Utils.Types.Lookup<NetObjectStateData>.WithIndex(index, out type);

#if UNITY_EDITOR
    public virtual IEnumerable<string> GetValuesInfo_Editor()
    {
        foreach (var kvp in m_InternalValueCollection)
            yield return $"{kvp.Key.Replace("k__BackingField", string.Empty)}:{kvp.Value}";
    }

    public int GetSerializedSize_Editor()
    {
        var writer = new NetWriter();
        SerializeInternal(writer);
        return writer.Position;
    }
#endif
}
