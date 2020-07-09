using Core.Networking;
using UnityEngine;

public static class HLNetReaderExtention
{
    public static ClientUID ReadConnectionUID(this NetReader reader) =>
        reader.ReadPackedUInt32();

    public static NetObjectUID ReadInstanceId(this NetReader reader) =>
        reader.ReadPackedUInt32();

    public static NetWorldPlayer ReadPlayer(this NetReader reader, NetWorld targetWorld) =>
        targetWorld.Players.Find(reader.ReadConnectionUID());

    public static NetObjectState ReadObjectState(this NetReader reader, NetWorld targetWorld) =>
        targetWorld.FindObject(reader.ReadPackedUInt32());

    public static T ReadObjectViewOfType<T>(this NetReader reader, NetWorld targetWorld) where T : GSInstanceBehaviourBase =>
        targetWorld.FindObjectView<T>(reader.ReadPackedUInt32());

    public static Vector2 ReadVector2(this NetReader reader) => 
        new Vector2(reader.ReadSingle(), reader.ReadSingle());

    public static Vector3 ReadVector3(this NetReader reader) =>
        new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    public static Vector4 ReadVector4(this NetReader reader) =>
        new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    public static Color ReadColor(this NetReader reader) =>
         new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    public static Color32 ReadColor32(this NetReader reader) =>
        new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());

    public static Quaternion ReadQuaternion(this NetReader reader) =>
        new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    public static Rect ReadRect(this NetReader reader) =>
        new Rect(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    public static Plane ReadPlane(this NetReader reader) =>
        new Plane(reader.ReadVector3(), reader.ReadSingle());

    public static Ray ReadRay(this NetReader reader) =>
        new Ray(reader.ReadVector3(), reader.ReadVector3());

    public static Matrix4x4 ReadMatrix4x4(this NetReader reader) =>
        new Matrix4x4
        {
            m00 = reader.ReadSingle(),
            m01 = reader.ReadSingle(),
            m02 = reader.ReadSingle(),
            m03 = reader.ReadSingle(),
            m10 = reader.ReadSingle(),
            m11 = reader.ReadSingle(),
            m12 = reader.ReadSingle(),
            m13 = reader.ReadSingle(),
            m20 = reader.ReadSingle(),
            m21 = reader.ReadSingle(),
            m22 = reader.ReadSingle(),
            m23 = reader.ReadSingle(),
            m30 = reader.ReadSingle(),
            m31 = reader.ReadSingle(),
            m32 = reader.ReadSingle(),
            m33 = reader.ReadSingle()
        };
}

