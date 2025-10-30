using Core.Networking;
using LROTech.Network.StateManagement.Values;
using UnityEngine;

namespace LROTech.Network.StateManagement
{
    public static class SerializableFloatExtensions
    {
        public static void Change(this SerializableValue<float> value, float modifier) => 
            value.Set(value.InternalValue + modifier);
        
        public static void ChangeClamped(this SerializableValue<float> value, float modifier, float min, float max) => 
            value.Set(Mathf.Clamp(value.InternalValue + modifier, min, max));
    }
    
    public static class SerializableIntExtensions
    {
        public static void Change(this SerializableValue<int> value, int modifier) => 
            value.Set(value.InternalValue + modifier);
        
        public static void ChangeClamped(this SerializableValue<int> value, int modifier, int min, int max) => 
            value.Set(Mathf.Clamp(value.InternalValue + modifier, min, max));
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
}