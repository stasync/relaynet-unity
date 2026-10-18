using System.Runtime.CompilerServices;
using Keel.Networking;
using Keel.RelayNet.GameObjects;
using UnityEngine;

namespace Keel.RelayNet.StateManagement
{
    public static class HLNetReaderExtension
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetObjectUid ReadNetObjectUid(this NetReader reader) =>
            new(reader.ReadPackedUInt32());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetWorldPlayer ReadPlayer(this NetReader reader, NetWorld targetWorld) =>
            targetWorld.Players.Get(reader.ReadClientUid());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetObjectState ReadObjectState(this NetReader reader, NetWorld targetWorld) =>
            targetWorld.GetObject(uid: new NetObjectUid(reader.ReadPackedUInt32()));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadObjectViewOfType<T>(this NetReader reader, NetWorld targetWorld) where T : NetObjectBehaviourBase =>
            targetWorld.GetObjectView<T>(uid: new NetObjectUid(reader.ReadPackedUInt32()));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ReadVector2(this NetReader reader) =>
            new(reader.ReadSingle(), reader.ReadSingle());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ReadVector3(this NetReader reader) =>
            new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 ReadVector4(this NetReader reader) =>
            new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color ReadColor(this NetReader reader) =>
            new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color32 ReadColor32(this NetReader reader) =>
            new(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion ReadQuaternion(this NetReader reader) =>
            new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }
}