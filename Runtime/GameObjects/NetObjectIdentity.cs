using System;
using System.Collections.Generic;
using Core.Networking;
using LROTech.Network.Sessions;
using LROTech.Network.StateManagement;
using UnityEngine;

namespace LROTech.Network.GameObjects
{
    [DisallowMultipleComponent]
    public sealed class NetObjectIdentity : MonoBehaviour
    {
        public NetObjectUid InstanceId => State?.Uid ?? NetObjectUid.Invalid;
        public bool IsOwnedByMasterClient => State is { IsOwnedByMasterClient: true };
        public bool IsLocal => State is { IsOwnedByLocalPlayer: true };

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

        public NetWorldPlayer Owner => State?.Owner;

        /// <summary>
        /// World, that contains this object;
        /// </summary>
        public NetWorld World { get; private set; }

        /// <summary>
        /// Object prefab id
        /// </summary>
        public bool IsSceneObject => State is { IsSceneObject: true };

        public bool Initialized { get; private set; }

        private NetObjectBehaviourBase[] m_Behaviours;

        private NetWriter m_ObjectWriter;
        private NetReader m_ObjectReader;

        public NetObjectState State => m_NetObjectStateReference.State;
        private readonly NetObjectStateReference m_NetObjectStateReference = new();

        private readonly Dictionary<Type, NetObjectBehaviourBase> m_CachedBehaviours = new();
        private readonly Dictionary<int, Action<byte[]>> m_RpcHandlers = new();

        /// <summary>
        /// Initialization. Called from GSInstanceFactory. 
        /// Shouldn't be called from top-level API.
        /// </summary>
        internal void Initialize(NetWorld world, NetObjectUid stateUID)
        {
            if (Initialized)
                throw new Exception("Object already initialized");

            m_Behaviours = gameObject.GetComponents<NetObjectBehaviourBase>();

            World = world;

            m_NetObjectStateReference.SetTargetUid(stateUID);
            m_NetObjectStateReference.SetTargetWorld(world);

            State.OnOwnerChanged += OnOwnerChanged;
            State.LocalUpdateRequest += OnLocalUpdateRequest;

            var viewStateData = State.DataCollection.GetOrCreateData<NetObjectViewStateData>();
            if (!IsLocal)
                Deserialize(viewStateData.UnstructuredData, force: true); // Force true to allow deserialization before object initialized.

            // Pre initialization.
            foreach (var behaviour in m_Behaviours)
                behaviour.OnPreInitializeInternal();

            // Mark object as initialized.
            Initialized = true;

            // Owner changed.
            OnOwnerChanged();

            // Register RPC handlers.
            foreach (var behaviour in m_Behaviours)
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

            // Tell components about object was initialized.
            foreach (var behaviour in m_Behaviours)
            {
                if (!viewStateData.ViewInitialized)
                    behaviour.OnInitializeViewDataInternal();
            }

            foreach (var behaviour in m_Behaviours)
                behaviour.OnPostInitializeInternal();

            // Invokes buffered rpc, must be called after deserialization done.
            // Only for remote objects.
            if (!IsLocal)
                State.RpcBuffer.ExecuteRemotely(executePersistent: true, ReceiveRpcRemotely);
        } 

        /// <summary>
        /// Other client disconnected. This method calls before actual player reference removed, so here we have valid owner.
        /// </summary>
        internal void OnDisconnected(ClientUid clientUid)
        {
            foreach (var behaviour in m_Behaviours)
                behaviour.OnDisconnectedInternal(clientUid);
        }

        private void OnLocalUpdateRequest(NetObjectStateChangeRequestType stateChangeType)
        {
            if (!Initialized)
                throw new InvalidOperationException($"Object {gameObject.name} not initialized");

            if (!IsLocal)
                return;

            foreach (var behaviour in m_Behaviours)
                behaviour.OnAuthorityUpdateInternal();

            State.ChangeObjectViewDataLocally(Serialize(), stateChangeType == NetObjectStateChangeRequestType.InitializationUpdate);
        }

