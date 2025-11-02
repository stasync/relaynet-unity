using Core.Networking;

namespace LROTech.Network.StateManagement.Values
{
    public sealed class NetworkString : NetworkValueManaged<string, string>
    {
        public new string Value
        {
            get => InternalValue;
            set => Set(value);
        }
        
        public NetworkString(string defaultValue = "") : base(defaultValue)
        {
        }

        protected override string OnDeserialize(NetReader reader) => 
            reader.ReadString();

        protected override void OnSerialize(NetWriter writer) => 
            writer.WriteString(InternalValue);
    }
}