#define VALIDATE_COLLECTED_FIELDS
using Core.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class IgnoreAutoCollection : Attribute { }

public abstract class GSBehaviourAbstractData : IJSONSerializable
{
    protected GSBehaviourAbstractData() { }

    protected bool Initialized { get; private set; }

    protected virtual bool InternalPermnission => true;
    public bool HasPermission => WritePemission();
    protected Func<bool> WritePemission { get; private set; }

    protected IReadOnlyDictionary<string, ISerializableValue> SerializeableValues => m_InternalValueCollection;
    Dictionary<string, ISerializableValue> m_InternalValueCollection;

    public void Initialize(Func<bool> externalWritePermission, Action onAfterValuesCollectedProcessor)
    {
        if (Initialized)
            throw new InvalidOperationException($"{GetType().FullName} aready initilized");

        if (externalWritePermission == null)
            throw new ArgumentNullException(nameof(externalWritePermission));

        if (onAfterValuesCollectedProcessor == null)
            throw new ArgumentNullException(nameof(onAfterValuesCollectedProcessor));

        WritePemission = () => externalWritePermission() && Initialized && InternalPermnission;

        OnDefineValues();
        CollectValues();
        OnValuesCollected();

        onAfterValuesCollectedProcessor();

        Initialized = true;

        OnInitialized();
    }

    protected abstract void OnDefineValues();

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
        foreach(var pvp in properties)
        {
            if(pvp.Key.SetMethod.IsPublic)
            {
                Debug.LogError($"{GetType().FullName}.{pvp.Key.Name} has public setter and will be ignored by auto collector." +
                    $"Use private setter instead");
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

    protected virtual void OnValuesCollected() { }
    protected virtual void OnInitialized() { }

    /// <summary>
    /// Serialization called by GSInstanceIdentity.
    /// </summary>
    /// <param name="writer"></param>
    public virtual void Serialize(NetWriter writer)
    {
        foreach (var kvp in SerializeableValues)
            kvp.Value.Serialize(writer);
    }

    /// <summary>
    /// Deserialization called by GSInstanceIdentity.
    /// </summary>
    /// <param name="reader"></param>
    public virtual void Deserialize(NetReader reader, bool firtsTime)
    {
        foreach (var kvp in SerializeableValues)
            kvp.Value.Deserialize(reader, firtsTime);
    }

    public string ToJSONProperty() => 
        RealtimeJSONUtils.ToJSONCollection(SerializeableValues
            .Select(kvp => "{" + RealtimeJSONUtils.ToJSONValue(kvp.Key) + ":" + kvp.Value.ToJSONProperty() + "}")
            .ToArray());
}
