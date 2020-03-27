using Core.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public enum RPCBufferChangeMethod : byte
{
    Replace, 
    Add,
}

[DisallowMultipleComponent]
public sealed class NetObjectIdentity : MonoBehaviour, IRPCReciever, IJSONSerializable
{
    public NetObjectUID InstanceId => State == null ? NetObjectUID.Invalid : State.UID;
    public bool IsOwnedByMasterClient => State != null && State.IsOwnedByMasterClient;
    public bool IsLocal => State != null && State.IsOwnedByLocalPlayer;
    public bool IsLocalPlayerTagObject => State != null && Owner != null && Owner.TagObjectUID == State.UID;
    public bool IsDirty => m_Observables.Any(o => o.IsDirty) || m_RPCBuffer.IsDirty;


    GenericRPCBuffer m_RPCBuffer = new GenericRPCBuffer();

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

    IEnumerable<IGSInstanceObservable> m_Observables;
    NetWriter m_ObjectWriter;
    NetReader m_ObjectReader;

    public NetObjectState State { get; private set; }

    /// <summary>
    /// Initialization. Called from GSInstanceFactory. 
    /// Shouldn't be called from top-level API.
    /// </summary>
    public NetObjectIdentity Initialze(NetObjectState state, NetWorld world, object[] additionalData)
    {
        if (Initialized)
            throw new Exception("Object already initialized");

        m_Observables = gameObject
            .GetComponents<IGSInstanceObservable>()
            .OrderBy(c => c.SerializationOrder)
            .ToList();

        World = world;

        State = state;
        State.OnOwnerChanged += OnOwnerChanged;
        State.LocalUpdateRequest += OnLocalUpdateRequest;
        /*
         * Skip deserialization if it calls on local object.
         * Object can be created with null data (see GSInstanceFactory.CreateObject). Just skip deserialization here.
         */
        if (!IsLocal && State.Data != null)
            Deserialize(State.Data, true, true); //Force true to allow deseriazation before object initialized.

        if(additionalData != null && additionalData.Length > 0)
            foreach (var o in m_Observables)
                o.OnCreatedWithAdditionalData(additionalData);

        //Tell components abount object pre initialization;
        foreach (var o in m_Observables)
            o.OnPreInitialize();

        //Mark object as initialized;
        Initialized = true;

        //Owner changed;
        OnOwnerChanged();

        //Tell components abount object was initialized;
        foreach (var o in m_Observables)
        {
            TryAutoRegisterRPCHandlers(o);
            o.OnPostInitialize();
        }

        //Invokes buffered rpc, must be called after deserialization done.
        //Only for remote objects.
        if (!IsLocal)
            m_RPCBuffer.Receive(true, ReceiveRPCRemotely);

        return this;
    }

