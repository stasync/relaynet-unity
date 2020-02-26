using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
public class NetWorld : IJSONSerializable
{
    public NetWorldUID UID { get; private set; }

    public event Action<NetObjectIdentity> NewObjectInitialized = delegate { };

    public IEnumerable<NetObjectIdentity> ObjectsCollection => Objects.Values;
    public IReadOnlyDictionary<NetObjectUID, NetObjectIdentity> Objects => m_Objects;
    Dictionary<NetObjectUID, NetObjectIdentity> m_Objects;
    HashSet<NetObjectUID> m_RemoteInitializationQueue;
    Queue<Action> m_UpdateActionQueue;

    NetWorldView m_WorldView;
    INetwork m_InternalNetwork;
    NetObjectFactory m_ObjectFactory;
    float m_LastUpdateTime;

    const float m_MinUpdateInteraval = 0.1f; //10 times per second;

    public NetWorld(INetwork network)
    {
        if (m_WorldView != null)
            throw new InvalidOperationException($"{nameof(m_WorldView)} alreay created");

        m_InternalNetwork = network ?? throw new ArgumentNullException(nameof(network));

        m_WorldView = new GameObject("NetworkWorldRoot")
            .AddComponent<NetWorldView>();
        UnityEngine.Object.DontDestroyOnLoad(m_WorldView.gameObject);

        m_ObjectFactory = new NetObjectFactory();
        m_Objects = new Dictionary<NetObjectUID, NetObjectIdentity>();
        m_RemoteInitializationQueue = new HashSet<NetObjectUID>();
        m_UpdateActionQueue = new Queue<Action>();

        UID = NetWorldUID.Allocate();
    }

    public void TryUpdate(float deltaTime)
    {
        var worldDelta = Time.fixedUnscaledTime - m_LastUpdateTime;
        if (worldDelta < m_MinUpdateInteraval)
            return;

        m_LastUpdateTime = Time.fixedUnscaledTime;
        m_WorldView.OnUpdate(worldDelta);

        //Execute commands;
        while (m_UpdateActionQueue.Count > 0)
            m_UpdateActionQueue.Dequeue()();

        //Update objects;
        var keys = m_Objects.Keys.ToArray(); // Copy to iterate;
        foreach (var key in keys)
        {
            var netObject = m_Objects[key];
            if (!netObject.IsLocal) //Skip remote objects;
                continue;

            netObject.OnUpdate(worldDelta);

            var deliveryIntent = DeliveryIntent.Unreliable;
            var requireRemoteInit = m_RemoteInitializationQueue.Remove(netObject.InstanceId);
            if (requireRemoteInit)
            {
                deliveryIntent = DeliveryIntent.Reliable;
                netObject.SetDirtyIfLocal();
            }

            if(netObject.IsDirty)
                m_InternalNetwork.SendGameObjectStateToOthers(
                    netObject.InstanceId, 
                    netObject.PrefabId, 
                    netObject.IsSceneObject, netObject.Serialize(), 
                    requireRemoteInit, 
                    deliveryIntent);
        }

        m_RemoteInitializationQueue.Clear();
    }

    public void ScheduleOnUpdate(Action a) => m_UpdateActionQueue.Enqueue(a);

    /// <summary>
    /// Destroy all network objects and cleanup scene.
    /// </summary>
    public void Destroy()
    {
        NewObjectInitialized = null;

        m_Objects.Clear();
        m_Objects = null;

        m_RemoteInitializationQueue.Clear();
        m_RemoteInitializationQueue = null;

        m_UpdateActionQueue.Clear();
        m_UpdateActionQueue = null;

        UnityEngine.Object.Destroy(m_WorldView); //Destroy all net object;
        m_WorldView = null;

        m_InternalNetwork = null;
        m_ObjectFactory = null;
    }

    public void SetLocalObjectsDirty()
    {
        foreach (var o in m_Objects.Values)
            if (o != null && o.IsLocal)
                o.SetDirtyIfLocal();
    }

    void ScheduleRemoteInitForLocalObject(NetObjectIdentity netObject)
    {
        if (netObject == null)
            throw new ArgumentNullException(nameof(netObject));

        if (!netObject.IsLocal)
            return;

        if (!m_Objects.ContainsKey(netObject.InstanceId))
            Debug.LogError($"Instance with id {netObject.InstanceId} doesn't presented in network world");

        if (!m_RemoteInitializationQueue.Add(netObject.InstanceId))
            Debug.LogError($"Initialization update for object {netObject.InstanceId} already scheduled");
    }

