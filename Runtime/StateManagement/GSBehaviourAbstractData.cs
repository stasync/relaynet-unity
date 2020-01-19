using Core.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public abstract class GSBehaviourAbstractData : ISerializable
{
    protected GSBehaviourAbstractData(Func<bool> writePermission)
    {
        OnDeclareValues(writePermission);
        SerializeableValues = Find
            .FieldReferencesWithFields<ISerializable>(this, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .ToList();
    }

    protected IEnumerable<KeyValuePair<FieldInfo, ISerializable>> SerializeableValues { get; private set; }
    protected abstract void OnDeclareValues(Func<bool> writePermission);

    public bool IsDirty => SerializeableValues != null && SerializeableValues.Any(v => v.Value.IsDirty);
    public virtual void SetDirty()
    {
        if (SerializeableValues != null)
            foreach (var kvp in SerializeableValues)
                kvp.Value.SetDirty();
    }

    /// <summary>
    /// Serialization called by GSInstanceIdentity.
    /// </summary>
    /// <param name="writer"></param>
    public virtual void Serialize(NetWriter writer)
    {
        var needSerialize = IsDirty;
        writer.Write(needSerialize);

        if (!needSerialize)
            return;

        foreach (var kvp in SerializeableValues)
            kvp.Value.Serialize(writer);
    }

    /// <summary>
    /// Deserialization called by GSInstanceIdentity.
    /// </summary>
    /// <param name="reader"></param>
    public virtual void Deserialize(NetReader reader, bool firtsTime)
    {
        //Check if sended object was dirty
        if (!reader.ReadBoolean())
            return;

        foreach (var kvp in SerializeableValues)
            kvp.Value.Deserialize(reader, firtsTime);
    }
}
