using Core.Networking;

namespace LROTech.Network.StateManagement.Values
{
    public sealed class NetworkString : NetworkValueBase<string, string>
    {
        public NetworkString(string defaultValue = "") : base(defaultValue)
        {
        }

        protected override string OnDeserialize(NetReader reader) => 
            reader.ReadString();

        protected override void OnSerialize(NetWriter writer) => 
            writer.WriteString(InternalValue);
    }
}