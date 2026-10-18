using System.Runtime.CompilerServices;
using Keel.Networking;
using LROTech.Network.GameObjects;
using UnityEngine;

namespace LROTech.Network.StateManagement
{
    public static class HLNetWriterExtension
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WritePlayer(this NetWriter writer, NetWorldPlayer value) =>
            writer.WriteClientUid(value?.Uid ?? ClientUid.Invalid);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteNetObjectUid(this NetWriter writer, NetObjectUid value) =>
            writer.WritePackedUInt32((uint)value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteIdentity(this NetWriter writer, NetObjectIdentity value) =>
            WriteNetObjectUid(writer, value == null ? NetObjectUid.Invalid : value.InstanceId);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteVector2(this NetWriter writer, Vector2 value)
        {
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteVector3(this NetWriter writer, Vector3 value)
        {
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
            writer.WriteSingle(value.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteVector4(this NetWriter writer, Vector4 value)
        {
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
            writer.WriteSingle(value.z);
            writer.WriteSingle(value.w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteColor(this NetWriter writer, Color value)
        {
            writer.WriteSingle(value.r);
            writer.WriteSingle(value.g);
            writer.WriteSingle(value.b);
            writer.WriteSingle(value.a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteColor32(this NetWriter writer, Color32 value)
        {
            writer.WriteSingle(value.r);
            writer.WriteSingle(value.g);
            writer.WriteSingle(value.b);
            writer.WriteSingle(value.a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteQuaternion(this NetWriter writer, Quaternion value)
        {
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
            writer.WriteSingle(value.z);
            writer.WriteSingle(value.w);
        }
    }
}