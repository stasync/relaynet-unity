using Core.Networking;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[Obsolete]
public sealed class NetObjectStateEmptyData : NetObjectStateData
{
    public NetObjectStateEmptyData(Func<bool> writePermission) : base(writePermission) { }
}

public abstract class NetObjectStateData
{
    [AttributeUsage(AttributeTargets.Field)]
    public class SyncValue : Attribute { }

    public bool HasWritePermission => WritePermission();
    public readonly int TypeHashCode;

    protected readonly Func<bool> WritePermission;
    protected bool Initialized { get; private set; }

    public IReadOnlyDictionary<string, SerializableValue> SerializableValues => m_InternalValueCollection;
    private readonly Dictionary<string, SerializableValue> m_InternalValueCollection = new ();

    protected NetObjectStateData(Func<bool> writePermission)
    {
        WritePermission = writePermission;
        TypeHashCode = GetTypeIndex(GetType());

        // Collect serializable values.
        // TODO: FIELD CACHE.
        foreach (var filedInfo in GetType().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (typeof(SerializableValue).IsAssignableFrom(filedInfo.FieldType))
            {
                if (filedInfo.GetValue(this) is SerializableValue value)
                    AddValueManual(filedInfo.Name, value);
                else
                    Debug.LogError($"'{GetType().FullName}.{filedInfo.Name}' field is not assigned and will be skipped.");
                    
                continue;
            }

            // TODO: The following is EXPERIMENTAL.
            if (Attribute.IsDefined(filedInfo, typeof(SyncValue)))
            {
                /*if (filedInfo.FieldType == typeof(float))
                    AddValueManual(filedInfo.Name, new SerializableFloatObserver(() => (float)filedInfo.GetValue(this), v => filedInfo.SetValue(this, v)));
                else
                    Debug.LogError($"'{typeof(SyncValue).FullName}' attribute is defined for field of unsupported type '{filedInfo.FieldType.FullName}'.");*/
            }
        }

        Initialized = true;
    }

    public override int GetHashCode()
    {
        var hash = TypeHashCode;
        foreach (var value in m_InternalValueCollection.Values)
            hash = hash * 7 + value.GetHashCode();

        return hash;
    }

    public virtual void Serialize(NetWriter writer)
    {
        foreach (var kvp in m_InternalValueCollection)
            kvp.Value.Serialize(writer);
    }

    public virtual void Deserialize(NetReader reader, bool firstTime)
    {
        foreach (var value in m_InternalValueCollection.Values)
            value.DeserializeAndApply(reader, firstTime);
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
        if (Initialized)
            throw new InvalidOperationException("Unable to add new value if data already initialized.");

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

    public static T Provide<T>(Func<bool> writePermission) where T : NetObjectStateData
    {
        if (Provide(typeof(T), writePermission) is not T instance)
            throw new InvalidOperationException($"Unable to produce instance of data of type '{typeof(T).FullName}'.");

        return instance;
    }
    
    private static byte GetTypeIndex(Type dataType) =>
        Core.Utils.Types.Lookup<NetObjectStateData>.IndexOf(dataType);

    public static int GetTypeIndex<TData>() where TData : NetObjectStateData =>
        GetTypeIndex(typeof(TData));

    public static bool TryGetType(byte index, out Type type) =>
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
        Serialize(writer);
        return writer.Position;
    }
#endif
}
