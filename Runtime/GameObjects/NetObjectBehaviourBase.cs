using Core.DependencyInjection.Services;
using Core.DependencyInjection.Services.Events;
using UnityEngine;

[RequireComponent(typeof(NetObjectIdentity))]
public abstract class GSInstanceBehaviourBase : MonoBehaviour
{
    protected IBroadcasterService Broadcaster => m_Broadcaster.Instance;
    private readonly ServiceProvider<IBroadcasterService> m_Broadcaster = new ();

    private GSClient m_BackingClient;

    [System.Obsolete]
    protected GSClient Network
    {
        get
        {
            // TODO: JUST A HACK TO COMPILE THE PROJECT.
            if (m_BackingClient == null)
                m_BackingClient = FindAnyObjectByType<GSClient>(FindObjectsInactive.Exclude);
            
            return m_BackingClient;
        }
    }

    public NetObjectIdentity Identity =>
        m_Identity != null ?
        m_Identity :
        m_Identity = GetComponent<NetObjectIdentity>();

    private NetObjectIdentity m_Identity;

    protected virtual void OnDestroy() { }
}