        /// <summary>
        /// Object owner was changed.
        /// </summary>
        private void OnOwnerChanged()
        {
            Debug.LogWarningFormat($"{gameObject.name} object with ID: {InstanceId} owner changed, now owner is {Owner}");
            foreach (var behavior in m_Behaviours)
                behavior.OnOwnerChangedInternal();
        }

        /// <summary>
        /// Ownership management, anyone can change scene object owner.
        /// </summary>
        public void OwnObject()
        {
            if (!Initialized)
                throw new Exception("You can manage owners only for initialized objects");

            if (!IsSceneObject)
                throw new Exception("You can manage owners only for scene objects");

            State.ChangedOwner(World.Players.LocalPlayerUid);
        }

        private byte[] Serialize()
        {
            // Serialize object.
            m_ObjectWriter ??= new NetWriter();

            SerializeTo(m_ObjectWriter);
            return m_ObjectWriter.ToArray();
        }

        private void SerializeTo(NetWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.SeekZero();
            foreach (var behaviour in m_Behaviours)
                behaviour.OnSerializeInternal(writer);
        }

        /// <summary>
        /// Deserialize whole object.
        /// </summary>
        internal void Deserialize(byte[] data, bool force)
        {
            if (IsLocal)
            {
                // TODO: Need to deserialize this data somehow, or change ownership changing pipeline.
                Debug.LogError(
                    $"Trying to deserialize local player object '{gameObject.name}', this is not allowed. Maybe owner was changed before. NOTE: This is normal, if owner was changed before.",
                    gameObject);
                return;
            }

            if (!Initialized && !force)
            {
                Debug.LogWarning(
                    $"Trying to deserialize object '{gameObject.name}' that is not initialized, this is not allowed. Use 'force' option to do this.",
                    gameObject);
                return;
            }

            if (data == null)
                return;

            m_ObjectReader ??= new NetReader();
            m_ObjectReader.Replace(data);

            foreach (var behaviour in m_Behaviours)
                behaviour.OnDeserializeInternal(m_ObjectReader);

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
            if (!m_RpcHandlers.TryAdd(rpcHash, handler))
                throw new InvalidOperationException(
                    $"'{gameObject.name}' already contains handler for '{handler.Method.DeclaringType!.FullName}.{handler.Method.Name}''{rpcHash}' rpc code.");
        }

        /// <summary>
        /// RPC, invokes locally and sends to ALL clients, who HAS NO AUTHORITY on this object.
        /// </summary>
        public void OwnerToOthersRpc(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable,
            bool invokeLocally = true) =>
            OwnerToOthersRpc(rpcHandlerMethod, data, persistent: false, RpcBufferChangeMethod.Add, deliveryMethod, invokeLocally);

        /// <summary>
        /// Same as AuthRPC, but buffered.
        /// Only works via RELIABLE delivery.
        /// </summary>
        public void OwnerToOthersRpcBuffered(Action<byte[]> rpcHandlerMethod, byte[] data,
            RpcBufferChangeMethod bufferChangeMethod = RpcBufferChangeMethod.Replace, bool invokeLocally = true) =>
            OwnerToOthersRpc(rpcHandlerMethod, data, persistent: true, bufferChangeMethod, DeliveryMethod.Reliable, invokeLocally);

        private void OwnerToOthersRpc(Action<byte[]> rpcHandlerMethod, byte[] data, bool persistent, RpcBufferChangeMethod bufferChangeMethod,
            DeliveryMethod deliveryMethod = DeliveryMethod.Reliable, bool invokeLocally = true)
        {
            if (!IsLocal)
                throw new Exception(
                    "Trying to send AuthRPC on remote object's behaviour, this is not allowed. Use NonAuthRPC if you not sure about object's authority");

            var hashCode = NetObjectRpcUtils.ActionToHash(rpcHandlerMethod);

            // Try to invoke handler locally.
            if (invokeLocally)
                InvokeRpcHandler(hashCode, data);

            // Add to sending buffer.
            State.ScheduleRpc(hashCode, data, persistent, bufferChangeMethod, deliveryMethod);
        }

