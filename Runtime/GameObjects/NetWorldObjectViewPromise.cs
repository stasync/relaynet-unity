using System;
using System.Collections.Generic;

namespace LROTech.Network.State
{
    public sealed class NetWorldObjectViewWaiter : IDisposable
    {
        private NetWorld m_World;
        private Dictionary<NetObjectUID, List<Action<NetObjectIdentity>>> m_NewObjectWaiters;

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

        private void OnNewObjectInitialized(NetObjectIdentity identity)
        {
            if (!m_NewObjectWaiters.TryGetValue(identity.InstanceId, out var waiters))
                return;

            foreach (var action in waiters)
                action(identity);

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
}