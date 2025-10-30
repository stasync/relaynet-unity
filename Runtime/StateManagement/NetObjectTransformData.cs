using UnityEngine;

namespace LROTech.Network.StateManagement
{
    public sealed class NetObjectTransformData : NetObjectStateData
    {
        public readonly NetworkValue<Vector3> Position = new();
        public readonly NetworkValue<Quaternion> Rotation = new();
        public readonly NetworkValue<Vector3> Velocity = new();
        public readonly NetworkValue<float> WorldTime = new();
        public readonly NetworkValue<bool> HasParent = new();
    }
}