using System;
using System.Collections.Generic;
using Keel.RelayNet.StateManagement;

namespace Keel.RelayNet.GameObjects
{
    public sealed class NetWorldObjectViewPromise : IDisposable
    {
        private NetWorld _world;
        private Dictionary<NetObjectUid, List<Action<NetObjectIdentity>>> _instances = new();

        public NetWorldObjectViewPromise(NetWorld world)
        {
            _world = world;
            _world.View.NewObjectInitialized += OnNewObjectInitialized;
        }

        public void GetOrWaitFor(NetObjectUid uid, Action<NetObjectIdentity> callback)
        {
            var existedView = _world.GetObjectView(uid);
            if (existedView != null)
            {
                callback(existedView);
                return;
            }

            if (!_instances.TryGetValue(uid, out var waiters))
            {
                waiters = new List<Action<NetObjectIdentity>>();
                _instances.Add(uid, waiters);
            }

            waiters.Add(callback);
        }

        private void OnNewObjectInitialized(NetObjectIdentity identity)
        {
            if (!_instances.Remove(identity.InstanceId, out var waiters))
                return;

            foreach (var action in waiters)
                action(identity);
        }

        public void Dispose()
        {
            if (_world.IsValid && _world.View != null)
                _world.View.NewObjectInitialized -= OnNewObjectInitialized;

            _world = null;
            _instances.Clear();
            _instances = null;
        }
    }
}