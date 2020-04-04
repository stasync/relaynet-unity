using System;
using System.Collections.Generic;

public class NetWorldObjectViewWaiter : IDisposable
{
    NetWorld m_World;
    Dictionary<NetObjectUID, List<Action<NetObjectIdentity>>> m_NewObjectWaiters;

    public NetWorldObjectViewWaiter(NetWorld world)
    {
        m_World = world;
        m_NewObjectWaiters = new Dictionary<NetObjectUID, List<Action<NetObjectIdentity>>>();

        m_World.View.NewObjectInitialized += OnNewObjectInitialized;
    }

    public void GetOrWaitFor(NetObjectUID uid, Action<NetObjectIdentity> a)
    {
        var existedView = m_World.FindObjectView(uid);
        if (existedView != null)
        {
            a(existedView);
            return;
        }

        if (!m_NewObjectWaiters.TryGetValue(uid, out var waiters))
        {
            waiters = new List<Action<NetObjectIdentity>>();
            m_NewObjectWaiters.Add(uid, waiters);
        }
        waiters.Add(a);
    }

    void OnNewObjectInitialized(NetObjectIdentity identity)
    {
        if (!m_NewObjectWaiters.TryGetValue(identity.InstanceId, out var waiters))
            return;

        foreach (var w in waiters)
            w(identity);

        waiters.Clear();
    }

    public void Dispose()
    {
        if (m_World.IsValid && m_World.View != null)
            m_World.View.NewObjectInitialized -= OnNewObjectInitialized;

        m_World = null;
        m_NewObjectWaiters.Clear();
        m_NewObjectWaiters = null;
    }
}
