using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Core.Networking;
using LROTech.Network.StateManagement;
using UnityEngine;

namespace LROTech.Network.GameObjects
{
    [DisallowMultipleComponent]
    public sealed class NetObjectIdentity : MonoBehaviour
    {
        public NetObjectUid InstanceId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => State?.Uid ?? NetObjectUid.Invalid;
        }

        public bool IsOwnedByMasterClient
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => State is { IsOwnedByMasterClient: true };
        }

        public bool IsLocal
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => State is { IsOwnedByLocalPlayer: true };
        }

        public bool IsSceneObject
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => State is { IsSceneObject: true };
        }

        public bool IsLocalPlayerMainTagObject
        {
            get
            {
                if (State == null || World == null)
                    return false;

                var localPlayer = World.Players.LocalPlayer;
                return localPlayer is { MainTagObjectUid: { IsValid: true } } && localPlayer.MainTagObjectUid.Equals(State.Uid);
            }
        }

        public NetWorldPlayer Owner
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => State?.Owner;
        }

        /// <summary>
        /// World that contains this object;
        /// </summary>
        public NetWorld World { get; private set; }

        public bool Initialized { get; private set; }

        private NetObjectBehaviourBase[] _behaviours;

        private NetWriter _objectWriter;
        private NetReader _objectReader;

        public NetObjectState State { get; private set; }

        private readonly Dictionary<Type, NetObjectBehaviourBase> _cachedBehaviours = new();
        private readonly Dictionary<int, Action<byte[]>> _rpcHandlers = new();

        /// <summary>
        /// Initialization. Called from GSInstanceFactory. 
        /// Shouldn't be called from top-level API.
        /// </summary>
        internal void Initialize(NetWorld world, NetObjectUid stateUid)
        {
            if (Initialized)
                throw new InvalidOperationException("Object already initialized");

            _behaviours = gameObject.GetComponents<NetObjectBehaviourBase>();

            World = world;
            State = world.GetObject(stateUid);
            if (State == null)
                throw new InvalidOperationException();

            State.OwnerChanged += OwnerChanged;
            State.LocalUpdateRequest += OnLocalUpdateRequest;
            State.Invalidated += OnStateInvalidated;

            var viewStateData = IsLocal ? State.DataCollection.GetOrCreateData<NetObjectViewStateData>() : State.DataCollection.GetData<NetObjectViewStateData>();

            // Pre initialization.
            foreach (var behaviour in _behaviours)
                behaviour.OnPreInitializeInternal();

            // First initial deserialization.
            if (!IsLocal)
                Deserialize(viewStateData.UnstructuredData, force: true); // Force true to allow deserialization before object initialized.

            // Register RPC handlers.
            foreach (var behaviour in _behaviours)
            {
                var behaviourType = behaviour.GetType();
                foreach (var methodInfo in NetObjectRpcUtils.GetAutoRegisterRpcHandlers(behaviourType))
                {
                    if (methodInfo.CreateDelegate(typeof(Action<byte[]>), behaviour) is Action<byte[]> action)
                        RegisterRpcHandler(action);
                    else
                        Debug.LogError($"Unable to create delegate for RPC handler '{methodInfo.Name}' on '{behaviourType.FullName}'.", gameObject);
                }
            }

            // Mark object as initialized.
            Initialized = true;

            // Owner changed.
            OwnerChanged();

            // Tell components about object was initialized.
            if (!viewStateData.ViewInitialized)
            {
                foreach (var behaviour in _behaviours)
                    behaviour.OnPostInitializeOnceInternal();
            }

            foreach (var behaviour in _behaviours)
                behaviour.OnPostInitializeInternal();

            // Invokes buffered rpc, must be called after deserialization done.
            // Only for remote objects.
            if (!IsLocal)
                State.RpcBuffer.ExecuteRemotely(executePersistent: true, ReceiveRpcRemotely);
        }

        /// <summary>
        /// Another client disconnected. This method calls before actual player reference removed, so here we have valid owner.
        /// </summary>
        internal void OnDisconnected(ClientUid clientUid)
        {
            foreach (var behaviour in _behaviours)
                behaviour.OnDisconnectedInternal(clientUid);
        }

        private void OnLocalUpdateRequest(NetObjectStateChangeRequestType stateChangeType)
        {
            if (!Initialized)
                throw new InvalidOperationException($"Object {gameObject.name} not initialized");

            if (!IsLocal)
                return;

            foreach (var behaviour in _behaviours)
                behaviour.OnAuthorityUpdateInternal();

            State.ChangeObjectViewDataLocally(Serialize());
        }

        /// <summary>
        /// Object owner was changed.
        /// </summary>
        private void OwnerChanged()
        {
            foreach (var behavior in _behaviours)
                behavior.OnOwnerChangedInternal();
        }

        /// <summary>
        /// Ownership management, anyone can change a scene object owner.
        /// </summary>
        public void OwnObject()
        {
            if (!Initialized)
                throw new InvalidOperationException("You can manage owners only for initialized objects");

            if (!IsSceneObject)
                throw new InvalidOperationException("You can manage owners only for scene objects");

            State.ChangedOwner(World.Players.LocalPlayer.Uid);
        }

        private byte[] Serialize()
        {
            // Serialize object.
            _objectWriter ??= new NetWriter();

            SerializeTo(_objectWriter);
            return _objectWriter.ToArray();
        }

        private void SerializeTo(NetWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.SeekZero();
            foreach (var behaviour in _behaviours)
                behaviour.OnSerializeInternal(writer);
        }

        /// <summary>
        /// Deserialize the whole object.
        /// </summary>
        internal void Deserialize(byte[] data, bool force)
        {
            if (IsLocal)
            {
                // TODO: Need to deserialize this data somehow, or change ownership changing pipeline.
                Debug.LogError($"Trying to deserialize local player object '{gameObject.name}' of '{State}', this is not allowed. Maybe owner was changed before. NOTE: This is normal, if owner was changed before.", gameObject);
                return;
            }

            if (!Initialized && !force)
            {
                Debug.LogWarning($"Trying to deserialize object '{gameObject.name}' that is not initialized, this is not allowed. Use 'force' option to do this.", gameObject);
                return;
            }

            if (data == null)
                return;

            _objectReader ??= new NetReader();
            _objectReader.Replace(data);

            foreach (var behaviour in _behaviours)
                behaviour.OnDeserializeInternal(_objectReader);

            // Receive rpc buffer if object initialized.
            if (Initialized)
                State.RpcBuffer.ExecuteRemotely(executePersistent: false, ReceiveRpcRemotely);
        }

        /// <summary>
        /// Register handler to receive RPC (Better to call in OnEnable or OnInit).
        /// </summary>
        /// <param name="handler"></param>
        public void RegisterRpcHandler(Action<byte[]> handler)
        {
            var rpcHash = NetObjectRpcUtils.ActionToHash(handler);
            // WE DON'T NEED TO ASSIGN MORE THAN 1 (ONE) RPC HANDLER FOR EACH RPC CODE.
            if (!_rpcHandlers.TryAdd(rpcHash, handler))
                throw new InvalidOperationException(
                    $"'{gameObject.name}' already contains handler for '{handler.Method.DeclaringType!.FullName}.{handler.Method.Name}''{rpcHash}' rpc code.");
        }

        /// <summary>
        /// RPC, invokes locally and sends to ALL clients, who HAS NO AUTHORITY on this object.
        /// </summary>
        public void OwnerToOthersRpc(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable, bool invokeLocally = true) =>
            OwnerToOthersRpc(rpcHandlerMethod, data, persistent: false, RpcBufferChangeMethod.Add, deliveryMethod, invokeLocally);


        private void OwnerToOthersRpc(Action<byte[]> rpcHandlerMethod, byte[] data, bool persistent, RpcBufferChangeMethod bufferChangeMethod, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable, bool invokeLocally = true)
        {
            if (!IsLocal)
                throw new InvalidOperationException(
                    "Trying to send AuthRPC on remote object's behaviour, this is not allowed. Use NonAuthRPC if you not sure about object's authority");

            var hashCode = NetObjectRpcUtils.ActionToHash(rpcHandlerMethod);

            // Try to invoke handler locally.
            if (invokeLocally)
                InvokeRpcHandler(hashCode, data);

            // Add to sending buffer.
            State.ScheduleRpc(hashCode, data, persistent, bufferChangeMethod, deliveryMethod);
        }

        /// <summary>
        /// RPC variation sends RPC to SINGLE client, who HAS AUTHORITY on this object.
        /// IF we have authority, just invokes locally without sending data.
        /// </summary>
        public void ToOwnerRpc(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable)
        {
            var hashCode = NetObjectRpcUtils.ActionToHash(rpcHandlerMethod);
            // If we have authority on this object, don't need to send this kind of RPC.
            if (IsLocal)
                InvokeRpcHandler(hashCode, data);
            else
                State.ScheduleRpcWithoutAuthority(NoAuthorityRpcType.ToOwner, hashCode, data, deliveryMethod);
        }

        /// <summary>
        /// RPC variation, sends RPC to MASTER client.
        /// It does not matter whether MASTER client is a owner of this object or not.
        /// IF we are a master client, just invokes locally without sending data.
        /// </summary>
        public void ToMasterRPC(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable)
        {
            var hashCode = NetObjectRpcUtils.ActionToHash(rpcHandlerMethod);
            // If we have authority on this object, don't need to send this kind of RPC.
            if (IsOwnedByMasterClient)
                InvokeRpcHandler(hashCode, data);
            else
                State.ScheduleRpcWithoutAuthority(NoAuthorityRpcType.ToMaster, hashCode, data, deliveryMethod);
        }

        /// <summary>
        /// RPC variation sends RPC to SINGLE client, who HAS AUTHORITY on this object.
        /// IF we have authority, just invoke it locally without sending data.
        /// </summary>
        public void ToAllRpc(Action<byte[]> rpcHandlerMethod, byte[] data, bool invokeLocally, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable)
        {
            var hashCode = NetObjectRpcUtils.ActionToHash(rpcHandlerMethod);
            if (invokeLocally)
                InvokeRpcHandler(hashCode, data);

            State.ScheduleRpcWithoutAuthority(NoAuthorityRpcType.ToAll, hashCode, data, deliveryMethod);
        }

        /// <summary>
        /// Remove RPC from the buffer.
        /// </summary>
        /// <param name="rpcHandlerMethod"></param>
        public void RemoveRpcFromBuffer(Action<byte[]> rpcHandlerMethod) =>
            State.ScheduleRemovePersistentRpc(NetObjectRpcUtils.ActionToHash(rpcHandlerMethod));

        /// <summary>
        /// Receive rpc on object WITHOUT authority.
        /// </summary>
        private void ReceiveRpcRemotely(int hashCode, byte[] data)
        {
            if (IsLocal)
                throw new InvalidOperationException("Trying to receive NO-AUTHORITY RPC on local object behaviour, this is not allowed.");

            InvokeRpcHandler(hashCode, data);
        }

        /// <summary>
        /// Receive rpc on the object with authority.
        /// </summary>
        internal void ReceiveRpcLocally(RpcWithoutAuthorityState state)
        {
            if (!IsLocal)
                throw new InvalidOperationException("Trying to receive AUTHORITY RPC on remote object behaviour, this is not allowed");

            InvokeRpcHandler(state.RpcState.Hash, state.RpcState.Payload);
        }

        /// <summary>
        /// Receive rpc on a master client. (Called from GSTransport).
        /// </summary>
        internal void ReceiveRpcOnMasterClient(RpcWithoutAuthorityState state)
        {
            if (!IsOwnedByMasterClient)
                throw new InvalidOperationException("Trying to receive MASTER CLIENT RPC on object behaviour, which is not owner by master client, this is not allowed");

            InvokeRpcHandler(state.RpcState.Hash, state.RpcState.Payload);
        }

        /// <summary>
        /// Receive prc anywhere.
        /// </summary>
        /// <param name="state"></param>
        internal void ReceiveRpc(RpcWithoutAuthorityState state) =>
            InvokeRpcHandler(state.RpcState.Hash, state.RpcState.Payload);

        private void InvokeRpcHandler(int hashCode, byte[] data)
        {
            if (_rpcHandlers.TryGetValue(hashCode, out var handler))
                handler.Invoke(data);
            else
            {
                var errorMessage = $"Handler for RPC is not registered on object '{gameObject.name}'.";

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var handlerInfoString = $"Hash: '{hashCode}'";
                var handlerMethodInfo = NetObjectRpcUtils.TryFindMethodInfoByHashSlow(hashCode);
                handlerInfoString = handlerMethodInfo != null
                    ? $"{handlerInfoString} Method: '{handlerMethodInfo.DeclaringType!.FullName}.{handlerMethodInfo.Name}'"
                    : $"{handlerInfoString} Method: 'UNKNOWN'";

                errorMessage += $" Handler info: {handlerInfoString}.";
#endif

                Debug.LogError(errorMessage, gameObject);
            }
        }

        /// <summary>
        /// Destroy the object (should be used instead of GameObject.Destroy).
        /// </summary>
        /// <param name="considerChildren">If true, all potential child network entities will be unparented and deleted.</param>
        public void DestroyViaNetwork(bool considerChildren = false)
        {
            if (!IsLocal)
                throw new InvalidOperationException("Trying to destroy remote object, this is not allowed");

            if (considerChildren)
            {
                foreach (var identity in GetComponentsInChildren<NetObjectIdentity>(includeInactive: true))
                {
                    // This will correctly unparent the object.
                    World.View.UpdateTransformInView(identity.transform);

                    // Schedule update to remove an object from the world.
                    State.ScheduleUpdate(NetObjectStateChangeRequestType.ReliableRemoveUpdateSoft);
                }
            }
            else
                State.ScheduleUpdate(NetObjectStateChangeRequestType.ReliableRemoveUpdateSoft);
        }

        internal void OnSoftDestroyRequestedLocallyInternal()
        {
            if (!IsLocal)
                throw new InvalidOperationException("No authority");

            foreach (var behaviour in _behaviours)
                behaviour.OnSoftDestroyRequestedLocallyInternal();
        }

        /// <summary>
        /// Could be used instead of <see cref="UnityEngine.Behaviour.GetComponent{T}"/>
        /// </summary>
        public T GetBehaviour<T>() where T : NetObjectBehaviourBase
        {
            var type = typeof(T);
            if (!_cachedBehaviours.TryGetValue(type, out var behaviour))
            {
                behaviour = GetComponent<T>();
                if (behaviour != null)
                    _cachedBehaviours.Add(type, behaviour);
            }

            return behaviour as T;
        }

        private void OnDestroy()
        {
            if (State == null)
                return;

            State.OwnerChanged -= OwnerChanged;
            State.LocalUpdateRequest -= OnLocalUpdateRequest;
            State.Invalidated -= OnStateInvalidated;
            State = null;
        }

        private void OnStateInvalidated()
        {
            State.Invalidated -= OnStateInvalidated;
            State = null;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!Initialized)
                return;

            UnityEditor.Handles.Label(
                transform.position,
                text: $"Object: {InstanceId}, Owner: {(Owner == null ? "NONE" : Owner.Name)}, Local: {IsLocal}");
        }
#endif
    }
}