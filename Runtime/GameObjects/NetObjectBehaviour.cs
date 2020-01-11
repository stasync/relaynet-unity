using Core.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// TODO:
/// </summary>
/// <typeparam name="T"></typeparam>
public class SerializableCollection<T> : SerializableValue<List<T>>
{
    public SerializableCollection(Func<bool> writePermission, Func<NetReader, bool, List<T>> reader, Action<NetWriter, List<T>> writer) : base(new List<T>(), writePermission, reader, writer)
    {

    }
}

public class SerializableValue<T> : ISerializeable
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

    public SerializableValue(T defaultValue, Func<bool> writePermission, Func<NetReader, bool, T> reader, Action<NetWriter, T> writer)
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

    public static implicit operator T(SerializableValue<T> value) => value.Value;
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

[RequireComponent(typeof(GSInstanceIdentity))]
public abstract class GSInstanceBehaviour<T> : MonoBehaviour, IGSInstanceBehaviour where T : GSBehaviourAbstractData
{
    protected ILocator Locator => m_Locator.Instance;
    ServiceProvider<ILocator> m_Locator = new ServiceProvider<ILocator>();

    protected GSNetwork Network => m_Network.Instance;
    ServiceProvider<GSNetwork> m_Network = new ServiceProvider<GSNetwork>();

    public GSInstanceIdentity Identity =>
        m_Identity != null ?
        m_Identity :
        m_Identity = GetComponent<GSInstanceIdentity>();

    GSInstanceIdentity m_Identity;

    T m_Data;
    protected abstract T ProduceData();
    protected T Data => m_Data ?? (m_Data = ProduceData());

    public INetRTPlayer Owner => Identity.Owner;

    public bool IsLocal => Identity.IsLocal;

    public bool IsLocalPlayerTagObject => Identity.IsLocalPlayerTagObject;

    public bool IsSceneObject => Identity.IsSceneObject;

    public virtual bool IsDirty => Data.IsDirty;

    public bool IsOwnedByMasterClient => Identity.IsOwnedByMasterClient;

    /// <summary>
    /// Calls IGSInstanceObservable.SetDirty() with IsLocal check.
    /// </summary>
    protected void TrySetDirty()
    {
        if (IsLocal)
            SetDirty();
    }

    /// <summary>
    /// Calls IGSInstanceObservable.SetDirty().
    /// </summary>
    protected void SetDirty()
    {
        if (!IsLocal)
            throw new Exception("Trying to set dirty remote obect behaviour, this is not allowed");

        Data.SetDirty();
    }

    void ISerializeable.SetDirty() => SetDirty();

    /// <summary>
    /// RPC, invokes locally and sends to ALL clients, who HAS NOT AUTHORITY on this object.
    /// </summary>
    /// <param name="rpcCode"></param>
    /// <param name="data"></param>
    public void OwnerToOthersRPC(
        Action<byte[]> rpcHandlerMethod,
        byte[] data,
        DeliveryIntent deliveryIntent = DeliveryIntent.Reliable,
        bool invokeLocally = true) =>
                Identity.OwnerToOthersRPC(rpcHandlerMethod, data, deliveryIntent, invokeLocally);

    /// <summary>
    /// RPC, invokes locally and sends to ALL clients, who HAS NOT AUTHORITY on this object.
    /// </summary>
    /// <param name="rpcCode"></param>
    /// <param name="data"></param>
    public void OwnerToOthersRPC(
        string rpcCode,
        byte[] data,
        DeliveryIntent deliveryIntent = DeliveryIntent.Reliable,
        bool invokeLocally = true) =>
                Identity.OwnerToOthersRPC(rpcCode, data, deliveryIntent, invokeLocally);


    /// <summary>
    /// Same as OwnerToOthersRPC, but buffered.
    /// </summary>
    /// <param name="rpcCode"></param>
    /// <param name="data"></param>
    public void OwnerToOthersRPCBuffered(Action<byte[]> rpcHandlerMethod, byte[] data) =>
        Identity.OwnerToOthersRPCBuffered(rpcHandlerMethod, data);

    /// <summary>
    /// Same as OwnerToOthersRPC, but buffered.
    /// </summary>
    /// <param name="rpcCode"></param>
    /// <param name="data"></param>
    public void OwnerToOthersRPCBuffered(string rpcCode, byte[] data) =>
        Identity.OwnerToOthersRPCBuffered(rpcCode, data);

