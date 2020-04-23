using Core.Networking;
using System;

public interface IJSONSerializable
{
    string ToJSONProperty();
}

public interface ISerializableValue : ISerializable, IJSONSerializable
{
}

public abstract class SerializableValueDefinition<TValue, TExposed> : ISerializableValue where TValue : TExposed
{
    public SerializableValueDefinition(
        TValue defaultValue, 
        Func<bool> writePermission, 
        Func<NetReader, bool, TValue> reader, 
        Action<NetWriter, TValue> writer)
    {
        m_InternalValue = defaultValue;
        m_WritePermission = writePermission;
        m_Reader = reader;
        m_Writer = writer;
    }

    readonly Func<bool> m_WritePermission;
    readonly Func<NetReader, bool, TValue> m_Reader;
    readonly Action<NetWriter, TValue> m_Writer;

    protected bool HasPermission => m_WritePermission == null || m_WritePermission();

    public TExposed Value => InternalValue;
    public void Set(TValue value) => InternalValue = value;

    TValue m_InternalValue;
    protected TValue InternalValue
    {
        get => m_InternalValue;
        set
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change value {typeof(TValue).FullName}");

            if (m_InternalValue.Equals(value))
                return;

            m_InternalValue = value;
        }
    }

    public virtual void Deserialize(NetReader reader, bool firstTime) => m_InternalValue = m_Reader(reader, firstTime);

    public virtual void Serialize(NetWriter writer)
    {
        if (!HasPermission)
            throw new InvalidOperationException($"No authority to serialize value {typeof(TValue).FullName}");

        m_Writer(writer, InternalValue);
    }

    public abstract string ToJSONProperty();
}

