using System;
using UnityEngine;

public class SerializableFloat : SerializableValue<float>
{
    public SerializableFloat(float defaultValue, Func<bool> writePermission) : 
        base(defaultValue, writePermission, (r, f) => r.ReadSingle(), (w, v) => w.Write(v))
    { }
}

public class SerializableInt : SerializableValue<int>
{
    public SerializableInt(int defaultValue, Func<bool> writePermission) :
        base(defaultValue, writePermission, (r, f) => r.ReadInt32(), (w, v) => w.Write(v))
    { }
}

public class SerializableBool : SerializableValue<bool>
{
    public SerializableBool(bool defaultValue, Func<bool> writePermission) :
        base(defaultValue, writePermission, (r, f) => r.ReadBoolean(), (w, v) => w.Write(v))
    { }
}

public class SerializableByte : SerializableValue<byte>
{
    public SerializableByte(byte defaultValue, Func<bool> writePermission) :
        base(defaultValue, writePermission, (r, f) => r.ReadByte(), (w, v) => w.Write(v))
    { }
}

public class SerializableString : SerializableValue<string>
{
    public SerializableString(string defaultValue, Func<bool> writePermission) :
        base(defaultValue, writePermission, (r, f) => r.ReadString(), (w, v) => w.Write(v))
    { }
}

public class SerializableUInt : SerializableValue<uint>
{
    public SerializableUInt(uint defaultValue, Func<bool> writePermission) :
        base(defaultValue, writePermission, (r, f) => r.ReadPackedUInt32(), (w, v) => w.WritePackedUInt32(v))
    { }
}

public class SerializableVector3: SerializableValue<Vector3>
{
    public SerializableVector3(Vector3 defaultValue, Func<bool> writePermission) :
        base(defaultValue, writePermission, (r, f) => r.ReadVector3(), (w, v) => w.Write(v))
    { }
}

public class SerializableVector2 : SerializableValue<Vector2>
{
    public SerializableVector2(Vector2 defaultValue, Func<bool> writePermission) :
        base(defaultValue, writePermission, (r, f) => r.ReadVector2(), (w, v) => w.Write(v))
    { }
}

public class SerializableQuaternion : SerializableValue<Quaternion>
{
    public SerializableQuaternion(Quaternion defaultValue, Func<bool> writePermission) :
        base(defaultValue, writePermission, (r, f) => r.ReadQuaternion(), (w, v) => w.Write(v))
    { }
}