using Core.Networking;
using System;
using UnityEngine;

[Obsolete("Use data based behaviour instead")]
public abstract class GSInstanceBehaviour : GSInstanceBehaviour<NetObjectStateEmptyData>
{
}

public abstract class GSInstanceBehaviour<TObjectData> : GSInstanceBehaviourBase, IGSInstanceBehaviour where TObjectData : NetObjectStateData
{
    protected TObjectData Data { get; private set; }

    public virtual bool DataWritePermission => IsLocal;

    public NetWorldPlayer Owner => Identity.Owner;

    public bool IsLocal => Identity.IsLocal;

    public bool IsLocalPlayerTagObject => Identity.IsLocalPlayerMainTagObject;

    public bool IsSceneObject => Identity.IsSceneObject;

    public bool IsOwnedByMasterClient => Identity.IsOwnedByMasterClient;

    /// <summary>
    /// RPC, invokes locally and sends to ALL clients, who HAS NOT AUTHORITY on this object.
    /// </summary>
    public void OwnerToOthersRPC(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryIntent deliveryIntent = DeliveryIntent.Reliable, bool invokeLocally = true) =>
        Identity.OwnerToOthersRPC(rpcHandlerMethod, data, deliveryIntent, invokeLocally);

    /// <summary>
    /// Same as OwnerToOthersRPC, but buffered.
    /// </summary>
    public void OwnerToOthersRPCBuffered(Action<byte[]> rpcHandlerMethod, byte[] data, RPCBufferChangeMethod bufferChangeMethod = RPCBufferChangeMethod.Replace) =>
        Identity.OwnerToOthersRPCBuffered(rpcHandlerMethod, data, bufferChangeMethod);

    /// <summary>
    /// RPC variation, sends RPC to SINGLE client, who HAS AUTHORITY on this object.
    /// IF we have authority, just invokes locally without sending data.
    /// </summary>
    public void ToOwnerRPC(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryIntent deliveryIntent = DeliveryIntent.Reliable) =>
        Identity.ToOwnerRPC(rpcHandlerMethod, data, deliveryIntent);

    /// <summary>
    /// Send RPC to all clients with this object;
    /// </summary>
    public void ToAllRPC(Action<byte[]> rpcHandlerMethod, byte[] data, bool invokeLocaly, DeliveryIntent deliveryIntent = DeliveryIntent.Reliable) =>
        Identity.ToAllRPC(rpcHandlerMethod, data, invokeLocaly, deliveryIntent);

    /// <summary>
    /// Serialization called by GSInstanceIdentity.
    /// </summary>
    void ISerializable.Serialize(NetWriter writer)
    {
        SerializeDirtyChecked(writer);

#if NETWORK_DEBUG
        writer.Write(GetType().FullName!.GetHashCode());
#endif
    }

    /// <summary>
    /// Deserialization called by GSInstanceIdentity.
    /// </summary>
    void ISerializable.Deserialize(NetReader reader, bool firtsTime)
    {
        DeserializeDirtyChecked(reader, firtsTime);

        OnDataDeserialized(firtsTime);

#if NETWORK_DEBUG
        var typeName = GetType().FullName;
        var hash = typeName!.GetHashCode();
        var serializedHash = reader.ReadInt32();

        if (!hash.Equals(serializedHash))
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
    public virtual void OnPreInitialize()
    {
        Data = Identity
            .State
            .DataCollection
            .GetOrCreateData<TObjectData>();
    }

    /// <summary>
    /// Object instantiated and initialized, use it instead of 'Awake'. Called by 'GSInstanceIdentity' after object deserialization.
    /// </summary>
    public virtual void OnPostInitialize() { }
    public virtual void OnInitializeViewData() { }

    /// <summary>
    /// Owner changed
    /// </summary>
    public virtual void OnOwnerChanged() { }

    /// <summary>
    /// Called, when object needs to be updated, according to <see cref="NetObjectIdentity.m_UpdateRate"/>
    /// Data values can be setted here
    /// </summary>
    public virtual void OnAuthorityUpdate() { }

    /// <summary>
    /// Other player disconnected
    /// </summary>
    /// <param name="peedId"></param>
    public virtual void OnDisconnected(ClientUID peedId) { }

    /// <summary>
    /// Destroy method called localy
    /// </summary>
    public virtual void OnSoftDestroyRequestedLocally() { }

    /// <summary>
    /// Serialize method without dirty check.
    /// </summary>
    [Obsolete("Use data based serialization instead")]
    protected virtual void SerializeDirtyChecked(NetWriter writer) { }

    /// <summary>
    /// Deserialize method without dirty check.
    /// </summary>
    [Obsolete("Use data based serialization instead")]
    protected virtual void DeserializeDirtyChecked(NetReader reader, bool firstTime) { }

    /// <summary>
    /// Called, when network data was deserialized;
    /// </summary>
    protected virtual void OnDataDeserialized(bool firstTime) { }

    [Obsolete]
    public string ToJSONProperty() => string.Empty;
}
