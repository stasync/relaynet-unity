using Core.DependencyInjection.Services;
using Core.DependencyInjection.Services.Events;
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

    protected virtual void OnDestroy() { }
}
