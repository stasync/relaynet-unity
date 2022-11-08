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
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class IgnoreAutoCollection : Attribute { }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class SyncValue : Attribute { }

    public bool HasPermission => WritePemission();
    public readonly int TypeHashCode;

    protected readonly Func<bool> WritePemission;
    protected bool Initialized { get; private set; }

    protected IReadOnlyDictionary<string, SerializableValue> SerializeableValues => m_InternalValueCollection;
    readonly Dictionary<string, SerializableValue> m_InternalValueCollection = new ();

    protected NetObjectStateData(Func<bool> writePermission)
    {
        WritePemission = writePermission;
        TypeHashCode = GetTypeHashCode(GetType());

        // Collect serializable values.
        {
            var valueType = typeof(SerializableValue);
            var fields = GetType().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

            foreach (var filedInfo in fields)
            {
                if (Attribute.IsDefined(filedInfo, typeof(SyncValue)))
                {
                    if (filedInfo.FieldType == typeof(float))
                    {
                        AddValueManual(filedInfo.Name, new SerializabeFloatObserver(() => (float)filedInfo.GetValue(this), v => filedInfo.SetValue(this, v)));
                    }
                    else
                    {
                        Debug.LogError($"'{typeof(SyncValue).FullName}' attribute is defined for field of unsupported type '{filedInfo.FieldType.FullName}'");
                    }

                    continue;
                }

                if (!valueType.IsAssignableFrom(filedInfo.FieldType))
                    continue;

                if (Attribute.IsDefined(filedInfo, typeof(IgnoreAutoCollection)))
                    continue;

                if (filedInfo.GetValue(this) is SerializableValue value)
                    AddValueManual(filedInfo.Name, value);
                else
                    Debug.LogError($"'{GetType().FullName}.{filedInfo.Name}' field is not assigned and will be skipped");
            }
        }

        Initialized = true;
    }

    public override int GetHashCode()
    {
        var hash = TypeHashCode;
        foreach (var value in m_InternalValueCollection.Values)
            hash = (hash * 7) + value.GetHashCode();

        return hash;
    }

    public virtual void Serialize(NetWriter writer)
    {
        foreach (var kvp in SerializeableValues)
            kvp.Value.Serialize(writer);
    }

    public virtual void Deserialize(NetReader reader, bool firtsTime)
    {
        foreach (var value in SerializeableValues.Values)
            value.DeserializeAndApply(reader, firtsTime);
    }

    public override string ToString()
    {
        var result = $"'{GetType().FullName}'";
        foreach (var kvp in SerializeableValues)
            result += $" {kvp.Key}:{kvp.Value}";

        return result;
    }

    protected void AddValueManual(string valueName, SerializableValue value)
    {
        if (Initialized)
            throw new InvalidOperationException("Unable to add new value if data already initialized");

        if (m_InternalValueCollection.ContainsKey(valueName))
            throw new InvalidOperationException($"{GetType().FullName} already contains value with name {valueName}");

        SerializableValue.WritePermissionFieldInfo.SetValue(value, WritePemission);
        m_InternalValueCollection.Add(valueName, value);
    }

    public static NetObjectStateData Provide(Type type, Func<bool> writePermission)
    {
        var dataType = typeof(NetObjectStateData);
        if (!dataType.IsAssignableFrom(type))
            throw new InvalidOperationException($"'{type.FullName} must derive '{dataType.FullName}'");

        return Activator.CreateInstance(type, writePermission) as NetObjectStateData;
    }

    public static T Provide<T>(Func<bool> writePermission) where T : NetObjectStateData
    {
        if (Provide(typeof(T), writePermission) is not T instance)
            throw new InvalidOperationException($"Unable to produse instance of data of type '{typeof(T).FullName}'");

        return instance;
    }


    static int GetTypeHashCode(Type dataType) =>
        Core.Main.IOC.TypeLookup.HashCodeOf(dataType);

    public static int GetTypeHashCode<TData>() where TData : NetObjectStateData =>
        GetTypeHashCode(typeof(TData));

    public static bool TryGetType(int typeHashCode, out Type type) =>
        Core.Main.IOC.TypeLookup.TryGetType(typeHashCode, out type);

#if UNITY_EDITOR
    public virtual IEnumerable<string> GetValuesInfo_Editor()
    {
        foreach (var kvp in m_InternalValueCollection)
            yield return $"{kvp.Key.Replace("k__BackingField", string.Empty)}:{kvp.Value}";
    }

    public int GetSerializedSize_Editor()
    {
        var writer = NetWriter.Get();
        Serialize(writer);
        return writer.Position;
    }
#endif
}