    void TryAutoRegisterRPCHandlers(IGSInstanceObservable targetObject)
    {
        var members = System.Reflection.Find
                .MethodsWithAttribute<AutoRegisterRPCHandler>(targetObject, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

        foreach (var m in members)
        {
            var action = Create.DelegateWithSingleParameter<byte[]>(targetObject, m.Key);
            if (action != null)
                RegisterRPCHandler(action);
        }
    }

    /// <summary>
    /// Other client connected and loaded game scene, set all component as dirty to send valid object state to other clients.
    /// </summary>
    public void OnBecomesReady(ClientUID peerId)
    {
        if (IsLocal)
            State.ScheduleUpdate(ObjectStateChangeRequestType.InitializationUpdate);
    }

    /// <summary>
    /// Other client disconnected. This method calls before actual player reference removed, so here we have valid owner.
    /// </summary>
    public void OnDisconnected(ClientUID peerId)
    {
        foreach (var b in m_Observables)
            b.OnDisconnected(peerId);
    }

    void OnLocalUpdateRequest(bool forced)
    {
        if (!Initialized)
            throw new InvalidOperationException($"Object {gameObject.name} not initialized");

        if (!IsLocal)
            return;

        if (forced)
            SetDirtyIfLocal();

        foreach (var b in m_Observables)
            b.OnUpdate();

        if (forced || IsDirty)
            State.ChangeObjectDataLocaly(Serialize());
    }

    /// <summary>
    /// Object owner was changed.
    /// </summary>
    void OnOwnerChanged()
    {
        Debug.LogWarningFormat($"{gameObject.name} object with ID: {InstanceId} owner changed, now owner is {Owner.ToString()}");
        foreach (var b in m_Observables)
            b.OnOwnerChanged();
    }

    /// <summary>
    /// Ownership managmet, anyone can change scene object owner.
    /// </summary>
    public void OwnObejct()
    {
        if (!Initialized)
            throw new Exception("You can manage owners only for initialized objects");

        if (!IsSceneObject)
            throw new Exception("You can manage owners only for scene objects");

        State.ScheduleOwnerChanged(World.Players.LocalPlayerUID);
    }

    /// <summary>
    /// Sets all behaviours dirty, if object is local.
    /// </summary>
    public void SetDirtyIfLocal()
    {
        if (!IsLocal)
            return;

        //Set RPC Buffers dirty;
        m_RPCBuffer.SetDirty();

        //Set components dirty;
        foreach (var o in m_Observables)
            o.SetDirty();
    }

    byte[] Serialize()
    {
        //Serialize object if it becomes dirty;
        if (m_ObjectWriter == null)
            m_ObjectWriter = new NetWriter();

        SerializeTo(m_ObjectWriter);
        return m_ObjectWriter.ToArray();
    }

    void SerializeTo(NetWriter writer)
    {
        if (writer == null)
            throw new ArgumentNullException(nameof(writer));

        writer.SeekZero();
        m_RPCBuffer.Serialize(writer);

        foreach (var b in m_Observables)
            b.Serialize(writer);
    }

    NetWriter SerializeToNewWriter()
    {
        var writer = new NetWriter();
        SerializeTo(writer);
        return writer;
    }

    /// <summary>
    /// Deserizlize whole object
    /// </summary>
    /// <param name="data"></param>
    /// <param name="force"></param>
    /// <param name="firstTime"></param>
    /// <returns></returns>
    public NetObjectIdentity Deserialize(byte[] data, bool force, bool firstTime)
    {
        if (IsLocal)
        {
            // TODO: Need to deserualized this data somehow, or change ownership changing pipeline.
            Debug.LogErrorFormat("Trying to deserialize local player object {0}, this is not allowed. Maybe owner was changed before. NOTE: This is normal, if owner was chaged bofore.", gameObject.name);
            return this;
        }

        if (!Initialized && !force)
        {
            Debug.LogWarningFormat("Trying to deserialize object {0} that is not initialized, this is not allowed. Use 'force' option to do this.", gameObject.name);
            return null;
        }

        if (data == null)
            return this;

        if (m_ObjectReader == null)
            m_ObjectReader = new NetReader();
        m_ObjectReader.Replace(data);

        //RPC
        m_RPCBuffer.Deserialize(m_ObjectReader);

        foreach (var b in m_Observables)
            b.Deserialize(m_ObjectReader, firstTime);

        //Recieve rpc buffer if object initialized;
        if(Initialized)
            m_RPCBuffer.Receive(false, ReceiveRPCRemotely);

        return this;
    }

    /// <summary>
    /// RPC handlers.
    /// </summary>
    IDictionary<int, Action<byte[]>> m_RPCHandlers = new Dictionary<int, Action<byte[]>>();

    /// <summary>
    /// Register handler to recieve RPC (Better to call in OnEnable or OnInit).
    /// </summary>
    /// <param name="handler"></param>
    public void RegisterRPCHandler(Action<byte[]> handler) => RegisterRPCHandler(RPCUtils.ActionToString(handler), handler);

    public void RegisterRPCHandler(string rpcCode, Action<byte[]> handler)
    {
        var rpcHash = RPCUtils.NameToHash(rpcCode);
        /// WE DON'T NEED TO ASSIGN MORE THAN 1 (ONE) RPC HANDLER FOR EACH RPC CODE.
        if (m_RPCHandlers.ContainsKey(rpcHash))
            throw new InvalidOperationException(gameObject.name + " already contains handler for '" + rpcCode + "' rpc code");

        m_RPCHandlers.Add(rpcHash, handler);
    }

    /// <summary>
    /// RPC, invokes locally and sends to ALL clients, who HAS NO AUTHORITY on this object.
    /// </summary>
    public void OwnerToOthersRPC(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryIntent deliveryIntent = DeliveryIntent.Reliable, bool invokeLocaly = true) =>
        OwnerToOthersRPC(RPCUtils.ActionToString(rpcHandlerMethod), data, deliveryIntent, invokeLocaly);

    /// <summary>
    /// RPC, invokes locally and sends to ALL clients, who HAS NO AUTHORITY on this object.
    /// </summary>
    void OwnerToOthersRPC(string rpcCode, byte[] data, DeliveryIntent deliveryIntent = DeliveryIntent.Reliable, bool invokeLocaly = true)
    {
        if (!IsLocal)
            throw new Exception("Trying to send AuthRPC on remote object's behaviour, this is not allowed. Use NonAuthRPC if you not sure about object's authority");

        var hashCode = RPCUtils.NameToHash(rpcCode);
        OwnerToOthersRPC(hashCode, data, false, deliveryIntent, invokeLocaly);
    }

    void OwnerToOthersRPC(int hashCode, byte[] data, bool persistent, DeliveryIntent deliveryIntent = DeliveryIntent.Reliable, bool invokeLocaly = true)
    {
        if (!IsLocal)
            throw new Exception("Trying to send AuthRPC on remote object's behaviour, this is not allowed. Use NonAuthRPC if you not sure about object's authority");

        //Try invoke handler locally.
        if (invokeLocaly)
            InvokeRpcHandler(hashCode, data);

        //Add to sending buffeR;
        m_RPCBuffer.AddOrUpdate(hashCode, data, persistent, RPCBufferChangeMethod.Add);

        if (deliveryIntent == DeliveryIntent.Reliable)
            State.ScheduleUpdate(ObjectStateChangeRequestType.ReliableUpdate);
    }

    /// <summary>
    /// Same as AuthRPC, but buffered.
    /// Only works via RELIABLE delivery.
    /// </summary>
    public void OwnerToOthersRPCBuffered(Action<byte[]> rpcHandlerMethod, byte[] data, RPCBufferChangeMethod rpcType = RPCBufferChangeMethod.Replace, bool invokeLocaly = true) =>
        OwnerToOthersRPCBuffered(RPCUtils.ActionToString(rpcHandlerMethod), data, rpcType, invokeLocaly);

    /// <summary>
    /// Same as AuthRPC, but buffered.
    /// Only works via RELIABLE delivery.
    /// </summary>
    void OwnerToOthersRPCBuffered(string rpcCode, byte[] data, RPCBufferChangeMethod rpcType = RPCBufferChangeMethod.Replace, bool invokeLocaly = true) =>
         OwnerToOthersRPC(RPCUtils.NameToHash(rpcCode), data, true, DeliveryIntent.Reliable, invokeLocaly);

    /// <summary>
    /// RPC variation, sends RPC to SINGLE client, who HAS AUTHORITY on this object.
    /// IF we have authority, just invokes locally without sending data.
    /// </summary>
    public void ToOwnerRPC(Action<byte[]> rpcHandlerMethod, byte[] data, DeliveryIntent deliveryIntent = DeliveryIntent.Reliable) =>
        ToOwnerRPC(RPCUtils.ActionToString(rpcHandlerMethod), data, deliveryIntent);

    /// <summary>
    /// RPC variation, sends RPC to SINGLE client, who HAS AUTHORITY on this object.
    /// IF we have authority, just invokes locally without sending data.
    /// </summary>
    void ToOwnerRPC(string rpcCode, byte[] data, DeliveryIntent deliveryIntent = DeliveryIntent.Reliable)
    {
        var hashCode = RPCUtils.NameToHash(rpcCode);
        //If we have authority on this object, don't need to send this kind of RPC.
        if (IsLocal)
            InvokeRpcHandler(hashCode, data);
        else
            State.ScheduleRPCWithoutAuthority(RPCType.ToOwner, hashCode, data, deliveryIntent);
    }

    /// <summary>
    /// RPC variation, sends RPC to MASTER client.
    /// It does not matter whether MASTER client is a owner of this object or not.
    /// IF we are master client, just invokes locally without sending data.
    /// </summary>
    public void ToMasterRPC(string rpcCode, byte[] data, DeliveryIntent deliveryIntent = DeliveryIntent.Reliable)
    {
        var hashCode = RPCUtils.NameToHash(rpcCode);

        if (IsOwnedByMasterClient)
            InvokeRpcHandler(hashCode, data);
        else
            State.ScheduleRPCWithoutAuthority(RPCType.ToMaster, hashCode, data, deliveryIntent);
    }

    /// <summary>
    /// Clear current rpc buffer and sets buffer dirty.
    /// </summary>
    public void ClearRPCBuffer() => m_RPCBuffer.ClearPersistent(true);

    /// <summary>
    /// Remove RPC from buffer
    /// </summary>
    /// <param name="rpcHandlerMethod"></param>
    public void RemoveRPCFromBuffer(Action<byte[]> rpcHandlerMethod) => RemoveRPCFromBuffer(RPCUtils.ActionToString(rpcHandlerMethod));

    public void RemoveRPCFromBuffer(string rpcCode) => m_RPCBuffer.RemovePersistent(RPCUtils.NameToHash(rpcCode));

    /// <summary>
    /// Recieve rpc on object WITHOUT authority (Called from GSTransport).
    /// </summary>
    void ReceiveRPCRemotely(int hashCode, byte[] data)
    {
        if (IsLocal)
            throw new Exception("Trying to recieve NO-AUTHORITY RPC on local object behaviour, this is not allowed");

        InvokeRpcHandler(hashCode, data);
    }

    /// <summary>
    /// Recieve rpc on object with authority. (Called from GSTransport).
    /// </summary>
    void IRPCReciever.RecieveRPCLocaly(int hashCode, byte[] data) => RecieveRPCLocaly(hashCode, data);
    void RecieveRPCLocaly(int hashCode, byte[] data)
    {
        if (!IsLocal)
            throw new Exception("Trying to recieve AUTHORITY RPC on remote object behaviour, this is not allowed");

        InvokeRpcHandler(hashCode, data);
    }

    /// <summary>
    /// Recieve rpc on master clent. (Called from GSTransport).
    /// </summary>
    void IRPCReciever.RecieveRPCOnMasterClient(int hashCode, byte[] data) => RecieveRPCOnMasterClient(hashCode, data);
    void RecieveRPCOnMasterClient(int hashCode, byte[] data)
    {
        if (!IsOwnedByMasterClient)
            throw new Exception("Trying to recieve MASTER CLIENT RPC on object behaviour, which is not owner by master client, this is not allowed");

        InvokeRpcHandler(hashCode, data);
    }

    void InvokeRpcHandler(int hashCode, byte[] data)
    {
        if (m_RPCHandlers.ContainsKey(hashCode))
            m_RPCHandlers[hashCode].Invoke(data);
        else
            Debug.LogErrorFormat("Handler for RPC with hash {0} doesn't registred on object {1}", hashCode, gameObject.name);
    }

    public void DestroyViaNetwork()
    {
        if (!IsLocal)
            throw new Exception("Trying to destroy remote object, this is not allowed");

        State.ScheduleUpdate(ObjectStateChangeRequestType.RemoveUpdate);
    }

    #if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!Initialized)
            return;

        UnityEditor.Handles.Label(
            transform.position, 
            string.Format("Object: {0}, Owner: {1}, Local: {2}", 
            InstanceId.ToString(), 
            Owner == null ? "NONE" : Owner.Name, 
            IsLocal));
    }
    #endif

    Dictionary<Type, GSInstanceBehaviourBase> m_CachedBehaviours = new Dictionary<Type, GSInstanceBehaviourBase>();
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

    public string ToJSONProperty() => "{" + RealtimeJSONUtils.ToJSONValue($"obj {InstanceId} {gameObject.name}") + ":" + RealtimeJSONUtils.ToJSONCollection(m_Observables.Select(v => v.ToJSONProperty()).ToArray()) + "}";

    #region STATIC_HELPERS
    static ServiceProvider<GSNetwork> s_NetworkProvider = new ServiceProvider<GSNetwork>();

    public static NetObjectIdentity Find(NetObjectUID instanceId) => 
        s_NetworkProvider.Instance.Realtime.FindNetworkObject(instanceId);

    public static T Find<T>(NetObjectUID instanceId) where T : GSInstanceBehaviourBase => 
        s_NetworkProvider.Instance.Realtime.FindNetworkObject<T>(instanceId);
    #endregion
}
