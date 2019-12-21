using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GSInstanceFactory : IGSInstanceFactory
{
    public event Action<GSInstanceIdentity> NewObjectInitialized = delegate { };

    INetwork m_InternalNetwork;
    IDictionary<GSInstanceId, GSInstanceIdentity> m_Objects;
    ServiceProvider<IResourceTracker> m_ResourcesFactory;

    public GSInstanceFactory(INetwork network)
    {
        m_InternalNetwork = network;
        m_Objects = new Dictionary<GSInstanceId, GSInstanceIdentity>();
        m_ResourcesFactory = new ServiceProvider<IResourceTracker>();
    }

    /// <summary>
    /// Someone connected and loaded game scene.
    /// </summary>
    /// <param name="peerId"></param>
    public void OnBecomesReady(ClientUID peerId)
    {
        foreach (var o in m_Objects.Values.Where(o => o != null))
            o.OnBecomesReady(peerId);
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
        var objects = m_Objects.Where(o =>
            o.Value == null ||
            ((o.Value.Owner == null ||
            o.Value.Owner.ConnectionUID == peerId) && !o.Value.IsSceneObject)).ToArray();//Copy to iterate

        foreach (var o in objects)
        {
            var obj = m_Objects[o.Key];
            if (obj != null)
                UnityEngine.Object.Destroy(obj.gameObject);
            m_Objects.Remove(o.Key);
        }

    }

    /// <summary>
    /// Destroy all network objects and cleanup scene.
    /// </summary>
    public void DestroyNetworkObjects()
    {
        foreach (var o in m_Objects.Values)
        {
            if (o == null)
                continue;
            UnityEngine.Object.Destroy(o.gameObject);
        }
        m_Objects.Clear();
    }

    /// <summary>
    /// When anybody destroys object, this will be called on other clients.
    /// </summary>
    /// <param name="peerId"></param>
    /// <param name="instanceId"></param>
    public void DestroyObject(ClientUID peerId, GSInstanceId instanceId)
    {
        if (!m_Objects.ContainsKey(instanceId))
            throw new Exception("Trying to delet object that not exists. Object id: " + instanceId.ToString());

        if (m_Objects.TryGetValue(instanceId, out GSInstanceIdentity objectToDelete))
        {
            if (objectToDelete.Owner.ConnectionUID != peerId)
                throw new Exception("Owner exception: Remote peer id: " + peerId + " Local object peer id: " + objectToDelete.Owner.ConnectionUID);
            UnityEngine.Object.Destroy(objectToDelete.gameObject);
        }
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
    public GSInstanceIdentity CreateObject(INetRTPlayer owner, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation, object[] additionalData) =>
            CreateObjectInternal(owner, new GSInstanceId(owner.ConnectionUID), prefabId, isSceneObject, position, rotation, null, additionalData); //Null data and additional data is ok.

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
    public GSInstanceIdentity CreateOrUpdateObject(GSInstanceId instanceId, bool initializable, INetRTPlayer owner, string prefabId, bool isSceneObject, byte[] baseData)
    {
        // Check if we dont have any created object with UID.
        if (!m_Objects.TryGetValue(instanceId, out GSInstanceIdentity identity))
            return initializable ?
                CreateObjectInternal(owner, instanceId, prefabId, isSceneObject, Vector3.zero, Quaternion.identity, baseData, null) : 
                null;

        // Check if identity not initialized yet.
        // This is used for object, which placed in scene by default.
        if (!identity.Initialized && initializable)
            InitializeIdentity(
                identity, 
                prefabId, 
                isSceneObject, 
                instanceId, 
                owner, 
                m_InternalNetwork, 
                baseData,
                null);

        return identity.Initialized ? 
            (identity as IIdentity).Deserialize(baseData, false, false) : 
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
    GSInstanceIdentity CreateObjectInternal(
        INetRTPlayer owner, 
        GSInstanceId instanceId, 
        string prefabId, 
        bool isSceneObject, 
        Vector3 position, 
        Quaternion rotation, 
        byte[] baseData,
        object[] additionalData)
    {
        if (m_Objects.ContainsKey(instanceId))
            throw new Exception("Object with id " + instanceId + " already created");

        var gameObject = m_ResourcesFactory.Instance.Load<GameObject>(prefabId);

        if (gameObject == null)
            throw new NullReferenceException("Object with prefab id " + prefabId + " not founded in resources");

        var newGameObject = UnityEngine.Object.Instantiate(gameObject, position, rotation);
        var identity = newGameObject.GetComponent<GSInstanceIdentity>();
        var sceneIdentity = newGameObject.GetComponent<GSSceneObjectIdentity>();

        if (identity == null || owner == null || sceneIdentity != null)
        {
            UnityEngine.Object.Destroy(newGameObject);
            throw new InvalidOperationException();
        }
        
        m_Objects.Add(
            instanceId,
            InitializeIdentity(
                identity,
                prefabId, 
                isSceneObject,
                instanceId, 
                owner, 
                m_InternalNetwork, 
                baseData, 
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

        if(!sceneIdentity.Identity.Initialized && m_InternalNetwork.IsMasterClient)
            InitializeIdentity(
                sceneIdentity.Identity,
                "SceneObject", //We don't need to know scene object prefab id, because it's already in scene, but prefab id can't be null.
                true,
                sceneIdentity.SceneObjectId,
                m_InternalNetwork.MasterPlayer,
                m_InternalNetwork,
                null, 
                null);
    }

    GSInstanceIdentity InitializeIdentity(
        GSInstanceIdentity identity, 
        string prefabId, 
        bool isSceneObject, 
        GSInstanceId instanceId, 
        INetRTPlayer owner, 
        INetwork transport,
        byte[] data,
        object[] additionalData)
    {
        var initializable = identity as IIdentity;
        if (initializable == null)
            throw new InvalidCastException();

        initializable.Initialze(
                    prefabId,
                    isSceneObject,
                    instanceId,
                    owner,
                    transport,
                    data, 
                    additionalData);

        NewObjectInitialized(identity);
        return identity;
    }

    /// <summary>
    /// Find game object reference by id.
    /// Can be NULL.
    /// </summary>
    /// <param name="instanceId"></param>
    /// <returns></returns>
    public GSInstanceIdentity FindObject(GSInstanceId instanceId)
    {
        return m_Objects.TryGetValue(instanceId, out GSInstanceIdentity identity) ? identity : null;
    }

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
    public GSInstanceIdentity FindObjectOrCreateMock(INetRTPlayer owner, GSInstanceId instanceId, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation)
    {
        var identity = FindObject(instanceId);
        return 
            identity == null ? 
            CreateObjectInternal(owner, instanceId, prefabId, isSceneObject, position, rotation, null, null) : 
            identity;
    }

    /// <summary>
    /// If some object doesn't exist yet, we can wait for it;
    /// Similar to: <see cref="FindObjectOrCreateMock(INetRTPlayer, GSInstanceId, string, bool, Vector3, Quaternion)"/>
    /// But it doesn't create mock objects;
    /// </summary>
    /// <param name="instanceId"></param>
    /// <param name="result"></param>
    public void FindObjectOrWait(GSInstanceId instanceId, Action<GSInstanceIdentity> result)
    {
        var identity = FindObject(instanceId);
        if(identity != null)
        {
            result(identity);
            return;
        }
        NewObjectInitialized += OnNewObjectInitialized;

        void OnNewObjectInitialized(GSInstanceIdentity o)
        {
            if (o == null || o.InstanceId != instanceId)
                return;

            result(o);
            NewObjectInitialized -= OnNewObjectInitialized;
        }
    }

    public IRPCReciever FindRpcReciever(GSInstanceId id) => FindObject(id);

    public IEnumerator<GSInstanceIdentity> GetEnumerator() => m_Objects.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => m_Objects.Values.GetEnumerator();
}
