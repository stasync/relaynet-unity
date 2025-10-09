using Core.Networking;
using System;
using System.Reflection;

public abstract class SerializableValue : ISerializable
{
    private readonly Func<bool> m_WritePermission = null;
    protected bool HasPermission => m_WritePermission();

    public abstract void DeserializeAndApply(NetReader reader, bool firstTime);
    public abstract void ApplyLastDeserialized();
    public abstract void Serialize(NetWriter writer);
    public abstract void Deserialize(NetReader reader, bool firstTime);

    public static readonly FieldInfo WritePermissionFieldInfo;

    static SerializableValue()
    {
        WritePermissionFieldInfo = typeof(SerializableValue)
            .GetField(nameof(m_WritePermission), BindingFlags.Instance | BindingFlags.NonPublic);
    }
}

public abstract class SerializableValueDefinition<TValue, TExposed> : SerializableValue where TValue : TExposed
{
    protected SerializableValueDefinition(TValue defaultValue, Func<NetReader, bool, TValue> reader, Action<NetWriter, TValue> writer)
    {
        m_InternalValue = defaultValue;
        m_Reader = reader;
        m_Writer = writer;
    }

    private readonly Func<NetReader, bool, TValue> m_Reader;
    private readonly Action<NetWriter, TValue> m_Writer;

    public TExposed Value => InternalValue;
    public void Set(TValue value) => InternalValue = value;

    private TValue m_InternalValue;
    private TValue m_LastDeserializedValue;

    protected TValue InternalValue
    {
        get => m_InternalValue;
        set
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change value '{typeof(TValue).FullName}'.");

            m_InternalValue = value;
        }
    }

    public override void DeserializeAndApply(NetReader reader, bool firstTime)
    {
        Deserialize(reader, firstTime);
        ApplyLastDeserialized();
    }

    public override void ApplyLastDeserialized() =>
        m_InternalValue = m_LastDeserializedValue;

    public override void Deserialize(NetReader reader, bool firstTime) =>
        m_LastDeserializedValue = m_Reader(reader, firstTime);

    public override void Serialize(NetWriter writer) =>
         m_Writer(writer, InternalValue);

    public override int GetHashCode() =>
        m_InternalValue.GetHashCode();

    public override string ToString() =>
        m_InternalValue.ToString();
}

