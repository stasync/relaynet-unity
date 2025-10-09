using UnityEngine;

[RequireComponent(typeof(NetObjectIdentity))]
public abstract class GSInstanceBehaviourBase : MonoBehaviour
{
    protected ILocator Locator => m_Locator.Instance;
    private readonly ServiceProvider<ILocator> m_Locator = new ();

    protected GSNetwork Network => m_Network.Instance;
    private readonly ServiceProvider<GSNetwork> m_Network = new ();

    public NetObjectIdentity Identity =>
        m_Identity != null ?
        m_Identity :
        m_Identity = GetComponent<NetObjectIdentity>();

    private NetObjectIdentity m_Identity;

    protected virtual void OnDestroy() { }
}
