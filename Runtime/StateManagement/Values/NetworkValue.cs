using Core.Networking;
using LROTech.Network.StateManagement.Values;

namespace LROTech.Network.StateManagement
{
    public class SerializableValue<TValue> : SerializableValueBase<TValue, TValue> where TValue : unmanaged
    {
        public SerializableValue(TValue defaultValue = default) : base(defaultValue)
        {
        }

        protected override TValue OnDeserialize(NetReader reader, bool firstTime) =>
            reader.ReadUnmanaged<TValue>();

        protected override void OnSerialize(NetWriter writer) => 
            writer.WriteUnmanaged(Value);
    }
}