using System.Runtime.CompilerServices;
using Keel.Networking;

namespace LROTech.Network.StateManagement.Values
{
    public sealed class NetworkString : NetworkValueManaged<string, string>
    {
        public new string Value
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => InternalValue;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
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