        /// <summary>
        /// RPC variation, sends RPC to SINGLE client, who HAS AUTHORITY on this object.
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
        /// IF we are master client, just invokes locally without sending data.
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
        /// RPC variation, sends RPC to SINGLE client, who HAS AUTHORITY on this object.
        /// IF we have authority, just invokes locally without sending data.
        /// </summary>
        public void ToAllRpc(Action<byte[]> rpcHandlerMethod, byte[] data, bool invokeLocally, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable)
        {
            var hashCode = NetObjectRpcUtils.ActionToHash(rpcHandlerMethod);
            if (invokeLocally)
                InvokeRpcHandler(hashCode, data);

            State.ScheduleRpcWithoutAuthority(NoAuthorityRpcType.ToAll, hashCode, data, deliveryMethod);
        }

        /// <summary>
        /// Remove RPC from buffer
        /// </summary>
        /// <param name="rpcHandlerMethod"></param>
        public void RemoveRpcFromBuffer(Action<byte[]> rpcHandlerMethod) =>
            State.ScheduleRemovePersistentRpc(NetObjectRpcUtils.ActionToHash(rpcHandlerMethod));

        /// <summary>
        /// Receive rpc on object WITHOUT authority..
        /// </summary>
        private void ReceiveRpcRemotely(int hashCode, byte[] data)
        {
            if (IsLocal)
                throw new Exception("Trying to receive NO-AUTHORITY RPC on local object behaviour, this is not allowed.");

            InvokeRpcHandler(hashCode, data);
        }

        /// <summary>
        /// Receive rpc on object with authority.
        /// </summary>
        internal void ReceiveRpcLocally(RpcWithoutAuthorityState state)
        {
            if (!IsLocal)
                throw new Exception("Trying to receive AUTHORITY RPC on remote object behaviour, this is not allowed");

            InvokeRpcHandler(state.RpcState.Hash, state.RpcState.Payload);
        }

        /// <summary>
        /// Receive rpc on master client. (Called from GSTransport).
        /// </summary>
        internal void ReceiveRpcOnMasterClient(RpcWithoutAuthorityState state)
        {
            if (!IsOwnedByMasterClient)
                throw new Exception("Trying to receive MASTER CLIENT RPC on object behaviour, which is not owner by master client, this is not allowed");

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
            if (m_RpcHandlers.TryGetValue(hashCode, out var handler))
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

        public void DestroyViaNetwork()
        {
            if (!IsLocal)
                throw new Exception("Trying to destroy remote object, this is not allowed");

            foreach (var behaviour in m_Behaviours)
                behaviour.OnSoftDestroyRequestedLocallyInternal();

            State.ScheduleUpdate(NetObjectStateChangeRequestType.RemoveUpdateSoft);
        }

        /// <summary>
        /// Could be used instead of <see cref="UnityEngine.Behaviour.GetComponent{T}"/>
        /// </summary>
        public T GetBehaviour<T>() where T : NetObjectBehaviourBase
        {
            var type = typeof(T);
            if (!m_CachedBehaviours.TryGetValue(type, out var behaviour))
            {
                behaviour = GetComponent<T>();
                if (behaviour != null)
                    m_CachedBehaviours.Add(type, behaviour);
            }

            return behaviour as T;
        }

        private void OnDestroy()
        {
            if (State == null)
                return;

            State.OnOwnerChanged -= OnOwnerChanged;
            State.LocalUpdateRequest -= OnLocalUpdateRequest;

            m_NetObjectStateReference.SetTargetUid(NetObjectUid.Invalid);
            m_NetObjectStateReference.SetTargetWorld(null);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!Initialized)
                return;

            UnityEditor.Handles.Label(
                transform.position,
                $"Object: {InstanceId}, Owner: {(Owner == null ? "NONE" : Owner.Name)}, Local: {IsLocal}");
        }
#endif
    }
}