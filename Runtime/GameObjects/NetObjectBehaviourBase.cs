using Core.DependencyInjection.Services;
using Core.DependencyInjection.Services.Events;
using UnityEngine;

[RequireComponent(typeof(NetObjectIdentity))]
public abstract class GSInstanceBehaviourBase : MonoBehaviour
{
    protected IBroadcasterService Broadcaster => m_Broadcaster.Instance;
    private readonly ServiceProvider<IBroadcasterService> m_Broadcaster = new ();

    protected GSNetwork Network => m_Network.Instance;
    private readonly ServiceProvider<GSNetwork> m_Network = new ();

    public NetObjectIdentity Identity =>
        m_Identity != null ?
        m_Identity :
        m_Identity = GetComponent<NetObjectIdentity>();

    private NetObjectIdentity m_Identity;

    protected virtual void OnDestroy() { }
}