    /// <summary>
    /// Someone connected and loaded game scene, schedule local objects for reliable update update;
    /// </summary>
    /// <param name="peerId"></param>
    public void OnBecomesReady(ClientUID peerId)
    {
        foreach(var obj in ObjectsCollection)
            ScheduleRemoteInitForLocalObject(obj);
    }

    /// <summary>
    /// Someone disconnected.
    /// </summary>
    /// <param name="peerId"></param>
    public void OnDisconnect(ClientUID peerId)
    {
        DestroyPlayerObjects(peerId);
        foreach (var o in m_Objects.Values.Where(o => o != null))
            o.OnDisconnected(peerId);
    }

    /// <summary>
    /// Removes all ojects (except scene objects), owned by disconnected player, cleanup null references.
    /// </summary>
    /// <param name="peerId"></param>
    public void DestroyPlayerObjects(ClientUID peerId)
    {
        var keys = m_Objects.Keys.ToArray(); //Copy to iterate
        foreach (var k in keys)
        {
            var obj = m_Objects[k];
            if(obj == null || (obj.Owner == null || obj.Owner.ConnectionUID == peerId) && !obj.IsSceneObject)
            {
                UnityEngine.Object.Destroy(obj.gameObject);
                m_Objects.Remove(k);
            }
        }
    }

    /// <summary>
    /// When anybody destroys object, this will be called on other clients.
    /// </summary>
    /// <param name="peerId"></param>
    /// <param name="instanceId"></param>
    public void DestroyObject(ClientUID peerId, NetObjectUID instanceId)
    {
        if (m_Objects.TryGetValue(instanceId, out NetObjectIdentity objectToDelete))
        {
            if (objectToDelete.Owner.ConnectionUID != peerId)
                throw new Exception("Owner exception: Remote peer id: " + peerId + " Local object peer id: " + objectToDelete.Owner.ConnectionUID);
            UnityEngine.Object.Destroy(objectToDelete.gameObject);
        }
        else
            throw new Exception("Trying to delet object that not exists. Object id: " + instanceId.ToString());

        m_Objects.Remove(instanceId);
    }

    /// <summary>
    /// Create object with position and rotation input.
    /// Sould be called on local client to create object across network.
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="prefabId"></param>
    /// <param name="position"></param>
    /// <param name="rotation"></param>
    /// <param name="baseData"></param>
    /// <returns></returns>
    public NetObjectIdentity CreateObject(INetRTPlayer owner, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation, object[] additionalData) =>
            CreateObjectInternal(
                ObjectCreateUpdateSnapshot.Create(owner.ConnectionUID, prefabId, isSceneObject, null),
                owner,
                position,
                rotation, 
                additionalData); //Null data and additional data is ok.

    /// <summary>
    /// Create object from remote client update, without position and rotation, but it can be stored in data.
    /// Called by GSTransport to syncronize object.
    /// We create object with zero position and roation. We expect, that position and rotation stored in 'baseData'.
    /// </summary>
    /// <param name="instanceId"></param>
    /// <param name="owner"></param>
    /// <param name="prefabId"></param>
    /// <param name="baseData"></param>
    /// <returns></returns>
    public NetObjectIdentity CreateOrUpdateObject(ObjectCreateUpdateSnapshot snapshot, bool initializable, INetRTPlayer owner)
    {
        // Check if we dont have any created object with UID.
        if (!m_Objects.TryGetValue(snapshot.ObjectUID, out NetObjectIdentity identity))
            return initializable ?
                CreateObjectInternal(snapshot, owner, Vector3.zero, Quaternion.identity, null) : 
                null;

        // Check if identity not initialized yet.
        // This is used for object, which placed in scene by default.
        if (!identity.Initialized && initializable)
            InitializeIdentity(
                identity,
                snapshot,
                owner, 
                m_InternalNetwork, 
                null);

        return identity.Initialized ? 
            identity.Deserialize(snapshot.Data, false, false) : 
            null;    
    }

