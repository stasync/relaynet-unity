using System.Runtime.CompilerServices;
using Core.Networking;
using UnityEngine;

namespace LROTech.Network.GameObjects
{
    [RequireComponent(typeof(NetObjectIdentity))]
    public abstract class NetObjectBehaviourBase : MonoBehaviour
    {
        public NetObjectIdentity Identity =>
            _identity != null ? _identity : _identity = GetComponent<NetObjectIdentity>();

        private NetObjectIdentity _identity;

        internal virtual void OnPreInitializeInternal()
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnPostInitializeInternal() =>
            OnPostInitialize();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnInitializeViewDataInternal() =>
            OnInitializeViewData();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnOwnerChangedInternal() =>
            OnOwnerChanged();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnAuthorityUpdateInternal() =>
            OnAuthorityUpdate();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnDisconnectedInternal(ClientUid clientUid) =>
            OnDisconnected(clientUid);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnSerializeInternal(NetWriter writer)
        {
            OnSerialize(writer);

#if NETWORK_DEBUG
            writer.WriteInt32(GetType().FullName!.GetHashCode());
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnDeserializeInternal(NetReader reader)
        {
            OnDeserialize(reader);

#if NETWORK_DEBUG
            var typeName = GetType().FullName;
            var hash = typeName!.GetHashCode();
            var serializedHash = reader.ReadInt32();

            if (!hash.Equals(serializedHash))
                Debug.LogError($"Potential serialization problem in '{typeName}' on '{gameObject.name}'.");
#endif

            OnDataDeserialized();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnSoftDestroyRequestedLocallyInternal() =>
            OnSoftDestroyRequestedLocally();

        /// <summary>
        /// Use this instead of start, called after objet was deserialized first time.
        /// </summary>
        protected virtual void OnPostInitialize()
        {
        }

        protected virtual void OnInitializeViewData()
        {
        }

        /// <summary>
        /// Calls when object's owner was changed locally or remotely.
        /// </summary>
        protected virtual void OnOwnerChanged()
        {
        }

        /// <summary>
        /// Called, when object needs to be updated.
        /// </summary>
        protected virtual void OnAuthorityUpdate()
        {
        }

        /// <summary>
        /// Other client disconnected.
        /// </summary>
        protected virtual void OnDisconnected(ClientUid clientUid)
        {
        }

        /// <summary>
        /// Destroy method called locally.
        /// </summary>
        protected virtual void OnSoftDestroyRequestedLocally()
        {
        }

        protected virtual void OnSerialize(NetWriter writer)
        {
        }

        protected virtual void OnDeserialize(NetReader reader)
        {
        }

        /// <summary>
        /// Called, when network data was deserialized.
        /// </summary>
        protected virtual void OnDataDeserialized()
        {
        }

        protected virtual void OnDestroy()
        {
        }
    }
}