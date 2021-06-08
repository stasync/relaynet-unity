using Core.Networking;
using System;
using UnityEngine;

public abstract class GSInstanceBehaviour<TBehaviourData> : GSInstanceBehaviour<TBehaviourData, NetObjectStateEmptyData>
     where TBehaviourData : GSBehaviourAbstractData, new()
{ }

public abstract class GSInstanceBehaviour<TBehaviourData, TObjectData> : GSInstanceBehaviourBase, IGSInstanceBehaviour 
    where TBehaviourData : GSBehaviourAbstractData, new()
    where TObjectData : NetObjectStateData
{
    TBehaviourData m_BehaviourData;
    protected TBehaviourData BehavourData
    {
        get
        {
            if (m_BehaviourData == null)
            {
                m_BehaviourData = new TBehaviourData();
                BehavourData.Initialize(() => DataWritePermission, OnDataValuesCollected);
            }
            return m_BehaviourData;
        }
    }

    protected TObjectData Data { get; private set; }

    public virtual int SerializationOrder => 0;

    public virtual bool DataWritePermission => IsLocal;

    public NetWorldPlayer Owner => Identity.Owner;

    public bool IsLocal => Identity.IsLocal;

    public bool IsLocalPlayerTagObject => Identity.IsLocalPlayerMainTagObject;

    public bool IsSceneObject => Identity.IsSceneObject;

    public bool IsOwnedByMasterClient => Identity.IsOwnedByMasterClient;

    protected virtual void OnDataValuesCollected() { }

    /// <summary>
    /// RPC, invokes locally and sends to ALL clients, who HAS NOT AUTHORITY on this object.
    /// </summary>
    /// <param name="rpcCode"></param>
    /// <param name="data"></param>
    public void OwnerToOthersRPC(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryIntent deliveryIntent = DeliveryIntent.Reliable, bool invokeLocally = true) =>
        Identity.OwnerToOthersRPC(rpcHandlerMethod, data, deliveryIntent, invokeLocally);

    /// <summary>
    /// Same as OwnerToOthersRPC, but buffered.
    /// </summary>
    /// <param name="rpcCode"></param>
    /// <param name="data"></param>
    public void OwnerToOthersRPCBuffered(Action<byte[]> rpcHandlerMethod, byte[] data, RPCBufferChangeMethod bufferChangeMethod = RPCBufferChangeMethod.Replace) =>
        Identity.OwnerToOthersRPCBuffered(rpcHandlerMethod, data, bufferChangeMethod);

    /// <summary>
    /// RPC variation, sends RPC to SINGLE client, who HAS AUTHORITY on this object.
    /// IF we have authority, just invokes locally without sending data.
    /// </summary>
    /// <param name="rpcCode"></param>
    /// <param name="data"></param>
    public void ToOwnerRPC(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryIntent deliveryIntent = DeliveryIntent.Reliable) =>
        Identity.ToOwnerRPC(rpcHandlerMethod, data, deliveryIntent);

    /// <summary>
    /// RPC variation, sends RPC to MASTER client.
    /// It does not matter whether MASTER client is a owner of this object or not.
    /// </summary>
    /// <param name="rpcCode"></param>
    /// <param name=""></param>
    public void MasterRPC(string rpcCode, byte[] data, DeliveryIntent deliveryIntent = DeliveryIntent.Reliable) =>
        Identity.ToMasterRPC(rpcCode, data, deliveryIntent);

    /// <summary>
    /// Send RPC to all clients with this object;
    /// </summary>
    /// <param name="rpcHandlerMethod"></param>
    /// <param name="data"></param>
    /// <param name="invokeLocaly"></param>
    /// <param name="deliveryIntent"></param>
    public void ToAllRPC(Action<byte[]> rpcHandlerMethod, byte[] data, bool invokeLocaly, DeliveryIntent deliveryIntent = DeliveryIntent.Reliable) =>
        Identity.ToAllRPC(rpcHandlerMethod, data, invokeLocaly, deliveryIntent);

    /// <summary>
    /// Serialization called by GSInstanceIdentity.
    /// </summary>
    /// <param name="writer"></param>
    void ISerializable.Serialize(NetWriter writer)
    {
        SerializeDirtyChecked(writer);

        BehavourData.Serialize(writer);

#if NETWORK_DEBUG
        writer.Write(GetType().FullName.GetHashCode());
#endif
    }

    /// <summary>
    /// Deserialization called by GSInstanceIdentity.
    /// </summary>
    /// <param name="reader"></param>
    void ISerializable.Deserialize(NetReader reader, bool firtsTime)
    {
        DeserializeDirtyChecked(reader, firtsTime);

        BehavourData.Deserialize(reader, firtsTime);
        OnDataDeserialized(firtsTime);

#if NETWORK_DEBUG
        var typeName = GetType().FullName;
        var hash = typeName.GetHashCode();
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
    public abstract void OnPostInitialize();
    public virtual void OnInitializeViewData() { }

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
    public virtual void OnSoftDestroyRequestedLocaly() { }

    /// <summary>
    /// Serialize method without dirty check.
    /// </summary>
    /// <param name="writer"></param>
    [Obsolete("Use data based serialization instead")]
    protected virtual void SerializeDirtyChecked(NetWriter writer) { }

    /// <summary>
    /// Deserialize method without dirty check.
    /// </summary>
    /// <param name="reader"></param>
    [Obsolete("Use data based serialization instead")]
    protected virtual void DeserializeDirtyChecked(NetReader reader, bool firstTime) { }

    /// <summary>
    /// Called, when network data was deserialized;
    /// </summary>
    /// <param name="firstTime"></param>
    protected virtual void OnDataDeserialized(bool firstTime) { }

    public string ToJSONProperty() => 
        "{" + RealtimeJSONUtils.ToJSONValue($"{GetType().Name} {typeof(TBehaviourData).Name}") + ":" + BehavourData.ToJSONProperty() + "}";
}
