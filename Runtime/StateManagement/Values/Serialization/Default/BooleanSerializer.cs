using Core.Networking;

namespace LROTech.Network.StateManagement.Values.Serialization.Default
{
    internal sealed class BooleanSerializer : VariableSerializer<bool>
    {
        public override void Serialize(NetWriter writer, bool value) =>
            writer.WriteBool(value);

        public override bool Deserialize(NetReader reader) =>
            reader.ReadBoolean();
    }
}