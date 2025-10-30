using Core.Networking;
using LROTech.Network.StateManagement.Values;
using LROTech.Network.StateManagement.Values.Serialization;

namespace LROTech.Network.StateManagement
{
    public class SerializableValue<TValue> : NetworkValueBase<TValue, TValue> where TValue : unmanaged
    {
        public SerializableValue(TValue defaultValue = default) : base(defaultValue)
        {
        }

        protected override TValue OnDeserialize(NetReader reader) =>
            SerializationAPI<TValue>.Serializer.Deserialize(reader);

        protected override void OnSerialize(NetWriter writer) => 
            SerializationAPI<TValue>.Serializer.Serialize(writer, InternalValue);
    }
}