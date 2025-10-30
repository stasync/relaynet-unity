using System;
using System.Collections.Generic;
using LROTech.Network.StateManagement;

namespace LROTech.Network.GameObjects
{
    public sealed class NetWorldObjectViewWaiter : IDisposable
    {
        private NetWorld _world;
        private Dictionary<NetObjectUid, List<Action<NetObjectIdentity>>> _newObjectWaiters;

        public NetWorldObjectViewWaiter(NetWorld world)
        {
            _world = world;
            _newObjectWaiters = new Dictionary<NetObjectUid, List<Action<NetObjectIdentity>>>();

            _world.View.NewObjectInitialized += OnNewObjectInitialized;
        }

        public void GetOrWaitFor(NetObjectUid uid, Action<NetObjectIdentity> a)
        {
            var existedView = _world.FindObjectView(uid);
            if (existedView != null)
            {
                a(existedView);
                return;
            }

            if (!_newObjectWaiters.TryGetValue(uid, out var waiters))
            {
                waiters = new List<Action<NetObjectIdentity>>();
                _newObjectWaiters.Add(uid, waiters);
            }

            waiters.Add(a);
        }

        private void OnNewObjectInitialized(NetObjectIdentity identity)
        {
            if (!_newObjectWaiters.TryGetValue(identity.InstanceId, out var waiters))
                return;

            foreach (var action in waiters)
                action(identity);

            waiters.Clear();
        }

        public void Dispose()
        {
            if (_world.IsValid && _world.View != null)
                _world.View.NewObjectInitialized -= OnNewObjectInitialized;

            _world = null;
            _newObjectWaiters.Clear();
            _newObjectWaiters = null;
        }
    }
}