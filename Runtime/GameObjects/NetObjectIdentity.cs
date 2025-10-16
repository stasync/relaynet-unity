using Core.Networking;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetObjectIdentity : MonoBehaviour, INoAuthorityRPCReceiver
{
    public NetObjectUID InstanceId => State == null ? NetObjectUID.Invalid : State.UID;
    public bool IsOwnedByMasterClient => State != null && State.IsOwnedByMasterClient;
    public bool IsLocal => State != null && State.IsOwnedByLocalPlayer;
    public bool IsLocalPlayerMainTagObject
    {
        get
        {
            if (State == null || World == null)
                return false;

            var localPlayer = World.Players.LocalPlayer;
            return localPlayer != null && localPlayer.MainTagObjectUID.IsValid && localPlayer.MainTagObjectUID.Equals(State.UID);
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
    public bool IsSceneObject => State != null && State.IsSceneObject;
    public bool Initialized { get; private set; }

    private IGSInstanceObservable[] m_Observables;

    private NetWriter m_ObjectWriter;
    private NetReader m_ObjectReader;

    public NetObjectState State => m_NetObjectStateReference.State;
    private readonly NetObjectStateReference m_NetObjectStateReference = new ();

    /// <summary>
    /// Initialization. Called from GSInstanceFactory. 
    /// Shouldn't be called from top-level API.
    /// </summary>
    public void Initialize(NetWorld world, NetObjectUID stateUID)
    {
        if (Initialized)
            throw new Exception("Object already initialized");

        m_Observables = gameObject
            .GetComponents<IGSInstanceObservable>();

        World = world;

        m_NetObjectStateReference.SetTargetUID(stateUID);
        m_NetObjectStateReference.SetTargetWorld(world);

        State.OnOwnerChanged += OnOwnerChanged;
        State.LocalUpdateRequest += OnLocalUpdateRequest;

        var viewStateData = State.DataCollection.GetOrCreateData<NetObjectViewStateData>();
        if (!IsLocal)
            Deserialize(viewStateData.UnstructuredData, force: true, firstTime: true); // Force true to allow deserialization before object initialized.

        // Pre initialization.
        foreach (var o in m_Observables)
            o.OnPreInitialize();

        // Mark object as initialized.
        Initialized = true;

        // Owner changed.
        OnOwnerChanged();

        // Register RPC handlers.
        foreach (var o in m_Observables)
        {
            var members = Find.MethodsWithAttribute<AutoRegisterRPCHandler>(o, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

            foreach (var methodInfo in members.Keys)
            {
                var parameters = methodInfo.GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != typeof(byte[]))
                {
                    Debug.LogError($"Invalid method info parameters for method {methodInfo.Name}");
                    continue;
                }

                if (methodInfo.CreateDelegate(typeof(Action<byte[]>), o) is Action<byte[]> action)
                    RegisterRPCHandler(action);
            }
        }

        // Tell components about object was initialized.
        foreach (var o in m_Observables)
        {
            if (!viewStateData.ViewInitialized)
                o.OnInitializeViewData();
        }

        foreach (var o in m_Observables)
            o.OnPostInitialize();

        // Invokes buffered rpc, must be called after deserialization done.
        // Only for remote objects.
        if (!IsLocal)
            State.RPCBuffer.ExecuteRemotely(receivePersistent: true, ReceiveRPCRemotely);
    }

    /// <summary>
    /// Other client disconnected. This method calls before actual player reference removed, so here we have valid owner.
    /// </summary>
    public void OnDisconnected(ClientUID peerId)
    {
        foreach (var b in m_Observables)
            b.OnDisconnected(peerId);
    }

    private void OnLocalUpdateRequest(ObjectStateChangeRequestType stateChengeType)
    {
        if (!Initialized)
            throw new InvalidOperationException($"Object {gameObject.name} not initialized");

        if (!IsLocal)
            return;

        foreach (var b in m_Observables)
            b.OnAuthorityUpdate();

        State.ChangeObjectViewDataLocally(Serialize(), stateChengeType == ObjectStateChangeRequestType.InitializationUpdate);
    }

    /// <summary>
    /// Object owner was changed.
    /// </summary>
    private void OnOwnerChanged()
    {
        Debug.LogWarningFormat($"{gameObject.name} object with ID: {InstanceId} owner changed, now owner is {Owner}");
        foreach (var b in m_Observables)
            b.OnOwnerChanged();
    }

    /// <summary>
    /// Ownership managment, anyone can change scene object owner.
    /// </summary>
    public void OwnObject()
    {
        if (!Initialized)
            throw new Exception("You can manage owners only for initialized objects");

        if (!IsSceneObject)
            throw new Exception("You can manage owners only for scene objects");

        State.ScheduleOwnerChanged(World.Players.LocalPlayerUID);
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
        foreach (var b in m_Observables)
            b.Serialize(writer);
    }

    /// <summary>
    /// Deserialize whole object.
    /// </summary>
    public void Deserialize(byte[] data, bool force, bool firstTime)
    {
        if (IsLocal)
        {
            // TODO: Need to deserialize this data somehow, or change ownership changing pipeline.
            Debug.LogError($"Trying to deserialize local player object '{gameObject.name}', this is not allowed. Maybe owner was changed before. NOTE: This is normal, if owner was changed before.", gameObject);
            return;
        }

        if (!Initialized && !force)
        {
            Debug.LogWarning($"Trying to deserialize object '{gameObject.name}' that is not initialized, this is not allowed. Use 'force' option to do this.", gameObject);
            return;
        }

        if (data == null)
            return;

        m_ObjectReader ??= new NetReader();
        m_ObjectReader.Replace(data);

        foreach (var b in m_Observables)
            b.Deserialize(m_ObjectReader, firstTime);

        // Receive rpc buffer if object initialized.
        if (Initialized)
            State.RPCBuffer.ExecuteRemotely(receivePersistent: false, ReceiveRPCRemotely);
    }

    /// <summary>
    /// RPC handlers.
    /// </summary>
    private readonly Dictionary<int, Action<byte[]>> m_RPCHandlers = new();

    /// <summary>
    /// Register handler to receive RPC (Better to call in OnEnable or OnInit).
    /// </summary>
    /// <param name="handler"></param>
    public void RegisterRPCHandler(Action<byte[]> handler) =>
        RegisterRPCHandler(RPCUtils.ActionToString(handler), handler);

    private void RegisterRPCHandler(string rpcCode, Action<byte[]> handler)
    {
        var rpcHash = RPCUtils.NameToHash(rpcCode);
        // WE DON'T NEED TO ASSIGN MORE THAN 1 (ONE) RPC HANDLER FOR EACH RPC CODE.
        if (!m_RPCHandlers.TryAdd(rpcHash, handler))
            throw new InvalidOperationException(gameObject.name + " already contains handler for '" + rpcCode + "' rpc code");
    }

    /// <summary>
    /// RPC, invokes locally and sends to ALL clients, who HAS NO AUTHORITY on this object.
    /// </summary>
    public void OwnerToOthersRPC(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable, bool invokeLocally = true) =>
        OwnerToOthersRPC(RPCUtils.ActionToString(rpcHandlerMethod), data, deliveryMethod, invokeLocally);

    /// <summary>
    /// RPC, invokes locally and sends to ALL clients, who HAS NO AUTHORITY on this object.
    /// </summary>
    private void OwnerToOthersRPC(string rpcCode, byte[] data, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable, bool invokeLocally = true)
    {
        if (!IsLocal)
            throw new Exception("Trying to send AuthRPC on remote object's behaviour, this is not allowed. Use NonAuthRPC if you not sure about object's authority");

        var hashCode = RPCUtils.NameToHash(rpcCode);
        OwnerToOthersRPC(hashCode, data, persistent: false, RPCBufferChangeMethod.Add, deliveryMethod, invokeLocally);
    }

    private void OwnerToOthersRPC(int hashCode, byte[] data, bool persistent, RPCBufferChangeMethod bufferChangeMethod, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable, bool invokeLocally = true)
    {
        if (!IsLocal)
            throw new Exception("Trying to send AuthRPC on remote object's behaviour, this is not allowed. Use NonAuthRPC if you not sure about object's authority");

        // Try to invoke handler locally.
        if (invokeLocally)
            InvokeRpcHandler(hashCode, data);

        // Add to sending buffer.
        State.ScheduleRPC(hashCode, data, persistent, bufferChangeMethod, deliveryMethod);
    }

    /// <summary>
    /// Same as AuthRPC, but buffered.
    /// Only works via RELIABLE delivery.
    /// </summary>
    public void OwnerToOthersRPCBuffered(Action<byte[]> rpcHandlerMethod, byte[] data, RPCBufferChangeMethod bufferChangeMethod = RPCBufferChangeMethod.Replace, bool invokeLocally = true) =>
        OwnerToOthersRPCBuffered(RPCUtils.ActionToString(rpcHandlerMethod), data, bufferChangeMethod, invokeLocally);

    /// <summary>
    /// Same as AuthRPC, but buffered.
    /// Only works via RELIABLE delivery.
    /// </summary>
    private void OwnerToOthersRPCBuffered(string rpcCode, byte[] data, RPCBufferChangeMethod bufferChangeMethod = RPCBufferChangeMethod.Replace, bool invokeLocally = true) =>
         OwnerToOthersRPC(RPCUtils.NameToHash(rpcCode), data, persistent: true, bufferChangeMethod, DeliveryMethod.Reliable, invokeLocally);

    /// <summary>
    /// RPC variation, sends RPC to SINGLE client, who HAS AUTHORITY on this object.
    /// IF we have authority, just invokes locally without sending data.
    /// </summary>
    public void ToOwnerRPC(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable) =>
        ToOwnerRPC(RPCUtils.ActionToString(rpcHandlerMethod), data, deliveryMethod);

    /// <summary>
    /// RPC variation, sends RPC to SINGLE client, who HAS AUTHORITY on this object.
    /// IF we have authority, just invokes locally without sending data.
    /// </summary>
    private void ToOwnerRPC(string rpcCode, byte[] data, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable)
    {
        var hashCode = RPCUtils.NameToHash(rpcCode);
        // If we have authority on this object, don't need to send this kind of RPC.
        if (IsLocal)
            InvokeRpcHandler(hashCode, data);
        else
            State.ScheduleRPCWithoutAuthority(NoAuthorityRPCType.ToOwner, hashCode, data, deliveryMethod);
    }

    /// <summary>
    /// RPC variation, sends RPC to MASTER client.
    /// It does not matter whether MASTER client is a owner of this object or not.
    /// IF we are master client, just invokes locally without sending data.
    /// </summary>
    public void ToMasterRPC(string rpcCode, byte[] data, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable)
    {
        var hashCode = RPCUtils.NameToHash(rpcCode);
        // If we have authority on this object, don't need to send this kind of RPC.
        if (IsOwnedByMasterClient)
            InvokeRpcHandler(hashCode, data);
        else
            State.ScheduleRPCWithoutAuthority(NoAuthorityRPCType.ToMaster, hashCode, data, deliveryMethod);
    }

    /// <summary>
    /// RPC variation, sends RPC to SINGLE client, who HAS AUTHORITY on this object.
    /// IF we have authority, just invokes locally without sending data.
    /// </summary>
    public void ToAllRPC(Action<byte[]> rpcHandlerMethod, byte[] data, bool invokeLocally, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable) =>
        ToAllRPC(RPCUtils.ActionToString(rpcHandlerMethod), data, invokeLocally, deliveryMethod);

    /// <summary>
    /// RPC variation, sends RPC to SINGLE client, who HAS AUTHORITY on this object.
    /// IF we have authority, just invokes locally without sending data.
    /// </summary>
    private void ToAllRPC(string rpcCode, byte[] data, bool invokeLocally, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable)
    {
        var hashCode = RPCUtils.NameToHash(rpcCode);
        if (invokeLocally)
            InvokeRpcHandler(hashCode, data);

        State.ScheduleRPCWithoutAuthority(NoAuthorityRPCType.ToAll, hashCode, data, deliveryMethod);
    }

    /// <summary>
    /// Remove RPC from buffer
    /// </summary>
    /// <param name="rpcHandlerMethod"></param>
    public void RemoveRPCFromBuffer(Action<byte[]> rpcHandlerMethod) => 
        RemoveRPCFromBuffer(RPCUtils.ActionToString(rpcHandlerMethod));

    public void RemoveRPCFromBuffer(string rpcCode) => 
        State.ScheduleRemovePersistentRPC(RPCUtils.NameToHash(rpcCode));

    /// <summary>
    /// Receive rpc on object WITHOUT authority (Called from GSTransport).
    /// </summary>
    private void ReceiveRPCRemotely(int hashCode, byte[] data)
    {
        if (IsLocal)
            throw new Exception("Trying to recieve NO-AUTHORITY RPC on local object behaviour, this is not allowed");

        InvokeRpcHandler(hashCode, data);
    }

    /// <summary>
    /// Receive rpc on object with authority. (Called from GSTransport).
    /// </summary>
    void INoAuthorityRPCReceiver.ReceiveRPCLocally(RPCWithoutAuthorityState state) => 
        ReceiveRPCLocally(state);
    
    private void ReceiveRPCLocally(RPCWithoutAuthorityState state)
    {
        if (!IsLocal)
            throw new Exception("Trying to receive AUTHORITY RPC on remote object behaviour, this is not allowed");

        InvokeRpcHandler(state.RPCState.Hash, state.RPCState.Payload);
    }

    /// <summary>
    /// Receive rpc on master client. (Called from GSTransport).
    /// </summary>
    void INoAuthorityRPCReceiver.ReceiveRPCOnMasterClient(RPCWithoutAuthorityState state) => 
        ReceiveRPCOnMasterClient(state);
    
    private void ReceiveRPCOnMasterClient(RPCWithoutAuthorityState state)
    {
        if (!IsOwnedByMasterClient)
            throw new Exception("Trying to recieve MASTER CLIENT RPC on object behaviour, which is not owner by master client, this is not allowed");

        InvokeRpcHandler(state.RPCState.Hash, state.RPCState.Payload);
    }

    void INoAuthorityRPCReceiver.ReceiveRPC(RPCWithoutAuthorityState state) => 
        InvokeRpcHandler(state.RPCState.Hash, state.RPCState.Payload);

    private void InvokeRpcHandler(int hashCode, byte[] data)
    {
        if (m_RPCHandlers.TryGetValue(hashCode, out var handler))
            handler.Invoke(data);
        else
            Debug.LogErrorFormat($"Handler for RPC with hash {hashCode} '{RPCUtils.TryFindStringByHashSlow(hashCode)}' doesn't registered on object {gameObject.name}");
    }

    public void DestroyViaNetwork()
    {
        if (!IsLocal)
            throw new Exception("Trying to destroy remote object, this is not allowed");

        foreach (var b in m_Observables)
            b.OnSoftDestroyRequestedLocally();

        State.ScheduleUpdate(ObjectStateChangeRequestType.RemoveUpdateSoft);
    }

    private void OnDestroy()
    {
        if (State == null) 
            return;
        
        State.OnOwnerChanged -= OnOwnerChanged;
        State.LocalUpdateRequest -= OnLocalUpdateRequest;

        m_NetObjectStateReference.SetTargetUID(NetObjectUID.Invalid);
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

    private readonly Dictionary<Type, GSInstanceBehaviourBase> m_CachedBehaviours = new ();

    public T GetBehaviour<T>() where T : GSInstanceBehaviourBase
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
}
