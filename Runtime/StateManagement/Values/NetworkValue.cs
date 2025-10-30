using System;
using Core.Networking;
using LROTech.Network.StateManagement.Values;
using LROTech.Network.StateManagement.Values.Serialization;

namespace LROTech.Network.StateManagement
{
    public class SerializableValue<TValue> : NetworkValueBase where TValue : unmanaged
    {
        private TValue _value;
        public TValue Value
        {
            get => _value;
            set
            {
                if (!HasPermission)
                    throw new InvalidOperationException($"No authority to change value '{typeof(TValue).FullName}'.");

                _value = value;
            }
        }
        
        public SerializableValue(TValue defaultValue = default) =>
            _value = defaultValue;
        
        internal override void InternalDeserialize(NetReader reader) =>
            _value = OnDeserialize(reader);

        internal override void InternalSerialize(NetWriter writer) =>
            OnSerialize(writer);
        
        protected virtual TValue OnDeserialize(NetReader reader) =>
            SerializationAPI<TValue>.Serializer.Deserialize(reader);

        protected virtual void OnSerialize(NetWriter writer) => 
            SerializationAPI<TValue>.Serializer.Serialize(writer, Value);

        public int GetValueHashCode() =>
            _value.GetHashCode();

        public override string ToString() =>
            _value.ToString();
    }
}