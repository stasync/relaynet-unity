using System;

namespace LROTech.Network.StateManagement
{
    public sealed class NetObjectTransformData : NetObjectStateData
    {
        public NetObjectTransformData(Func<bool> writePermission) : base(writePermission)
        {
        }

        public readonly SerializableVector3 Position = new();
        public readonly SerializableQuaternion Rotation = new();
        public readonly SerializableVector3 Velocity = new();
        public readonly SerializableFloat WorldTime = new();
        public readonly SerializableBool HasParent = new();
    }
}