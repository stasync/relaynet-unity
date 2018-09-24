using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameSparks.RT;
using UnityEngine;

public enum BufferedRPCType
{
    Replace, 
    Add,
}

[DisallowMultipleComponent]
public sealed class GSInstanceIdentity : MonoBehaviour, IRPCReciever, IIdentity
{
    class AuthRpcBuffer : IRPCBuffer
    {
        class BufferedRPCData
        {
            public BufferedRPCData(string key, RTData data)
            {
                Key = key;
                Data = data;
            }

            public BufferedRPCData(GSReader reader)
            {
                Key = reader.ReadString();
                Data = reader.ReadData();
            }

            public GSWriter Serialize(GSWriter writer)
            {
                writer.Write(Key);
                writer.Write(Data);
                return writer;
            }

            public readonly string Key;
            public readonly RTData Data;
        }

        public bool IsDirty { get; private set; }

        IList<BufferedRPCData> m_Buffer;

        public AuthRpcBuffer()
        {
            m_Buffer = new List<BufferedRPCData>();
        }

        void IRPCBuffer.AddOrUpdate(string rpcCode, RTData data, BufferedRPCType type)
        {
            if(type == BufferedRPCType.Replace)
            {
                var rpcData = m_Buffer.FirstOrDefault(d => d.Key == rpcCode);
                if (rpcData != null)
                    m_Buffer.Remove(rpcData);
            }

            m_Buffer.Add(new BufferedRPCData(rpcCode, data));
            IsDirty = true;
        }

        void ISerializeable.Serialize(GSWriter writer)
        {
            writer.Write(IsDirty);
            if (!IsDirty)
                return;

            IsDirty = false;
            writer.WriteCollection(m_Buffer, (v, w) => v.Serialize(w));
        }

        void ISerializeable.Deserialize(GSReader reader, bool firstTime)
        {
            var wasDirty = reader.ReadBool();
            if (!wasDirty)
                return;

            m_Buffer = reader.ReadCollection(r => new BufferedRPCData(r)).ToList();
        }

        public IEnumerator<RTData> GetEnumerator()
        {
            return m_Buffer.Select(r => r.Data).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return m_Buffer.GetEnumerator();
        }

        void IRPCBuffer.Recieve(Action<string, RTData> reciever)
        {
            foreach (var rpc in m_Buffer)
                reciever(rpc.Key, rpc.Data);
        }

        public void SetDirty()
        {
            IsDirty = true;
        }

        public override string ToString()
        {
            return m_Buffer.ToString();
        }

        public void Clear()
        {
            m_Buffer.Clear();
            IsDirty = true;
        }
    }

    [SerializeField]
    GameSparksRT.DeliveryIntent m_DeliveryIntent = GameSparksRT.DeliveryIntent.RELIABLE;

    [SerializeField, Range(0.01f, 1f)]
    float m_SendRate = 0.1f;
    float m_NextSendEventTime;

    public GSInstanceId InstanceId { get; private set; }

    IRPCBuffer m_RpcBuffer = new AuthRpcBuffer();

    public bool IsMasterClient
    {
        get
        {
            return
                m_Transport != null && 
                m_Transport.MasterPlayer == Owner;
        }
    }

    public bool IsLocal
    {
        get
        {
            return 
                m_Transport != null &&
                m_Transport.LocalPlayer == Owner;
        }
    }

    public bool IsDirty
    {
        get { return m_Observables.Any(o => o.IsDirty) || m_RpcBuffer.IsDirty; }
    }

    /// <summary>
    /// Readonly owner reference
    /// </summary>
    IGSRTPlayer m_Owner;
    public IGSRTPlayer Owner
    {
        get { return m_Owner; }
        private set
        {
            m_Owner = value;
            OnOwnerChanged();
        }
    }

    /// <summary>
    /// Readonly transport reference
    /// </summary>
    IGSTransport m_Transport;

    /// <summary>
    /// Object prefab id
    /// </summary>
    public string PrefabId { get; private set; }
    public bool IsSceneObject { get; private set; }
    public bool Initialized { get; private set; }

    IEnumerable<IGSInstanceObservable> m_Observables;