    /// <summary>
    /// Internal object creation pipeline. Null baseData is ok.
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="instanceId"></param>
    /// <param name="prefabId"></param>
    /// <param name="position"></param>
    /// <param name="rotation"></param>
    /// <param name="baseData"></param>
    /// <returns></returns>
    NetObjectIdentity CreateObjectInternal(ObjectCreateUpdateSnapshot snapshot, INetRTPlayer owner, Vector3 position, Quaternion rotation, object[] additionalData)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));

        if (m_Objects.ContainsKey(snapshot.ObjectUID))
            throw new InvalidOperationException($"Object with id {snapshot.ObjectUID} already exists");

        var identity = m_ObjectFactory.Create(snapshot.PrefabId, position, rotation);
        m_Objects.Add(
            snapshot.ObjectUID,
            InitializeIdentity(
                identity,
                snapshot, 
                owner, 
                m_InternalNetwork, 
                additionalData));

        return identity;
    }

    /// <summary>
    /// Register object, which is not created via main object creation pipline.
    /// See 'CreateObject' method.
    /// This is not initialize object, so object will be invalid, untill first update happens.
    /// </summary>
    /// <param name="sceneIdentity"></param>
    public void TryRegisterOrInitializeSceneObjectIdentity(GSSceneObjectIdentity sceneIdentity)
    {
        if (m_Objects.ContainsKey(sceneIdentity.SceneObjectId))
        {
#if NETWORK_DEBUG
            Debug.LogErrorFormat("Trying to register scene identity {0}: but object with id {1} already registered",
                sceneIdentity.gameObject.name,
                sceneIdentity.SceneObjectId);
#endif
        }
        else
            m_Objects.Add(
                sceneIdentity.SceneObjectId, 
                sceneIdentity.Identity);

        //We don't need to know scene object prefab id, because it's already in scene, but prefab id can't be null.
        if (!sceneIdentity.Identity.Initialized && m_InternalNetwork.IsMasterClient)
            InitializeIdentity(
                sceneIdentity.Identity, 
                ObjectCreateUpdateSnapshot.Create(sceneIdentity.SceneObjectId, m_InternalNetwork.MasterClientConnection, "SceneObject", true, null),
                m_InternalNetwork.MasterPlayer,
                m_InternalNetwork,
                null);
    }

    NetObjectIdentity InitializeIdentity(NetObjectIdentity identity, ObjectCreateUpdateSnapshot snapshot, INetRTPlayer owner, INetwork transport, object[] additionalData)
    {
        //Put to world view;
        UnityEngine.Object.DontDestroyOnLoad(identity.gameObject);
        identity.transform.SetParent(m_WorldView.transform);

        //Init;
        identity.Initialze(
                    snapshot.PrefabId,
                    snapshot.IsSceneObject,
                    snapshot.ObjectUID,
                    owner,
                    transport,
                    snapshot.Data, 
                    additionalData);

        //Try schedule remote init;
        ScheduleRemoteInitForLocalObject(identity);

        NewObjectInitialized(identity);
        return identity;
    }

    /// <summary>
    /// Find game object reference by id.
    /// Can be NULL.
    /// </summary>
    /// <param name="instanceId"></param>
    /// <returns></returns>
    public NetObjectIdentity FindObject(NetObjectUID instanceId) =>
        m_Objects.TryGetValue(instanceId, out NetObjectIdentity identity) ? identity : null;

    /// <summary>
    /// Get object before in was actually created by transport system.
    /// Note that this object will not be in valid state, until update happens.
    /// Use in only if its highly nessesery.
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="instanceId"></param>
    /// <param name="prefabId"></param>
    /// <param name="isSceneObject"></param>
    /// <param name="position"></param>
    /// <param name="rotation"></param>
    /// <returns></returns>
    public NetObjectIdentity FindObjectOrCreateMock(INetRTPlayer owner, NetObjectUID instanceId, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation)
    {
        var identity = FindObject(instanceId);
        return 
            identity == null ? 
            CreateObjectInternal(ObjectCreateUpdateSnapshot.Create(instanceId, owner.ConnectionUID, prefabId, isSceneObject, null), owner, position, rotation, null) : 
            identity;
    }

    /// <summary>
    /// If some object doesn't exist yet, we can wait for it;
    /// Similar to: <see cref="FindObjectOrCreateMock(INetRTPlayer, NetObjectUID, string, bool, Vector3, Quaternion)"/>
    /// But it doesn't create mock objects;
    /// </summary>
    /// <param name="instanceId"></param>
    /// <param name="result"></param>
    public void FindObjectOrWait(NetObjectUID instanceId, Action<NetObjectIdentity> result)
    {
        var identity = FindObject(instanceId);
        if(identity != null)
        {
            result(identity);
            return;
        }
        NewObjectInitialized += OnNewObjectInitialized;

        void OnNewObjectInitialized(NetObjectIdentity o)
        {
            if (o == null || o.InstanceId != instanceId)
                return;

            result(o);
            NewObjectInitialized -= OnNewObjectInitialized;
        }
    }

    public IRPCReciever FindRpcReciever(NetObjectUID id) => FindObject(id);

    public string ToJSONProperty() => RealtimeJSONUtils.ToJSONCollection(m_Objects.Values.Select(o => o.ToJSONProperty()).ToArray());
}
