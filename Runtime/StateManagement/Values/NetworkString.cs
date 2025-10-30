using Core.Networking;
using LROTech.Network.StateManagement.Values;

namespace LROTech.Network.StateManagement
{
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