using System;
using LROTech.Network.StateManagment;

public abstract class GSInstanceBehaviour<TObjectData> : GSInstanceBehaviourBase, IGSInstanceBehaviour
    where TObjectData : NetObjectStateData
{
    protected TObjectData Data { get; private set; }

    public virtual bool DataWritePermission => IsLocal;

    public NetWorldPlayer Owner => Identity.Owner;

    public bool IsLocal => Identity.IsLocal;

    public bool IsLocalPlayerTagObject => Identity.IsLocalPlayerMainTagObject;

    public bool IsSceneObject => Identity.IsSceneObject;

    public bool IsOwnedByMasterClient => Identity.IsOwnedByMasterClient;

    /// <summary>
    /// RPC, invokes locally and sends to ALL clients, who HAS NO AUTHORITY on this object.
    /// </summary>
    public void OwnerToOthersRPC(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable, bool invokeLocally = true) =>
        Identity.OwnerToOthersRPC(rpcHandlerMethod, data, deliveryMethod, invokeLocally);

    /// <summary>
    /// Same as OwnerToOthersRPC, but buffered.
    /// </summary>
    public void OwnerToOthersRPCBuffered(Action<byte[]> rpcHandlerMethod, byte[] data, RPCBufferChangeMethod bufferChangeMethod = RPCBufferChangeMethod.Replace) =>
        Identity.OwnerToOthersRPCBuffered(rpcHandlerMethod, data, bufferChangeMethod);

    /// <summary>
    /// RPC variation, sends RPC to SINGLE client, who HAS AUTHORITY on this object.
    /// IF we have authority, just invokes locally without sending data.
    /// </summary>
    public void ToOwnerRPC(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable) =>
        Identity.ToOwnerRPC(rpcHandlerMethod, data, deliveryMethod);

    /// <summary>
    /// Send RPC to all clients with this object.
    /// </summary>
    public void ToAllRPC(Action<byte[]> rpcHandlerMethod, byte[] data, bool invokeLocally, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable) =>
        Identity.ToAllRPC(rpcHandlerMethod, data, invokeLocally, deliveryMethod);

    /// <summary>
    /// Destroy game object and sync it. Use this instead of Destroy(GameObject).
    /// You can't destroy remote objects.
    /// </summary>
    public void DestroyObjectViaNetwork() => Identity.DestroyViaNetwork();

    /// <summary>
    /// Consider to use <see cref="GSInstanceBehaviourBase.OnPostInitialize"/> instead.
    /// </summary>
    protected override void OnPreInitialize()
    {
        base.OnPreInitialize();

        Data = Identity
            .State
            .DataCollection
            .GetOrCreateData<TObjectData>();
    }
}
