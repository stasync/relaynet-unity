using UnityEngine;

[RequireComponent(typeof(NetObjectIdentity))]
public abstract class GSInstanceBehaviourBase : MonoBehaviour
{
    protected ILocator Locator => m_Locator.Instance;
    ServiceProvider<ILocator> m_Locator = new ServiceProvider<ILocator>();

    protected GSNetwork Network => m_Network.Instance;
    ServiceProvider<GSNetwork> m_Network = new ServiceProvider<GSNetwork>();

    public NetObjectIdentity Identity =>
        m_Identity != null ?
        m_Identity :
        m_Identity = GetComponent<NetObjectIdentity>();

    NetObjectIdentity m_Identity;
}
