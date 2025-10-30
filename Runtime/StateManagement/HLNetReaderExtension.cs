using Core.Networking;
using LROTech.Network.GameObjects;
using LROTech.Network.Sessions;
using UnityEngine;

namespace LROTech.Network.StateManagement
{
    public static class HLNetReaderExtension
    {
        public static NetObjectUid ReadNetObjectUid(this NetReader reader) =>
            new (reader.ReadPackedUInt32());

        public static NetWorldPlayer ReadPlayer(this NetReader reader, NetWorld targetWorld) =>
            targetWorld.Players.Find(reader.ReadClientUid());

        public static NetObjectState ReadObjectState(this NetReader reader, NetWorld targetWorld) =>
            targetWorld.FindObject(instanceId: new NetObjectUid(reader.ReadPackedUInt32()));

        public static T ReadObjectViewOfType<T>(this NetReader reader, NetWorld targetWorld) where T : NetObjectBehaviourBase =>
            targetWorld.FindObjectView<T>(instanceId: new NetObjectUid(reader.ReadPackedUInt32()));

        public static Vector2 ReadVector2(this NetReader reader) =>
            new(reader.ReadSingle(), reader.ReadSingle());

        public static Vector3 ReadVector3(this NetReader reader) =>
            new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        public static Vector4 ReadVector4(this NetReader reader) =>
            new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        public static Color ReadColor(this NetReader reader) =>
            new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        public static Color32 ReadColor32(this NetReader reader) =>
            new(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());

        public static Quaternion ReadQuaternion(this NetReader reader) =>
            new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }
}