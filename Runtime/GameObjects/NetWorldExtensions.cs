using System.Runtime.CompilerServices;
using LROTech.Network.StateManagement;
using UnityEngine;

namespace LROTech.Network.GameObjects
{
    public static class NetWorldExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetObjectIdentity CreateObject(this NetWorldRuntime world, NetPrefabObjectWeakRef netPrefabRef, bool isSceneObject, Vector3 position, Quaternion rotation) =>
            world.CreateObject((uint)netPrefabRef, isSceneObject, position, rotation);
    }
}