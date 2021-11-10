#define VALIDATE_COLLECTED_FIELDS

using Core.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public sealed class NetObjectStateEmptyData : NetObjectStateData
{
    public NetObjectStateEmptyData(Func<bool> writePermission) : base(writePermission) { }
}

public abstract class NetObjectStateData
{
    public bool HasPermission => WritePemission();
    public readonly int TypeHashCode;

    protected readonly Func<bool> WritePemission;
    protected bool Initialized { get; private set; }

#if UNITY_EDITOR
    public IEnumerable<string> GetValuePairs_Editor()
    {
        foreach (var kvp in m_InternalValueCollection)
            yield return $"{kvp.Key}:{kvp.Value}";
    }
#endif

    protected IReadOnlyDictionary<string, ISerializableValue> SerializeableValues => m_InternalValueCollection;
    Dictionary<string, ISerializableValue> m_InternalValueCollection;

    protected NetObjectStateData(Func<bool> writePermission)
    {
        WritePemission = writePermission;
        TypeHashCode = GetTypeHashCode(GetType());

        OnDefineValues();
        CollectValues();

        Initialized = true;
    }

    protected virtual void OnDefineValues() { }

    public override int GetHashCode()
    {
        var hash = GetType().GetHashCode();
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

    void CollectValues()
    {
        var bindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;
        var properties = Find.PropertyReferencesWithInfo<ISerializableValue>(this, bindingFlags);

#if VALIDATE_COLLECTED_FIELDS
        var fields = Find.FieldReferencesWithInfo<ISerializableValue>(this, bindingFlags);
        foreach (var fvp in fields)
        {
            if (properties.Any(pvp => fvp.Key.Name.Equals($"<{pvp.Key.Name}>k__BackingField"))) //Skip backing fields;
                continue;

            Debug.LogError($"{GetType().FullName}.{fvp.Key.Name} is a field and will be ignored by auto collector. " +
                $"Use property pattern instead: {fvp.Key.Name} {"{ get; private set; }"}");
        }
#endif

        m_InternalValueCollection = new Dictionary<string, ISerializableValue>();
        foreach (var pvp in properties)
        {
            if (pvp.Key.SetMethod != null && pvp.Key.SetMethod.IsPublic)
            {
                Debug.LogError($"{GetType().FullName}.{pvp.Key.Name} has public setter and will be ignored by auto collector. Use private setter instead");
                continue;
            }

            if (pvp.Key.GetCustomAttributes<IgnoreAutoCollection>().Any())
                continue;

            if (pvp.Value == null)
                Debug.LogError($"{GetType().FullName}.{pvp.Key.Name} value is not assigned and will be skipped");
            else
                AddValueManual(pvp.Key.Name, pvp.Value);
        }
    }

    protected void AddValueManual(string valueName, ISerializableValue value)
    {
        if (Initialized)
            throw new InvalidOperationException("Unable to add new value if data already initialized");

        if (m_InternalValueCollection.ContainsKey(valueName))
            throw new InvalidOperationException($"{GetType().FullName} already contains value with name {valueName}");

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
        if (!(Provide(typeof(T), writePermission) is T instance))
            throw new InvalidOperationException($"Unable to produse instance of data of type '{typeof(T).FullName}'");

        return instance;
    }


    static int GetTypeHashCode(Type dataType) =>
        Core.Main.TypeLookup.HashCodeOf(dataType);

    public static int GetTypeHashCode<TData>() where TData : NetObjectStateData =>
        GetTypeHashCode(typeof(TData));

    public static bool TryGetType(int typeHashCode, out Type type) =>
        Core.Main.TypeLookup.TryGetType(typeHashCode, out type);
}
