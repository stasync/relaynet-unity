using System;
using Core.Networking;
using UnityEngine;

namespace LROTech.Network.StateManagement
{
    public abstract class SerializableNumeric<TValue> : SerializableValue<TValue> where TValue : struct, IComparable<TValue>
    {
        public SerializableNumeric(TValue defaultValue, Func<NetReader, bool, TValue> reader, Action<NetWriter, TValue> writer) : base(defaultValue,
            reader, writer)
        {
        }

        public abstract void Change(TValue value);
        public abstract void ChangeClamped(TValue value, TValue min, TValue max);
    }


    public class SerializableFloat : SerializableNumeric<float>
    {
        public SerializableFloat(float defaultValue = default) : base(defaultValue, (r, f) => r.ReadSingle(), (w, v) => w.WriteSingle(v))
        {
        }

        public override void Change(float value) => Set(InternalValue + value);
        public override void ChangeClamped(float value, float min, float max) => Set(Mathf.Clamp(InternalValue + value, min, max));
    }

    public class SerializableInt : SerializableNumeric<int>
    {
        public SerializableInt(int defaultValue = default) : base(defaultValue, (r, f) => r.ReadInt32(), (w, v) => w.WriteInt32(v))
        {
        }

        public override void Change(int value) => Set(InternalValue + value);
        public override void ChangeClamped(int value, int min, int max) => Set(Mathf.Clamp(InternalValue + value, min, max));
    }

    public class SerializableBool : SerializableValue<bool>
    {
        public SerializableBool(bool defaultValue = default) : base(defaultValue, (r, f) => r.ReadBoolean(), (w, v) => w.WriteBool(v))
        {
        }
    }

    public class SerializableByte : SerializableValue<byte>
    {
        public SerializableByte(byte defaultValue = default) : base(defaultValue, (r, f) => r.ReadByte(), (w, v) => w.WriteByte(v))
        {
        }
    }

    public class SerializableString : SerializableValue<string>
    {
        public SerializableString() : this(string.Empty)
        {
        }

        public SerializableString(string defaultValue) : base(defaultValue, (r, f) => r.ReadString(), (w, v) => w.WriteString(v))
        {
        }
    }

    public class SerializableUInt : SerializableNumeric<uint>
    {
        public SerializableUInt(uint defaultValue = default) : base(defaultValue, (r, f) => r.ReadPackedUInt32(), (w, v) => w.WritePackedUInt32(v))
        {
        }

        public override void Change(uint value) => Set(InternalValue + value);

        public override void ChangeClamped(uint value, uint min, uint max)
        {
            var result = InternalValue + value;
            if (result > max)
                result = max;
            else if (result < min)
                result = min;

            Set(result);
        }
    }

    public class SerializableObjectUID : SerializableValue<NetObjectUID>
    {
        public SerializableObjectUID() : this(NetObjectUID.Invalid)
        {
        }

        public SerializableObjectUID(NetObjectUID defaultValue) : base(defaultValue, (r, f) => r.ReadInstanceId(), (w, v) => w.WriteInstanceId(v))
        {
        }
    }

    public class SerializableVector3 : SerializableValue<Vector3>
    {
        public SerializableVector3(Vector3 defaultValue = default) : base(defaultValue, (r, f) => r.ReadVector3(), (w, v) => w.Write(v))
        {
        }
    }

    public class SerializableVector2 : SerializableValue<Vector2>
    {
        public SerializableVector2(Vector2 defaultValue = default) : base(defaultValue, (r, f) => r.ReadVector2(), (w, v) => w.Write(v))
        {
        }
    }

    public class SerializableQuaternion : SerializableValue<Quaternion>
    {
        public SerializableQuaternion() : this(Quaternion.identity)
        {
        }

        public SerializableQuaternion(Quaternion defaultValue) : base(defaultValue, (r, f) => r.ReadQuaternion(), (w, v) => w.Write(v))
        {
        }
    }
}