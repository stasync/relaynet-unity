using Core.DependencyInjection.Events;
using UnityEngine;

[RequireComponent(typeof(NetObjectIdentity))]
public abstract class GSInstanceBehaviourBase : MonoBehaviour
{
    protected IBroadcaster Broadcaster => m_Broadcaster.Instance;
    private readonly Core.Main.IOC.IOCProvider<IBroadcaster> m_Broadcaster = new ();

    protected GSNetwork Network => m_Network.Instance;
    private readonly Core.Main.IOC.IOCProvider<GSNetwork> m_Network = new ();

    public NetObjectIdentity Identity =>
        m_Identity != null ?
        m_Identity :
        m_Identity = GetComponent<NetObjectIdentity>();

    private NetObjectIdentity m_Identity;

    protected virtual void OnDestroy() { }
}
