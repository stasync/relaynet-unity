using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameSparks.RT;
using UnityEngine;

public class GSInstanceFactory : IGSInstanceFactory
{
    IGSTransport m_Transport;
    IDictionary<GSInstanceId, GSInstanceIdentity> m_Objects;

    public GSInstanceFactory(IGSTransport transport)
    {
        m_Transport = transport;
        m_Objects = new Dictionary<GSInstanceId, GSInstanceIdentity>();
    }

    /// <summary>
    /// Someone connected and loaded game scene.
    /// </summary>
    /// <param name="peerId"></param>
    public void OnBecomesReady(GSPeerId peerId)
    {
        foreach (var o in m_Objects.Values.Where(o => o != null))
            o.OnBecomesReady(peerId);
    }

    /// <summary>
    /// Someone disconnected.
    /// </summary>
    /// <param name="peerId"></param>
    public void OnDisconnect(GSPeerId peerId)
    {
        DestroyPlayerObjects(peerId);
        foreach (var o in m_Objects.Values.Where(o => o != null))
            o.OnDisconnected(peerId);
    }

    /// <summary>
    /// Removes all ojects (except scene objects), owned by disconnected player, cleanup null references.
    /// </summary>
    /// <param name="peerId"></param>
    public void DestroyPlayerObjects(GSPeerId peerId)
    {
        var objects = m_Objects.Where(o => 
            (o.Value == null || 
            o.Value.Owner == null || 
            o.Value.Owner.PeerId == peerId) && 
            !o.Value.IsSceneObject).ToArray();//Copy to iterate

        foreach (var o in objects)
        {
            var obj = m_Objects[o.Key];
            if (obj == null)
                continue;
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
    public void DestroyObject(GSPeerId peerId, GSInstanceId instanceId)
    {
        if (!m_Objects.ContainsKey(instanceId))
            throw new Exception("Trying to delet object that not exists. Object id: " + instanceId.ToString());

        GSInstanceIdentity objectToDelete = null;
        if (m_Objects.TryGetValue(instanceId, out objectToDelete))
        {
            if(objectToDelete.Owner.PeerId != peerId)
                throw new Exception("Owner exception: Remote peer id: " + peerId + " Local object peer id: " + objectToDelete.Owner.PeerId);
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
    public GSInstanceIdentity CreateObject(IGSRTPlayer owner, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation)
    {
        return CreateObjectInternal(owner, new GSInstanceId(owner.PeerId), prefabId, isSceneObject, position, rotation, null); //Null data is ok.
    }

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
    public GSInstanceIdentity CreateOrUpdateObject(GSInstanceId instanceId, IGSRTPlayer owner, string prefabId, bool isSceneObject, RTData baseData)
    {
        GSInstanceIdentity identity = null;

        // Check if we dont have any created object with UID.
        if(!m_Objects.TryGetValue(instanceId, out identity))
            return CreateObjectInternal(owner, instanceId, prefabId, isSceneObject, Vector3.zero, Quaternion.identity, baseData);

        // Check if identity not initialized yet.
        // This is used for object, which placed in scene by default.
        if (!identity.Initialized)
            ((IIdentity)identity).Initialze(
                prefabId,
                isSceneObject,
                instanceId,
                owner,
                m_Transport,
                baseData);

        return ((IIdentity)identity).Deserialize(baseData, false, false);        
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
    GSInstanceIdentity CreateObjectInternal(IGSRTPlayer owner, GSInstanceId instanceId, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation, RTData baseData)
    {
        if (m_Objects.ContainsKey(instanceId))
            throw new Exception("Object with id " + instanceId + " already created");

        var gameObject = Resources.Load(prefabId) as GameObject;
        if (gameObject == null)
            throw new NullReferenceException();

        var newGameObject = UnityEngine.Object.Instantiate(gameObject, position, rotation);
        var identity = newGameObject.GetComponent<GSInstanceIdentity>();

        if (identity == null || owner == null)
        {
            UnityEngine.Object.Destroy(newGameObject);
            throw new ArgumentNullException();
        }
        
        m_Objects.Add(
            instanceId,
            InitializeIdentity(
                identity,
                prefabId, 
                isSceneObject,
                instanceId, 
                owner, 
                m_Transport, 
                baseData));

        return identity;
    }

    /// <summary>
    /// Register object, which is not created via main object creation pipline.
    /// See 'CreateObject' method.
    /// This is not initialize object, so object will be invalid, untill first update happens.
    /// </summary>
    /// <param name="sceneIdentity"></param>
    public void RegisterObject(GSSceneObjectIdentity sceneIdentity)
    {
        if (m_Objects.ContainsKey(sceneIdentity.SceneObjectId))
            return;

        m_Objects.Add(
            sceneIdentity.SceneObjectId, 
            sceneIdentity.Identity);

        if(m_Transport.MasterClientPeerId == m_Transport.LocalPeerId)
            InitializeIdentity(
                sceneIdentity.Identity,
                sceneIdentity.PrefabId,
                true,
                sceneIdentity.SceneObjectId,
                m_Transport.MasterPlayer,
                m_Transport,
                null);
    }

    GSInstanceIdentity InitializeIdentity(
        GSInstanceIdentity identity, 
        string prefabId, 
        bool isSceneObject, 
        GSInstanceId instanceId, 
        IGSRTPlayer owner, 
        IGSTransport transport, 
        RTData data)
    {
        return ((IIdentity)identity).Initialze(
                    prefabId,
                    isSceneObject,
                    instanceId,
                    owner,
                    transport,
                    data);
    }

    /// <summary>
    /// Find game object reference by id.
    /// Can be NULL.
    /// </summary>
    /// <param name="instanceId"></param>
    /// <returns></returns>
    public GSInstanceIdentity FindObject(GSInstanceId instanceId)
    {
        GSInstanceIdentity identity = null;
        return m_Objects.TryGetValue(instanceId, out identity) ? identity : null;
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
    public GSInstanceIdentity FindObjectOrCreateMock(IGSRTPlayer owner, GSInstanceId instanceId, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation)
    {
        var identity = FindObject(instanceId);
        return 
            identity == null ? 
            CreateObjectInternal(owner, instanceId, prefabId, isSceneObject, position, rotation, null) : 
            identity;
    }

    public IRPCReciever FindRpcReciever(GSInstanceId id)
    {
        return FindObject(id);
    }

    public IEnumerator<GSInstanceIdentity> GetEnumerator()
    {
        return m_Objects.Values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return m_Objects.Values.GetEnumerator();
    }
}
