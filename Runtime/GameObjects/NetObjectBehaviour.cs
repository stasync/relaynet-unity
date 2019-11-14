using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class GSBehaviourEmptyData : GSBehaviourAbstractData
{
    public GSBehaviourEmptyData(Func<bool> writePermission) : base(writePermission) { }

    protected override void OnDeclareValues(Func<bool> writePermission)
    {
        TestValue = new SerializeableValue<byte>(0, writePermission, (r, f) => r.ReadByte(), (w, v) => w.Write(v));
    }
    //This value in case of backwards compatibility;
    protected SerializeableValue<byte> TestValue { get; private set; } = null;
}

public class SerializeableValue<T> : ISerializeable
{
    readonly Func<NetReader, bool, T> m_Reader;
    readonly Action<NetWriter, T> m_Writer;
    readonly Func<bool> m_WritePermission;

    protected bool HasPermission => m_WritePermission == null || m_WritePermission();

    T m_Value;
    public T Value
    {
        get => m_Value;
        set
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change value {typeof(T).Name}");

            if (m_Value.Equals(value))
                return;

            m_Value = value;
            SetDirty();
        }
    }

    public SerializeableValue(T defaultValue, Func<bool> writePermission, Func<NetReader, bool, T> reader, Action<NetWriter, T> writer)
    {
        m_Value = defaultValue;
        m_WritePermission = writePermission;
        m_Reader = reader;
        m_Writer = writer;
    }

    public bool IsDirty { get; private set; }

    public void Deserialize(NetReader reader, bool firstTime) => m_Value = m_Reader(reader, firstTime);
    public void Serialize(NetWriter writer)
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to serialize value {typeof(T).Name}");

        IsDirty = false;
        m_Writer(writer, Value);
    }

    public void SetDirty()
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to set value {typeof(T).Name} dirty");

        IsDirty = true;
    }

    public static implicit operator T(SerializeableValue<T> value) => value.Value;
}

public abstract class GSBehaviourAbstractData : ISerializeable
{
    protected GSBehaviourAbstractData(Func<bool> writePermission)
    {
        OnDeclareValues(writePermission);
        SerializeableValues = Find
            .FieldReferences<ISerializeable>(this, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .ToList();
    }

    protected IEnumerable<ISerializeable> SerializeableValues { get; private set; }
    protected abstract void OnDeclareValues(Func<bool> writePermission);

    public bool IsDirty => SerializeableValues != null && SerializeableValues.Any(v => v.IsDirty);
    public virtual void SetDirty()
    {
        if (SerializeableValues != null)
            foreach (var v in SerializeableValues)
                v.SetDirty();
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

        foreach (var v in SerializeableValues)
            v.Serialize(writer);
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

        foreach (var v in SerializeableValues)
            v.Deserialize(reader, firtsTime);
    }
}