using System;
using Core.DependencyInjection.Services;
using Core.DependencyInjection.Services.Events;
using Core.Networking;
using UnityEngine;

[RequireComponent(typeof(NetObjectIdentity))]
public abstract class GSInstanceBehaviourBase : MonoBehaviour
{
    protected IBroadcasterService Broadcaster => m_Broadcaster.Instance;
    private readonly ServiceProvider<IBroadcasterService> m_Broadcaster = new ();

    private ClientSession _mBackingClientSession;

    [System.Obsolete]
    protected ClientSession Network
    {
        get
        {
            // TODO: JUST A HACK TO COMPILE THE PROJECT.
            if (_mBackingClientSession == null)
                _mBackingClientSession = FindAnyObjectByType<ClientSession>(FindObjectsInactive.Exclude);
            
            return _mBackingClientSession;
        }
    }

    public NetObjectIdentity Identity =>
        m_Identity != null ?
        m_Identity :
        m_Identity = GetComponent<NetObjectIdentity>();

    private NetObjectIdentity m_Identity;
    
    internal void OnPreInitializeInternal() => 
        OnPreInitialize();
    
    internal void OnPostInitializeInternal() => 
        OnPostInitialize();
    
    internal void OnInitializeViewDataInternal() => 
        OnInitializeViewData();
    
    internal void OnOwnerChangedInternal() => 
        OnOwnerChanged();
    
    internal void OnAuthorityUpdateInternal() => 
        OnAuthorityUpdate();
    
    internal void OnDisconnectedInternal(ClientUID clientUid) => 
        OnDisconnected(clientUid);
    
    internal void OnSerializeInternal(NetWriter writer)
    {
        OnSerialize(writer);

#if NETWORK_DEBUG
        writer.WriteInt32(GetType().FullName!.GetHashCode());
#endif
    }

    internal void OnDeserializeInternal(NetReader reader, bool firstTime)
    {
        OnDeserialize(reader, firstTime);
        
#if NETWORK_DEBUG
        var typeName = GetType().FullName;
        var hash = typeName!.GetHashCode();
        var serializedHash = reader.ReadInt32();

        if (!hash.Equals(serializedHash))
            Debug.LogError($"Potential serialization problem in '{typeName}' on '{gameObject.name}'.");
#endif
        
        OnDataDeserialized(firstTime);
    }
    
    internal void OnSoftDestroyRequestedLocallyInternal() => 
        OnSoftDestroyRequestedLocally();
    
    /// <summary>
    /// Use this instead of start, called after objet was deserialized first time.
    /// </summary>
    protected virtual void OnPreInitialize()
    {
    }

    /// <summary>
    /// Use this instead of start, called after objet was deserialized first time.
    /// </summary>
    protected virtual void OnPostInitialize()
    {
    }

    protected virtual void OnInitializeViewData()
    {
    }

    /// <summary>
    /// Calls when object's owner was changed locally or remotely.
    /// </summary>
    protected virtual void OnOwnerChanged()
    {
    }

    /// <summary>
    /// Called, when object needs to be updated, according to <see cref="NetObjectIdentity.m_UpdateRate"/>.
    /// </summary>
    protected virtual void OnAuthorityUpdate()
    {
    }

    /// <summary>
    /// Other client disconnected.
    /// </summary>
    protected virtual void OnDisconnected(ClientUID clientUid)
    {
    }
    
    /// <summary>
    /// Destroy method called locally.
    /// </summary>
    protected virtual void OnSoftDestroyRequestedLocally()
    {
    }

    protected virtual void OnSerialize(NetWriter writer)
    {
    }
    
    protected virtual void OnDeserialize(NetReader reader, bool firstTime)
    {
    }
    
    /// <summary>
    /// Called, when network data was deserialized.
    /// </summary>
    protected virtual void OnDataDeserialized(bool firstTime)
    {
    }

    protected virtual void OnDestroy()
    {
    }
}
