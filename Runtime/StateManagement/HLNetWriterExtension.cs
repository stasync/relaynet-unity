using Core.Networking;
using System;
using UnityEngine;

namespace LROTech.Network.State
{
    public static class HLNetWriterExtension
    {
        public static void WritePlayer(this NetWriter writer, NetWorldPlayer value) =>
            writer.WriteClientUid(value == null ? ClientUID.Invalid : value.Uid);

        public static void WriteInstanceId(this NetWriter writer, NetObjectUID value) =>
            writer.WritePackedUInt32(value);

        public static void WriteIdentity(this NetWriter writer, NetObjectIdentity value) =>
            WriteInstanceId(writer, value == null ? NetObjectUID.Invalid : value.InstanceId);

        public static void WriteBehaviour(this NetWriter writer, GSInstanceBehaviourBase value) =>
            WriteIdentity(writer, value == null ? null : value.Identity);

        public static void WriteGameObject(this NetWriter writer, GameObject value)
        {
            var identity = value.GetComponent<NetObjectIdentity>();
            if (identity == null)
                throw new InvalidOperationException("Unable to write object without 'GSInstanceIdentity'");

            WriteIdentity(writer, identity);
        }

        public static void Write(this NetWriter writer, Vector2 value)
        {
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
        }

        public static void Write(this NetWriter writer, Vector3 value)
        {
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
            writer.WriteSingle(value.z);
        }

        public static void Write(this NetWriter writer, Vector4 value)
        {
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
            writer.WriteSingle(value.z);
            writer.WriteSingle(value.w);
        }

        public static void Write(this NetWriter writer, Color value)
        {
            writer.WriteSingle(value.r);
            writer.WriteSingle(value.g);
            writer.WriteSingle(value.b);
            writer.WriteSingle(value.a);
        }

        public static void Write(this NetWriter writer, Color32 value)
        {
            writer.WriteSingle(value.r);
            writer.WriteSingle(value.g);
            writer.WriteSingle(value.b);
            writer.WriteSingle(value.a);
        }

        public static void Write(this NetWriter writer, Quaternion value)
        {
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
            writer.WriteSingle(value.z);
            writer.WriteSingle(value.w);
        }

        public static void Write(this NetWriter writer, Rect value)
        {
            writer.WriteSingle(value.xMin);
            writer.WriteSingle(value.yMin);
            writer.WriteSingle(value.width);
            writer.WriteSingle(value.height);
        }

        public static void Write(this NetWriter writer, Plane value)
        {
            writer.Write(value.normal);
            writer.WriteSingle(value.distance);
        }

        public static void Write(this NetWriter writer, Ray value)
        {
            writer.Write(value.direction);
            writer.Write(value.origin);
        }

        public static void Write(this NetWriter writer, Matrix4x4 value)
        {
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