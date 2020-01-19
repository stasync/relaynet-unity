using Core.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public enum BufferedRPCType
{
    Replace, 
    Add,
}

[DisallowMultipleComponent]
public sealed class GSInstanceIdentity : MonoBehaviour, IRPCReciever, IIdentity
{
    class DefaultRpcBuffer : IRPCBuffer
    {
        class BufferedRPCData
        {
            public BufferedRPCData(int key, byte[] data)
            {
                Key = key;
                Data = data;
            }

            public BufferedRPCData(NetReader reader)
            {
                Key = reader.ReadInt32();
                Data = reader.ReadBytesAndSize();
            }

            public NetWriter Serialize(NetWriter writer)
            {
                writer.Write(Key);
                writer.WriteBytesAndSize(Data);
                return writer;
            }

            public readonly int Key;
            public readonly byte[] Data;
        }

        public bool IsDirty { get; private set; }

        List<BufferedRPCData> m_Buffer;

        public DefaultRpcBuffer()
        {
            m_Buffer = new List<BufferedRPCData>();
        }

        void IRPCBuffer.AddOrUpdate(int hashCode, byte[] data, BufferedRPCType type)
        {
            if (type == BufferedRPCType.Replace)
                Remove(hashCode);

            m_Buffer.Add(new BufferedRPCData(hashCode, data));
            IsDirty = true;
        }

        void ISerializable.Serialize(NetWriter writer)
        {
            writer.Write(IsDirty);
            if (!IsDirty)
                return;

            IsDirty = false;
            writer.WriteCollection(m_Buffer, (v, w) => v.Serialize(w));
        }

        void ISerializable.Deserialize(NetReader reader, bool firstTime)
        {
            var wasDirty = reader.ReadBoolean();
            if (!wasDirty)
                return;

            m_Buffer = reader.ReadCollection(r => new BufferedRPCData(r)).ToList();
        }

        public IEnumerator<byte[]> GetEnumerator() => m_Buffer
                .Select(r => r.Data)
                .GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => m_Buffer.GetEnumerator();

        void IRPCBuffer.Recieve(Action<int, byte[]> reciever)
        {
            foreach (var rpc in m_Buffer)
                reciever(rpc.Key, rpc.Data);
        }

        void Remove(int hashCode)
        {
            m_Buffer.RemoveAll(d => d.Key == hashCode);
            SetDirty();
        }

        void IRPCBuffer.Remove(int hashCode) => Remove(hashCode);

        public void SetDirty() => IsDirty = true;

        public override string ToString() => m_Buffer.ToString();

        public void Clear()
        {
            m_Buffer.Clear();
            IsDirty = true;
        }
    }

    [SerializeField]
    DeliveryIntent m_DeliveryIntent = DeliveryIntent.Unreliable;

    [SerializeField, Range(0.01f, 1f)]
    float m_UpdateRate = 0.05f;
    float m_NextUpdateEventTime;

    public GSInstanceId InstanceId { get; private set; }
    public bool IsOwnedByMasterClient => m_InternalNetwork != null && m_InternalNetwork.MasterPlayer == Owner;
    public bool IsLocal => m_InternalNetwork != null && m_InternalNetwork.LocalPlayer == Owner;
    public bool IsLocalPlayerTagObject => m_InternalNetwork.LocalPlayer.TagObjectId == InstanceId;
    public bool IsDirty => m_Observables.Any(o => o.IsDirty) || m_RpcBuffer.IsDirty;

    /// <summary>
    /// Rpc buffer
    /// </summary>
    IRPCBuffer m_RpcBuffer = new DefaultRpcBuffer();

    /// <summary>
    /// Readonly owner reference
    /// </summary>
    INetRTPlayer m_Owner;
    public INetRTPlayer Owner
    {
        get => m_Owner;
        private set
        {
            m_Owner = value;
            OnOwnerChanged();
        }
    }

    /// <summary>
    /// Readonly transport reference
    /// </summary>
    INetwork m_InternalNetwork;

    /// <summary>
    /// Object prefab id
    /// </summary>
    public string PrefabId { get; private set; }
    public bool IsSceneObject { get; private set; }
    public bool Initialized { get; private set; }

    IEnumerable<IGSInstanceObservable> m_Observables;
    NetWriter m_ObjectWriter;

