using System;
using Core.Networking;
using LROTech.Network.StateManagement.Values;
using UnityEngine;

namespace LROTech.Network.StateManagement
{
    public abstract class SerializableNumeric<TValue> : NetworkValueBase<TValue, TValue> where TValue : struct, IComparable<TValue>
    {
        protected SerializableNumeric(TValue defaultValue = default) : base(defaultValue)
        {
        }

        public abstract void Change(TValue value);
        public abstract void ChangeClamped(TValue value, TValue min, TValue max);
    }


    public sealed class SerializableFloat : SerializableNumeric<float>
    {
        public override void Change(float value) => 
            Set(InternalValue + value);
        
        public override void ChangeClamped(float value, float min, float max) => 
            Set(Mathf.Clamp(InternalValue + value, min, max));

        protected override float OnDeserialize(NetReader reader) => 
            reader.ReadSingle();

        protected override void OnSerialize(NetWriter writer) => 
            writer.WriteSingle(InternalValue);
    }

    public sealed class SerializableInt : SerializableNumeric<int>
    {
        public override void Change(int value) => 
            Set(InternalValue + value);
        
        public override void ChangeClamped(int value, int min, int max) => 
            Set(Mathf.Clamp(InternalValue + value, min, max));
        
        protected override int OnDeserialize(NetReader reader) => 
            reader.ReadInt32();

        protected override void OnSerialize(NetWriter writer) => 
            writer.WriteInt32(InternalValue);
    }

    public sealed class SerializableBool : SerializableValue<bool>
    {
        protected override bool OnDeserialize(NetReader reader) => 
            reader.ReadBoolean();

        protected override void OnSerialize(NetWriter writer) => 
            writer.WriteBool(InternalValue);
    }

    public sealed class SerializableByte : SerializableValue<byte>
    {
        protected override byte OnDeserialize(NetReader reader) => 
            reader.ReadByte();

        protected override void OnSerialize(NetWriter writer) => 
            writer.WriteByte(InternalValue);
    }

    public sealed class SerializableString : NetworkValueBase<string, string>
    {
        public SerializableString(string defaultValue = "") : base(defaultValue)
        {
        }

        protected override string OnDeserialize(NetReader reader) => 
            reader.ReadString();

        protected override void OnSerialize(NetWriter writer) => 
            writer.WriteString(InternalValue);
    }

    public sealed class SerializableUInt : SerializableNumeric<uint>
    {
        protected override uint OnDeserialize(NetReader reader) => 
            reader.ReadPackedUInt32();

        protected override void OnSerialize(NetWriter writer) => 
            writer.WritePackedUInt32(InternalValue);

        public override void Change(uint value) => 
            Set(InternalValue + value);

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

    public sealed class SerializableObjectUID : SerializableValue<NetObjectUid>
    {
        protected override NetObjectUid OnDeserialize(NetReader reader) => 
            reader.ReadNetObjectUid();

        protected override void OnSerialize(NetWriter writer) => 
            writer.WriteNetObjectUid(InternalValue);
    }
}