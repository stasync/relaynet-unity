using Core.Networking;
using System;
using UnityEngine;

public static class HLNetWriterExtention
{
    public static void WriteConnectionUID(this NetWriter writer, ClientUID connectionUID) =>
       writer.WritePackedUInt32(connectionUID);

    public static void WritePlayer(this NetWriter writer, INetRTPlayer value) =>
        writer.WriteConnectionUID(value == null ? ClientUID.Invalid : value.ConnectionUID);

    public static void WriteInstanceId(this NetWriter writer, GSInstanceId value) => 
        writer.WritePackedUInt32(value);

    public static void WriteIdentity(this NetWriter writer, GSInstanceIdentity value) => 
        WriteInstanceId(writer, value == null ? GSInstanceId.Invalid : value.InstanceId);

    public static void WriteBehaviour(this NetWriter writer, GSInstanceBehaviour value) => 
        WriteIdentity(writer, value == null ? null : value.Identity);

    public static void WriteGameObject(this NetWriter writer, GameObject value)
    {
        var identity = value.GetComponent<GSInstanceIdentity>();
        if (identity == null)
            throw new InvalidOperationException("Unable to write object without 'GSInstanceIdentity'");

        WriteIdentity(writer, identity);
    }

    public static void Write(this NetWriter writer, Vector2 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
    }

    public static void Write(this NetWriter writer, Vector3 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
    }

    public static void Write(this NetWriter writer, Vector4 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
        writer.Write(value.w);
    }

    public static void Write(this NetWriter writer, Color value)
    {
        writer.Write(value.r);
        writer.Write(value.g);
        writer.Write(value.b);
        writer.Write(value.a);
    }

    public static void Write(this NetWriter writer, Color32 value)
    {
        writer.Write(value.r);
        writer.Write(value.g);
        writer.Write(value.b);
        writer.Write(value.a);
    }

    public static void Write(this NetWriter writer, Quaternion value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
        writer.Write(value.w);
    }

    public static void Write(this NetWriter writer, Rect value)
    {
        writer.Write(value.xMin);
        writer.Write(value.yMin);
        writer.Write(value.width);
        writer.Write(value.height);
    }

    public static void Write(this NetWriter writer, Plane value)
    {
        writer.Write(value.normal);
        writer.Write(value.distance);
    }

    public static void Write(this NetWriter writer, Ray value)
    {
        writer.Write(value.direction);
        writer.Write(value.origin);
    }

    public static void Write(this NetWriter writer, Matrix4x4 value)
    {
        writer.Write(value.m00);
        writer.Write(value.m01);
        writer.Write(value.m02);
        writer.Write(value.m03);
        writer.Write(value.m10);
        writer.Write(value.m11);
        writer.Write(value.m12);
        writer.Write(value.m13);
        writer.Write(value.m20);
        writer.Write(value.m21);
        writer.Write(value.m22);
        writer.Write(value.m23);
        writer.Write(value.m30);
        writer.Write(value.m31);
        writer.Write(value.m32);
        writer.Write(value.m33);
    }
}