    /// <summary>
    /// Initialization. Called from GSInstanceFactory. 
    /// Shouldn't be called from top-level API.
    /// </summary>
    /// <param name="prefabId"></param>
    /// <param name="instanceId"></param>
    /// <param name="owner"></param>
    /// <param name="network"></param>
    /// <param name="data"></param>
    /// <returns></returns>
    GSInstanceIdentity IIdentity.Initialze(
        string prefabId, 
        bool isSceneObject,
        GSInstanceId instanceId,
        INetRTPlayer owner, 
        INetwork network, 
        byte[] data,
        object[] additionalData)
    {
        if (Initialized)
            throw new Exception("Object already initialized");

        m_Observables = gameObject.GetComponents<IGSInstanceObservable>();
        m_Owner = owner ?? throw new ArgumentNullException("owner");

        InstanceId = instanceId;
        PrefabId = prefabId;
        IsSceneObject = isSceneObject;
        m_InternalNetwork = network;

        /*
         * Skip deserialization if it calls on local object.
         * Object can be created with null data (see GSInstanceFactory.CreateObject). Just skip deserialization here.
         */
        if (!IsLocal && data != null)
            Deserialize(data, true, true); //Force true to allow deseriazation before object initialized.

        if(additionalData != null && additionalData.Length > 0)
            foreach (var o in m_Observables)
                o.OnCreatedWithAdditionalData(additionalData);

        //Tell components abount object pre initialization;
        foreach (var o in m_Observables)
            o.OnPreInitialize();

        //Mark object as initialized;
        Initialized = true;

        //Forse obect to send itself after creation.
        //Is local check inside this method.
        ForceSerializeIfLocal();

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
            m_RpcBuffer.Recieve(RecieveRPCRemotely);

        return this;
    }

