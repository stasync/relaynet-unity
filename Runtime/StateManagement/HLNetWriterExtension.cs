using Core.Networking;
using UnityEngine;

namespace LROTech.Network.StateManagement
{
    public static class HLNetWriterExtension
    {
        public static void WritePlayer(this NetWriter writer, NetWorldPlayer value) =>
            writer.WriteClientUid(value?.Uid ?? ClientUid.Invalid);

        public static void WriteInstanceId(this NetWriter writer, NetObjectUid value) =>
            writer.WritePackedUInt32(value);

        public static void WriteIdentity(this NetWriter writer, NetObjectIdentity value) =>
            WriteInstanceId(writer, value == null ? NetObjectUid.Invalid : value.InstanceId);

        public static void WriteVector2(this NetWriter writer, Vector2 value)
        {
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
        }

        public static void WriteVector3(this NetWriter writer, Vector3 value)
        {
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
            writer.WriteSingle(value.z);
        }

        public static void WriteVector4(this NetWriter writer, Vector4 value)
        {
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
            writer.WriteSingle(value.z);
            writer.WriteSingle(value.w);
        }

        public static void WriteColor(this NetWriter writer, Color value)
        {
            writer.WriteSingle(value.r);
            writer.WriteSingle(value.g);
            writer.WriteSingle(value.b);
            writer.WriteSingle(value.a);
        }

        public static void WriteColor32(this NetWriter writer, Color32 value)
        {
            writer.WriteSingle(value.r);
            writer.WriteSingle(value.g);
            writer.WriteSingle(value.b);
            writer.WriteSingle(value.a);
        }

        public static void WriteQuaternion(this NetWriter writer, Quaternion value)
        {
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
            writer.WriteSingle(value.z);
            writer.WriteSingle(value.w);
        }

        public static void WriteRect(this NetWriter writer, Rect value)
        {
            writer.WriteSingle(value.xMin);
            writer.WriteSingle(value.yMin);
            writer.WriteSingle(value.width);
            writer.WriteSingle(value.height);
        }

        public static void WritePlane(this NetWriter writer, Plane value)
        {
            writer.WriteVector3(value.normal);
            writer.WriteSingle(value.distance);
        }

        public static void WriteRay(this NetWriter writer, Ray value)
        {
            writer.WriteVector3(value.direction);
            writer.WriteVector3(value.origin);
        }

        public static void WriteMatrix4x4(this NetWriter writer, Matrix4x4 value)
        {
            // TODO: CHANGE TO WRITE BLITTABLE.
            writer.WriteSingle(value.m00);
            writer.WriteSingle(value.m01);
            writer.WriteSingle(value.m02);
            writer.WriteSingle(value.m03);
            writer.WriteSingle(value.m10);
            writer.WriteSingle(value.m11);
            writer.WriteSingle(value.m12);
            writer.WriteSingle(value.m13);
            writer.WriteSingle(value.m20);
            writer.WriteSingle(value.m21);
            writer.WriteSingle(value.m22);
            writer.WriteSingle(value.m23);
            writer.WriteSingle(value.m30);
            writer.WriteSingle(value.m31);
            writer.WriteSingle(value.m32);
            writer.WriteSingle(value.m33);
        }
    }
}