    /// <summary>
    /// Initialization. Called from GSInstanceFactory. 
    /// Shouldn't be called from top-level API.
    /// </summary>
    /// <param name="prefabId"></param>
    /// <param name="instanceId"></param>
    /// <param name="owner"></param>
    /// <param name="transport"></param>
    /// <param name="data"></param>
    /// <returns></returns>
    GSInstanceIdentity IIdentity.Initialze(
        string prefabId, 
        bool isSceneObject,
        GSInstanceId instanceId,
        IGSRTPlayer owner, 
        IGSTransport transport, 
        RTData data,
        RTData additionalData)
    {
        if (Initialized)
            throw new Exception("Object already initialized");

        if (owner == null)
            throw new ArgumentNullException("owner");

        m_Observables = gameObject.GetComponents<IGSInstanceObservable>();

        InstanceId = instanceId;
        Owner = owner;
        PrefabId = prefabId;
        IsSceneObject = isSceneObject;
        m_Transport = transport;
        /*
         * Skip deserialization if it calls on local object.
         * Object can be created with null data (see GSInstanceFactory.CreateObject). Just skip deserialization here.
         */
        if (!IsLocal && data != null)
            ((IIdentity)this).Deserialize(data, true, true); //Force true to allow deseriazation before object initialized.

        if(additionalData != null)
            foreach (var o in m_Observables)
                o.OnCreatedWithAdditionalData(additionalData);

        foreach (var o in m_Observables)
            o.OnInitialze();

        //Make sure it will send valid state after initialization.
        //Is local check inside this method.
        ForceSerialize();

        Debug.LogWarning("New network object: Is Local: " + IsLocal + ", Owner: " + Owner);
        Initialized = true;

        //Invokes buffered rpc, must be called after deserialization done.
        //Only for remote objects.
        if (!IsLocal)
            m_RpcBuffer.Recieve(((IRPCReciever)this).RecieveRpcWithoutAuthority);

        return this;
    }

    /// <summary>
    /// Other client connected and loaded game scene, set all component as dirty to send valid object state to other clients.
    /// </summary>
    public void OnBecomesReady(GSPeerId peerId)
    {
        if (!IsLocal)
            return;

        ForceSerialize();
    }

