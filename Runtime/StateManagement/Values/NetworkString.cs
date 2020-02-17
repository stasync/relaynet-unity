using System;
using Core.Networking;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class SerializableFloat : SerializableValue<float>
{
    public SerializableFloat(float defaultValue, Func<bool> writePermission) : 
        base(defaultValue, writePermission, (r, f) => r.ReadSingle(), (w, v) => w.Write(v))
    { }

    public void Change(float value) => Set(InternalValue + value);
}

public class SerializableInt : SerializableValue<int>
{
    public SerializableInt(int defaultValue, Func<bool> writePermission) :
        base(defaultValue, writePermission, (r, f) => r.ReadInt32(), (w, v) => w.Write(v))
    { }

    public void Change(int value) => Set(InternalValue + value);
}

public class SerializableBool : SerializableValue<bool>
{
    public SerializableBool(bool defaultValue, Func<bool> writePermission) :
        base(defaultValue, writePermission, (r, f) => r.ReadBoolean(), (w, v) => w.Write(v))
    { }
}

public class SerializableByte : SerializableValue<byte>
{
    public SerializableByte(byte defaultValue, Func<bool> writePermission) :
        base(defaultValue, writePermission, (r, f) => r.ReadByte(), (w, v) => w.Write(v))
    { }
}

public class SerializableString : SerializableValue<string>
{
    public SerializableString(string defaultValue, Func<bool> writePermission) :
        base(defaultValue, writePermission, (r, f) => r.ReadString(), (w, v) => w.Write(v))
    { }
}

public class SerializableUInt : SerializableValue<uint>
{
    public SerializableUInt(uint defaultValue, Func<bool> writePermission) :
        base(defaultValue, writePermission, (r, f) => r.ReadPackedUInt32(), (w, v) => w.WritePackedUInt32(v))
    { }

    public void Change(uint value) => Set(InternalValue + value);
}

public class SerializableVector3: SerializableValue<Vector3>
{
    public SerializableVector3(Vector3 defaultValue, Func<bool> writePermission) :
        base(defaultValue, writePermission, (r, f) => r.ReadVector3(), (w, v) => w.Write(v))
    { }
}

public class SerializableVector2 : SerializableValue<Vector2>
{
    public SerializableVector2(Vector2 defaultValue, Func<bool> writePermission) :
        base(defaultValue, writePermission, (r, f) => r.ReadVector2(), (w, v) => w.Write(v))
    { }
}

public class SerializableQuaternion : SerializableValue<Quaternion>
{
    public SerializableQuaternion(Quaternion defaultValue, Func<bool> writePermission) :
        base(defaultValue, writePermission, (r, f) => r.ReadQuaternion(), (w, v) => w.Write(v))
    { }
}

public class SerializableValueCollection : ISerializableValue
{
    public SerializableValueCollection(Func<bool> collectionChangePermission, Dictionary<string, ISerializableValue> baseValues = null)
    {
        CollectionChangePermission = collectionChangePermission ?? throw new ArgumentNullException(nameof(collectionChangePermission));
        m_InternalCollection = new Dictionary<string, ISerializableValue>();
        if (baseValues != null && baseValues.Any())
            foreach(var kvp in baseValues)
                m_InternalCollection.Add(kvp.Key, kvp.Value);
    }

    protected readonly Func<bool> CollectionChangePermission;
    readonly Dictionary<string, ISerializableValue> m_InternalCollection;

    public bool IsDirty => m_InternalCollection.Values.Any(v => v.IsDirty);

    public void Add(string valueName, ISerializableValue value)
    {
        if (!CollectionChangePermission())
            throw new InvalidOperationException("No permission to change collection");

        if (m_InternalCollection.ContainsKey(valueName))
            Debug.LogError($"Value collection already contains value with name {valueName}");
        else
            m_InternalCollection.Add(valueName, value);
    }

    public void AddRange(string valueName, Dictionary<string, ISerializableValue> values)
    {
        if (!CollectionChangePermission())
            throw new InvalidOperationException("No permission to change collection");

        foreach (var kvp in values)
            Add(kvp.Key, kvp.Value);
    }

    public void Deserialize(NetReader reader, bool firstTime)
    {
        foreach (var v in m_InternalCollection.Values)
            v.Deserialize(reader, firstTime);
    }

    public void Serialize(NetWriter writer)
    {
        foreach (var v in m_InternalCollection.Values)
            v.Serialize(writer);
    }

    public void SetDirty()
    {
        foreach (var v in m_InternalCollection.Values)
            v.SetDirty();
    }

    public string ToJSONProperty() =>
            RealtimeJSONUtils
                .ToJSONCollection(m_InternalCollection.Select(kvp => "{" + RealtimeJSONUtils.ToJSONValue(kvp.Key.ToString()) + ":" + kvp.Value.ToJSONProperty() + "}")
                .ToArray());
}