    void TryAutoRegisterRPCHandlers(IGSInstanceObservable targetObject)
    {
        var members = System.Reflection.Find
                .MethodsWithAttribute<AutoRegisterRPCHandler>(targetObject, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

        foreach (var m in members)
        {
            var action = Create.DelegateWithSingleParameter<byte[]>(targetObject, m.Member);
            if (action != null)
                RegisterRPCHandler(action);
        }
    }

    /// <summary>
    /// Other client connected and loaded game scene, set all component as dirty to send valid object state to other clients.
    /// </summary>
    public void OnBecomesReady(ClientUID peerId)
    {
        if (!IsLocal)
            return;

        ForceSerializeIfLocal();
    }

    /// <summary>
    /// Other client disconnected. This method calls before actual player reference removed, so here we have valid owner.
    /// </summary>
    public void OnDisconnected(ClientUID peerId)
    {
        //Is object owned by disconnected player and a scene object, trying to change owner.
        if (IsSceneObject && peerId == Owner.ConnectionUID)
        {
            //Is local client becomes master client, set object's owner and sync it.
            if (m_InternalNetwork.IsMasterClient)
                OwnObejct();
        }

        foreach (var b in m_Observables)
            b.OnDisconnected(peerId);
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

        SetOwner(m_InternalNetwork.LocalPlayer, true);
    }

    /// <summary>
    /// Change object owner.
    /// </summary>
    /// <param name="owner"></param>
    public void SetOwner(INetRTPlayer owner, bool send = false)
    {
        if (!Initialized)
            throw new Exception("You can manage owners only for initialized objects");

        Owner = owner ?? throw new ArgumentNullException("owner");
        //Send changes
        if(send)
            m_InternalNetwork.SetOwner(InstanceId, Owner.ConnectionUID);
    }

    /// <summary>
    /// Sets all behaviours dirty, if object is local.
    /// </summary>
    void SetDirtyIfLocal()
    {
        if (!IsLocal)
            return;

        //Set RPC Buffer dirty;
        m_RpcBuffer.SetDirty();

        //Set components dirty;
        foreach (var o in m_Observables)
            o.SetDirty();
    }

    void FixedUpdate()
    {
        //Object not initialized or remote;
        if (!Initialized || !IsLocal)
            return;

        if (Time.fixedUnscaledTime < m_NextUpdateEventTime)
            return;

        //Update all behaviours;
        UpdateBehaviours();

        //Serialize object if it becomes dirty;
        if (IsDirty)
            TrySerialize(m_DeliveryIntent);
    }

    void UpdateBehaviours()
    {
        foreach (var b in m_Observables)
            b.OnUpdate();
    }

    void ForceSerializeIfLocal()
    {
        if (!IsLocal)
            return;

        //Force behaviour update;
        UpdateBehaviours();

        //Set object dirty;
        SetDirtyIfLocal();

        //Force serialization;
        TrySerialize(DeliveryIntent.Reliable, true);
    }

    void TrySerialize(DeliveryIntent deliveryIntent, bool initializable = false)
    {
        if (!IsLocal)
            throw new Exception("Trying to serialize remote player object, this is not allowed");

        if (!Initialized)
            throw new Exception("Trying to serialize object that not initialized, this is not allowed");

        if (m_ObjectWriter == null)
            m_ObjectWriter = new NetWriter();
        m_ObjectWriter.SeekZero();

        m_ObjectWriter.WriteInstanceId(InstanceId);
        m_ObjectWriter.Write(PrefabId);
        m_ObjectWriter.Write(IsSceneObject);

        m_RpcBuffer.Serialize(m_ObjectWriter);

        foreach (var b in m_Observables)
            b.Serialize(m_ObjectWriter);

        SendGameObject(m_ObjectWriter.ToArray(), initializable, deliveryIntent);

        m_NextUpdateEventTime = Time.fixedUnscaledTime + m_UpdateRate;
    }

    /// <summary>
    /// Deserizlize whole object
    /// </summary>
    /// <param name="data"></param>
    /// <param name="force"></param>
    /// <param name="firstTime"></param>
    /// <returns></returns>
    GSInstanceIdentity IIdentity.Deserialize(byte[] data, bool force, bool firstTime) => Deserialize(data, force, firstTime);
    GSInstanceIdentity Deserialize(byte[] data, bool force, bool firstTime)
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

        var r = new NetReader(data);

        //Check instance id
        var id = r.ReadInstanceId();
        if (id != InstanceId)
            throw new InvalidOperationException(string.Format("Object id is:{0}, but recieved id is {1}", InstanceId, id));

        var prefabId = r.ReadString();
        if (prefabId != PrefabId)
            throw new InvalidOperationException(string.Format("Object prefab id is:{0}, but recieved preafab id is {1}", PrefabId, prefabId));

        var isSceneObject = r.ReadBoolean();
        if (isSceneObject)
        {
            //Scene object can be handled here
        }

        //Buffered rpc
        m_RpcBuffer.Deserialize(r, firstTime);

        foreach (var b in m_Observables)
            b.Deserialize(r, firstTime);

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
    public void OwnerToOthersRPC(
        Action<byte[]> rpcHandlerMethod,
        byte[] data,
        DeliveryIntent deliveryIntent = DeliveryIntent.Reliable,
        bool invokeLocaly = true)
    {
        OwnerToOthersRPC(RPCUtils.ActionToString(rpcHandlerMethod), data, deliveryIntent, invokeLocaly);
    }

    /// <summary>
    /// RPC, invokes locally and sends to ALL clients, who HAS NO AUTHORITY on this object.
    /// </summary>
    public void OwnerToOthersRPC(
        string rpcCode,
        byte[] data, 
        DeliveryIntent deliveryIntent = DeliveryIntent.Reliable, 
        bool invokeLocaly = true)
    {
        if (!IsLocal)
            throw new Exception("Trying to send AuthRPC on remote object's behaviour, this is not allowed. Use NonAuthRPC if you not sure about object's authority");

        var hashCode = RPCUtils.NameToHash(rpcCode);
        OwnerToOthersRPC(hashCode, data, deliveryIntent, invokeLocaly);
    }

    void OwnerToOthersRPC(
        int hashCode,
        byte[] data,
        DeliveryIntent deliveryIntent = DeliveryIntent.Reliable,
        bool invokeLocaly = true)
    {
        if (!IsLocal)
            throw new Exception("Trying to send AuthRPC on remote object's behaviour, this is not allowed. Use NonAuthRPC if you not sure about object's authority");

        //Invoke handler locally.
        if (invokeLocaly)
            InvokeRpcHandler(hashCode, data);

        //Send ot others.
        m_InternalNetwork.RPC(RPCType.OwnerToOthers, InstanceId, hashCode, data, deliveryIntent);
    }

    /// <summary>
    /// Same as AuthRPC, but buffered.
    /// Only works via RELIABLE delivery.
    /// </summary>
    public void OwnerToOthersRPCBuffered(
        Action<byte[]> rpcHandlerMethod,
        byte[] data,
        BufferedRPCType rpcType = BufferedRPCType.Replace,
        bool invokeLocaly = true)
    {
        OwnerToOthersRPCBuffered(RPCUtils.ActionToString(rpcHandlerMethod), data, rpcType, invokeLocaly);
    }

    /// <summary>
    /// Same as AuthRPC, but buffered.
    /// Only works via RELIABLE delivery.
    /// </summary>
    public void OwnerToOthersRPCBuffered(
        string rpcCode,
        byte[] data, 
        BufferedRPCType rpcType = BufferedRPCType.Replace,
        bool invokeLocaly = true)
    {
        var hashCode = RPCUtils.NameToHash(rpcCode);

        //Call regular AuthRPC
        OwnerToOthersRPC(hashCode, data, DeliveryIntent.Reliable, invokeLocaly);

        //Add rpc to buffer, rpc buffer becomes dirty after this.
        m_RpcBuffer.AddOrUpdate(hashCode, data, rpcType);
    }

    /// <summary>
    /// RPC variation, sends RPC to SINGLE client, who HAS AUTHORITY on this object.
    /// IF we have authority, just invokes locally without sending data.
    /// </summary>
    public void ToOwnerRPC(
        Action<byte[]> rpcHandlerMethod,
        byte[] data,
        DeliveryIntent deliveryIntent = DeliveryIntent.Reliable)
    {
        ToOwnerRPC(RPCUtils.ActionToString(rpcHandlerMethod), data, deliveryIntent);
    }

    /// <summary>
    /// RPC variation, sends RPC to SINGLE client, who HAS AUTHORITY on this object.
    /// IF we have authority, just invokes locally without sending data.
    /// </summary>
    public void ToOwnerRPC(string rpcCode, byte[] data, DeliveryIntent deliveryIntent = DeliveryIntent.Reliable)
    {
        var hashCode = RPCUtils.NameToHash(rpcCode);
        //If we have authority on this object, don't need to send this kind of RPC.
        if (IsLocal)
            InvokeRpcHandler(hashCode, data);
        else
            m_InternalNetwork.RPC(RPCType.ToOwner, InstanceId, hashCode, data, deliveryIntent);
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
            m_InternalNetwork.RPC(RPCType.ToMaster, InstanceId, hashCode, data, deliveryIntent);
    }

    /// <summary>
    /// Clear current rpc buffer and sets buffer dirty.
    /// </summary>
    public void ClearRPCBuffer() => m_RpcBuffer.Clear();

    /// <summary>
    /// Remove RPC from buffer
    /// </summary>
    /// <param name="rpcHandlerMethod"></param>
    public void RemoveRPCFromBuffer(Action<byte[]> rpcHandlerMethod) => RemoveRPCFromBuffer(RPCUtils.ActionToString(rpcHandlerMethod));

    public void RemoveRPCFromBuffer(string rpcCode) => m_RpcBuffer.Remove(RPCUtils.NameToHash(rpcCode));

    /// <summary>
    /// Recieve rpc on object WITHOUT authority (Called from GSTransport).
    /// </summary>
    void IRPCReciever.RecieveRPCRemotely(int hashCode, byte[] data) => RecieveRPCRemotely(hashCode, data);
    void RecieveRPCRemotely(int hashCode, byte[] data)
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

    void SendGameObject(byte[] data, bool initializable, DeliveryIntent deliveryIntent) => m_InternalNetwork.SendGameObjectToOthers(
            InstanceId,
            PrefabId,
            IsSceneObject,
            data,
            initializable,
            deliveryIntent);

    public void DestroyViaNetwork()
    {
        if (!IsLocal)
            throw new Exception("Trying to destroy remote object, this is not allowed");

        m_InternalNetwork.DestroyGameObject(InstanceId);
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

    Dictionary<Type, GSInstanceBehaviour> m_CachedBehaviours = new Dictionary<Type, GSInstanceBehaviour>();
    public T GetBehaviour<T>() where T : GSInstanceBehaviour
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

    #region STATIC_HELPERS
    static ServiceProvider<GSNetwork> s_NetworkProvider = new ServiceProvider<GSNetwork>();

    public static GSInstanceIdentity Find(GSInstanceId instanceId) => 
        s_NetworkProvider.Instance.Realtime.FindNetworkObject(instanceId);

    public static T Find<T>(GSInstanceId instanceId) where T : GSInstanceBehaviour => 
        s_NetworkProvider.Instance.Realtime.FindNetworkObject<T>(instanceId);
    #endregion
}
