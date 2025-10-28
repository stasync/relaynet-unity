using UnityEngine;

namespace LROTech.Network.StateManagement
{
    public sealed class NetObjectTransformData : NetObjectStateData
    {
        public readonly SerializableValue<Vector3> Position = new();
        public readonly SerializableValue<Quaternion> Rotation = new();
        public readonly SerializableValue<Vector3> Velocity = new();
        public readonly SerializableFloat WorldTime = new();
        public readonly SerializableBool HasParent = new();
    }
}