    /// <summary>
    /// RPC variation, sends RPC to SINGLE client, who HAS AUTHORITY on this object.
    /// IF we have authority, just invokes locally without sending data.
    /// </summary>
    /// <param name="rpcCode"></param>
    /// <param name="data"></param>
    public void ToOwnerRPC(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryIntent deliveryIntent = DeliveryIntent.Reliable) =>
        Identity.ToOwnerRPC(rpcHandlerMethod, data, deliveryIntent);

    /// <summary>
    /// RPC variation, sends RPC to SINGLE client, who HAS AUTHORITY on this object.
    /// IF we have authority, just invokes locally without sending data.
    /// </summary>
    /// <param name="rpcCode"></param>
    /// <param name="data"></param>
    public void ToOwnerRPC(string rpcCode, byte[] data, DeliveryIntent deliveryIntent = DeliveryIntent.Reliable) =>
        Identity.ToOwnerRPC(rpcCode, data, deliveryIntent);

    /// <summary>
    /// RPC variation, sends RPC to MASTER client.
    /// It does not matter whether MASTER client is a owner of this object or not.
    /// </summary>
    /// <param name="rpcCode"></param>
    /// <param name=""></param>
    public void MasterRPC(string rpcCode, byte[] data, DeliveryIntent deliveryIntent = DeliveryIntent.Reliable) =>
        Identity.ToMasterRPC(rpcCode, data, deliveryIntent);

    /// <summary>
    /// Serialization called by GSInstanceIdentity.
    /// </summary>
    /// <param name="writer"></param>
    void ISerializeable.Serialize(NetWriter writer)
    {
        var needSerialize = IsDirty;
        writer.Write(needSerialize);

        if (!needSerialize)
            return;

        SerializeDirtyChecked(writer);
        Data.Serialize(writer);

#if NETWORK_DEBUG
        writer.Write(GetType().FullName.GetHashCode());
#endif
    }

    /// <summary>
    /// Deserialization called by GSInstanceIdentity.
    /// </summary>
    /// <param name="reader"></param>
    void ISerializeable.Deserialize(NetReader reader, bool firtsTime)
    {
        //Check if sended object was dirty
        if (!reader.ReadBoolean())
            return;

        DeserializeDirtyChecked(reader, firtsTime);
        Data.Deserialize(reader, firtsTime);
        OnDataDeserialized(firtsTime);

#if NETWORK_DEBUG
        var typeName = GetType().FullName;
        var hash = typeName.GetHashCode();
        var serializedHash = reader.ReadInt32();

        if (hash != serializedHash)
            Debug.LogError($"Potential serialization problem in {typeName} on {gameObject.name}");
#endif
    }

    /// <summary>
    /// Destroy game object and sync it. Use this instead of Destroy(GameObject).
    /// You can't destroy remote objects.
    /// </summary>
    public void DestroyObjectViaNetwork() => Identity.DestroyViaNetwork();

    /// <summary>
    /// Consider to use <see cref="OnPostInitialize"/> instead.
    /// </summary>
    public virtual void OnPreInitialize() { }

    /// <summary>
    /// Object instantiated and initialized, use it instead of 'Awake'. Called by 'GSInstanceIdentity' after object deserialization.
    /// </summary>
    public abstract void OnPostInitialize();

    /// <summary>
    /// Called when object crated with additional data.
    /// </summary>
    /// <param name="data"></param>
    public virtual void OnCreatedWithAdditionalData(object[] data) { }

    /// <summary>
    /// Owner changed
    /// </summary>
    public virtual void OnOwnerChanged() { }

    /// <summary>
    /// Called, when object needs to be updated, according to <see cref="GSInstanceIdentity.m_UpdateRate"/>
    /// Data values can be setted here
    /// </summary>
    public virtual void OnUpdate() { }

    /// <summary>
    /// Other player disconnected
    /// </summary>
    /// <param name="peedId"></param>
    public virtual void OnDisconnected(ClientUID peedId) { }

    /// <summary>
    /// Serialize method without dirty check.
    /// </summary>
    /// <param name="writer"></param>
    [Obsolete("Use data based serialization instead")]
    protected abstract void SerializeDirtyChecked(NetWriter writer);

    /// <summary>
    /// Deserialize method without dirty check.
    /// </summary>
    /// <param name="reader"></param>
    [Obsolete("Use data based serialization instead")]
    protected abstract void DeserializeDirtyChecked(NetReader reader, bool firstTime);

    /// <summary>
    /// Called, when network data was deserialized;
    /// </summary>
    /// <param name="firstTime"></param>
    protected virtual void OnDataDeserialized(bool firstTime) { }
}