    /// <summary>
    /// Other client disconnected. This method calls before actual player reference removed, so here we have valid owner.
    /// </summary>
    public void OnDisconnected(GSPeerId peerId)
    {
        //Is object owned by disconnected player and a scene object, trying to change owner.
        if (IsSceneObject && peerId == Owner.PeerId)
        {
            //Is local client becomes master client, set object's owner and sync it.
            if (m_Transport.MasterClientPeerId == m_Transport.LocalPeerId)
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
        Debug.LogWarningFormat("{0} object with ID: {1} owner changed, now owner is {2}",
            gameObject.name, InstanceId, Owner.ToString());
        foreach (var b in m_Observables)
            b.OnOwnerChanged();
    }

    /// <summary>
    /// Ownership managmet, anyone can change scene object owner.
    /// </summary>
    public void OwnObejct()
    {
        if (!IsSceneObject)
            throw new Exception("You can manage owners only for scene objects");

        SetOwner(m_Transport.LocalPlayer, true);
    }

    /// <summary>
    /// Change object owner.
    /// </summary>
    /// <param name="owner"></param>
    public void SetOwner(IGSRTPlayer owner, bool send = false)
    {
        if (owner == null)
            throw new ArgumentNullException("owner");

        Owner = owner;
        //Send changes
        if(send)
            m_Transport.SetOwner(InstanceId, Owner.PeerId);
    }

    /// <summary>
    /// Sets all behaviours dirty, if object is local.
    /// </summary>
    void SetDirty()
    {
        if (!IsLocal)
            return;

        m_RpcBuffer.SetDirty();

        foreach (var o in m_Observables)
            o.SetDirty();
    }

    void FixedUpdate()
    {
        if (!IsLocal || !IsDirty || Time.fixedUnscaledTime < m_NextSendEventTime)
            return;

        TrySerialize();
    }

    void ForceSerialize()
    {
        if (!IsLocal)
            return;

        SetDirty();
        TrySerialize();
    }

    void TrySerialize()
    {
        if (!IsLocal)
            throw new Exception("Trying to serialize remote player object, this is not allowed");

        if (!Initialized)
            return;

        var w = new GSWriter()
            .Write(InstanceId)
            .Write(PrefabId)
            .Write(IsSceneObject);

        m_RpcBuffer.Serialize(w);

        foreach (var b in m_Observables)
            b.Serialize(w);

        SendGameObject(w.AsData());

        m_NextSendEventTime = Time.fixedUnscaledTime + m_SendRate;
    }

    /// <summary>
    /// Deserizlize whole object
    /// </summary>
    /// <param name="data"></param>
    /// <param name="force"></param>
    /// <param name="firstTime"></param>
    /// <returns></returns>
    GSInstanceIdentity IIdentity.Deserialize(RTData data, bool force, bool firstTime)
    {
        if (IsLocal)
        {
            // TODO: Need to deserualized this data somehow, or change ownership changing pipeline.
            Debug.LogError("Trying to deserialize local player object, this is not allowed. Maybe owner was changed before. NOTE: This is normal, if owner was chaged bofore.");
            return this;
        }

        if (!Initialized && !force)
            return null;

        if (data == null)
            return this;

        var r = new GSReader(data);

        //Check instance id
        var id = r.ReadInt();
        if (id != InstanceId)
            throw new InvalidOperationException(string.Format("Object id is:{0}, but recieved id is {1}", InstanceId, id));

        var prefabId = r.ReadString();
        if(prefabId != PrefabId)
            throw new InvalidOperationException(string.Format("Object prefab id is:{0}, but recieved preafab id is {1}", PrefabId, prefabId));

        var isSceneObject = r.ReadBool();
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
    private IDictionary<string, Action<RTData>> m_RPCHandlers = new Dictionary<string, Action<RTData>>();

    /// <summary>
    /// Register handler to recieve RPC (Better to call in OnEnable).
    /// </summary>
    /// <param name="rpcCode"></param>
    /// <param name="handler"></param>
    public void RegisterRPCHandler(string rpcCode, Action<RTData> handler)
    {
        if (m_RPCHandlers.ContainsKey(rpcCode))
            m_RPCHandlers[rpcCode] += handler;
        else
            m_RPCHandlers.Add(rpcCode, handler);
    }

    /// <summary>
    /// Unregiter handler (Better to call in OnDisable).
    /// For internal usage only. Use just RegisterRPCHandler in 'OnInit' method.
    /// </summary>
    /// <param name="rpcCode"></param>
    /// <param name="handler"></param>
    void UnregisterRPCHandler(string rpcCode, Action<RTData> handler)
    {
        if (m_RPCHandlers.ContainsKey(rpcCode))
            m_RPCHandlers[rpcCode] -= handler;
    }

    /// <summary>
    /// RPC, invokes locally and sends to ALL clients, who HAS NO AUTHORITY on this object.
    /// </summary>
    public void AuthRPC(
        string rpcCode, 
        RTData data, 
        GameSparksRT.DeliveryIntent deliveryIntent = GameSparksRT.DeliveryIntent.RELIABLE, 
        bool invokeLocaly = true)
    {
        if (!IsLocal)
            throw new Exception("Trying to send AuthRPC on remote object's behaviour, this is not allowed. Use NonAuthRPC if you not sure about object's authority");

        //Invoke handler locally.
        if(invokeLocaly)
            InvokeRpcHandler(rpcCode, data);

        //Send ot others.
        m_Transport.RPC(RpcType.Auth, InstanceId, rpcCode, data, deliveryIntent);
    }

    /// <summary>
    /// Same as AuthRPC, but buffered.
    /// Only works via UNRELIABLE_SEQUENCED delivery.
    /// </summary>
    public void AuthRpcBuffered(
        string rpcCode, 
        RTData data, 
        BufferedRPCType rpcType = BufferedRPCType.Replace,
        bool invokeLocaly = true)
    {
        //Call regular AuthRPC
        AuthRPC(rpcCode, data, GameSparksRT.DeliveryIntent.RELIABLE, invokeLocaly);

        //Add rpc to buffer, rpc buffer becomes dirty after this.
        m_RpcBuffer.AddOrUpdate(rpcCode, data, rpcType);
    }

    /// <summary>
    /// RPC variation, sends RPC to SINGLE client, who HAS AUTHORITY on this object.
    /// IF we have authority, just invokes locally without sending data.
    /// </summary>
    public void NonAuthRPC(string rpcCode, RTData data, GameSparksRT.DeliveryIntent deliveryIntent = GameSparksRT.DeliveryIntent.RELIABLE)
    {
        //If we have authority on this object, don't need to send this kind of RPC.
        if (IsLocal)
            InvokeRpcHandler(rpcCode, data);
        else
            m_Transport.RPC(RpcType.NonAuth, InstanceId, rpcCode, data, deliveryIntent);
    }

    /// <summary>
    /// RPC variation, sends RPC to MASTER client.
    /// It does not matter whether MASTER client is a owner of this object or not.
    /// IF we are master client, just invokes locally without sending data.
    /// </summary>
    public void MasterRPC(string rpcCode, RTData data, GameSparksRT.DeliveryIntent deliveryIntent = GameSparksRT.DeliveryIntent.RELIABLE)
    {
        if(IsMasterClient)
            InvokeRpcHandler(rpcCode, data);
        else
            m_Transport.RPC(RpcType.Master, InstanceId, rpcCode, data, deliveryIntent);
    }

    /// <summary>
    /// Clear current rpc buffer and sets buffer dirty.
    /// </summary>
    public void ClearRPCBuffer()
    {
        m_RpcBuffer.Clear();
    }

    /// <summary>
    /// Recieve rpc on object WITHOUT authority (Called from GSTransport).
    /// </summary>
    void IRPCReciever.RecieveRpcWithoutAuthority(string rpcCode, RTData data)
    {
        if (IsLocal)
            throw new Exception("Trying to recieve NO-AUTHORITY RPC on local object behaviour, this is not allowed");

        InvokeRpcHandler(rpcCode, data);
    }

    /// <summary>
    /// Recieve rpc on object with authority. (Called from GSTransport).
    /// </summary>
    void IRPCReciever.RecieveRpcWithAuthority(string rpcCode, RTData data)
    {
        if (!IsLocal)
            throw new Exception("Trying to recieve AUTHORITY RPC on remote object behaviour, this is not allowed");

        InvokeRpcHandler(rpcCode, data);
    }

    /// <summary>
    /// Recieve rpc on master clent. (Called from GSTransport).
    /// </summary>
    void IRPCReciever.RecieveMasterClientRpc(string rpcCode, RTData data)
    {
        if (!IsMasterClient)
            throw new Exception("Trying to recieve MASTER CLIENT RPC on object behaviour, which is not owner by master client, this is not allowed");

        InvokeRpcHandler(rpcCode, data);
    }

    void InvokeRpcHandler(string rpcCode, RTData data)
    {
        if (m_RPCHandlers.ContainsKey(rpcCode))
            m_RPCHandlers[rpcCode].Invoke(data);
        else
            Debug.LogErrorFormat("Handler for RPC with code {0} doesn't registred on object {1}", rpcCode, gameObject.name);
    }

    void SendGameObject(RTData data)
    {
        m_Transport.SendGameObjectToOthers(
            InstanceId, 
            PrefabId, 
            IsSceneObject,
            data, 
            m_DeliveryIntent);
    }

    public void DestroyViaNetwork()
    {
        if (!IsLocal)
            throw new Exception("Trying to destroy remote object, this is not allowed");

        m_Transport.DestroyGameObject(InstanceId);
    }

    #if UNITY_EDITOR
    private void OnDrawGizmos()
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

    #region HELPERS
    static GSRealtime s_Realtime;

    [Locator.Initialized]
    static void Init(ILocator locator)
    {
        s_Realtime = locator.Get<GSNetwork>().Realtime;
    }

    public static GSInstanceIdentity Find(GSInstanceId instanceId)
    {
        return s_Realtime.FindNetworkObject(instanceId);
    }

    public static T Find<T>(GSInstanceId instanceId) where T : GSInstanceBehaviour
    {
        return s_Realtime.FindNetworkObject<T>(instanceId);
    }
    #endregion
}
