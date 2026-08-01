using System;
using System.Runtime.CompilerServices;
using LROTech.Network.StateManagement;

namespace LROTech.Network.GameObjects
{
    public abstract class NetObjectBehaviour<TObjectData> : NetObjectBehaviourBase where TObjectData : NetObjectStateData, new()
    {
        public TObjectData Data
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            private set;
        }

        public NetWorldPlayer Owner
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Identity.Owner;
        }

        public bool IsLocal
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Identity.IsLocal;
        }

        /// <summary>
        /// RPC, invokes locally and sends to ALL clients who HAVE NO AUTHORITY on this object.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OwnerToOthersRpc(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryMethod deliveryMethod, bool invokeLocally = true) =>
            Identity.OwnerToOthersRpc(rpcHandlerMethod, data, deliveryMethod, invokeLocally);

        /// <summary>
        /// RPC variation sends RPC to SINGLE client, who HAS AUTHORITY on this object.
        /// IF we have authority, just invoke it locally without sending data.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ToOwnerRpc(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryMethod deliveryMethod) =>
            Identity.ToOwnerRpc(rpcHandlerMethod, data, deliveryMethod);

        /// <summary>
        /// Send RPC to all clients with this object.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ToAllRpc(Action<byte[]> rpcHandlerMethod, byte[] data, bool invokeLocally, DeliveryMethod deliveryMethod) =>
            Identity.ToAllRpc(rpcHandlerMethod, data, invokeLocally, deliveryMethod);

        /// <summary>
        /// Destroy the game object and sync it. Use this instead of Destroy(GameObject).
        /// You can't destroy remote objects.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DestroyObjectViaNetwork() =>
            Identity.DestroyViaNetwork();

        internal sealed override void OnPreInitializeInternal()
        {
            base.OnPreInitializeInternal();

            var dataCollection = Identity.State.DataCollection;
            Data = IsLocal ? dataCollection.GetOrCreateData<TObjectData>() : dataCollection.GetData<TObjectData>();
        }
    }
}