using System.Runtime.CompilerServices;
using Keel.Networking;
using UnityEngine;

namespace Keel.RelayNet.GameObjects
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
        internal void OnPostInitializeOnceInternal() =>
            OnPostInitializeOnce();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnPostInitializeInternal() =>
            OnPostInitialize();

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
            var type = GetType();
            Keel.Utils.Types.TryGetTypeStableHashCode(type, out var hashCode);
            writer.WriteInt32(hashCode);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnDeserializeInternal(NetReader reader)
        {
            OnDeserialize(reader);

#if NETWORK_DEBUG
            var type = GetType();
            Keel.Utils.Types.TryGetTypeStableHashCode(type, out var hashCode);
            var serializedHash = reader.ReadInt32();

            if (!hashCode.Equals(serializedHash))
                Debug.LogError($"Potential serialization problem in '{type.FullName}' on '{gameObject.name}'.");
#endif

            OnDataDeserialized();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnSoftDestroyRequestedLocallyInternal() =>
            OnSoftDestroyRequestedLocally();

        /// <summary>
        /// Used instead of start, but will be only called when the object was created for first time.
        /// See <see cref="Keel.RelayNet.StateManagement.NetObjectViewStateData.ViewInitialized"/> flag.
        /// </summary>
        protected virtual void OnPostInitializeOnce()
        {
        }

        /// <summary>
        /// Use this instead of start, called after objet was deserialized first time.
        /// </summary>
        protected virtual void OnPostInitialize()
        {
        }

        /// <summary>
        /// Calls when the object's owner was changed locally or remotely.
        /// </summary>
        protected virtual void OnOwnerChanged()
        {
        }

        /// <summary>
        /// Called, when the object needs to be updated.
        /// </summary>
        protected virtual void OnAuthorityUpdate()
        {
        }

        /// <summary>
        /// Another client disconnected.
        /// </summary>
        protected virtual void OnDisconnected(ClientUid clientUid)
        {
        }

        /// <summary>
        /// Destroy callback called locally.
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