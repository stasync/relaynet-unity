using System;
using System.Runtime.CompilerServices;
using Keel.RelayNet.StateManagement;
using UnityEngine;

namespace Keel.RelayNet.GameObjects
{
    public static class NetWorldExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetObjectIdentity CreateObject(this NetWorldRuntime world, NetPrefabObjectWeakRef netPrefabRef, bool shared, Vector3 position, Quaternion rotation) =>
            world.CreateObject((uint)netPrefabRef, shared, position, rotation);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetObjectState CreateObjectAsync(this NetWorldRuntime world, NetPrefabObjectWeakRef netPrefabRef, bool shared, Vector3 position, Quaternion rotation, Action<NetObjectIdentity> callback = null) =>
            world.CreateObjectAsync((uint)netPrefabRef, shared, position, rotation, callback);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetObjectIdentity GetObjectView(this NetWorld world, NetObjectUid uid)
        {
            EnsureValid(world);
            return world.View == null ? null : world.View.GetObjectView(uid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T GetObjectView<T>(this NetWorld world, NetObjectUid uid) where T : NetObjectBehaviourBase
        {
            EnsureValid(world);
            return world.View == null ? null : world.View.GetObjectView<T>(uid);
        }

        public static T FindObjectViewOfType<T>(this NetWorld world) where T : NetObjectBehaviourBase
        {
            EnsureValid(world);

            if (world.View == null)
                return null;

            foreach (var view in world.View.CurrentObjectViews.Values)
            {
                var component = view.GetBehaviour<T>();
                if (component != null)
                    return component;
            }

            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureValid(NetWorld world)
        {
            if (!world.IsValid)
                throw new InvalidOperationException($"Object of type '{world.GetType().FullName}' is invalid.");
        }
    }
}