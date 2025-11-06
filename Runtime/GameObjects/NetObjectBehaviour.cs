using System;
using LROTech.Network.StateManagement;

namespace LROTech.Network.GameObjects
{
    public abstract class NetObjectBehaviour<TObjectData> : NetObjectBehaviourBase where TObjectData : NetObjectStateData, new()
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
        public void OwnerToOthersRpc(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable, bool invokeLocally = true) =>
            Identity.OwnerToOthersRpc(rpcHandlerMethod, data, deliveryMethod, invokeLocally);

        /// <summary>
        /// Same as OwnerToOthersRPC, but buffered.
        /// </summary>
        public void OwnerToOthersRpcBuffered(Action<byte[]> rpcHandlerMethod, byte[] data, RpcBufferChangeMethod bufferChangeMethod = RpcBufferChangeMethod.Replace) =>
            Identity.OwnerToOthersRpcBuffered(rpcHandlerMethod, data, bufferChangeMethod);

        /// <summary>
        /// RPC variation, sends RPC to SINGLE client, who HAS AUTHORITY on this object.
        /// IF we have authority, just invokes locally without sending data.
        /// </summary>
        public void ToOwnerRpc(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable) =>
            Identity.ToOwnerRpc(rpcHandlerMethod, data, deliveryMethod);

        /// <summary>
        /// Send RPC to all clients with this object.
        /// </summary>
        public void ToAllRpc(Action<byte[]> rpcHandlerMethod, byte[] data, bool invokeLocally, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable) =>
            Identity.ToAllRpc(rpcHandlerMethod, data, invokeLocally, deliveryMethod);

        /// <summary>
        /// Destroy game object and sync it. Use this instead of Destroy(GameObject).
        /// You can't destroy remote objects.
        /// </summary>
        public void DestroyObjectViaNetwork() =>
            Identity.DestroyViaNetwork();

        /// <summary>
        /// Consider to use <see cref="NetObjectBehaviourBase.OnPostInitialize"/> instead.
        /// </summary>
        protected override void OnPreInitialize()
        {
            base.OnPreInitialize();

            var dataCollection = Identity.State.DataCollection;
            Data = IsLocal ? dataCollection.GetOrCreateData<TObjectData>() : dataCollection.GetData<TObjectData>();
        }
    }
}