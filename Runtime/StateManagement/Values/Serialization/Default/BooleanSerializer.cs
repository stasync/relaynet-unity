using Keel.Networking;

namespace Keel.RelayNet.StateManagement.Values.Serialization.Default
{
    internal sealed class BooleanSerializer : VariableSerializer<bool>
    {
        public override void Serialize(NetWriter writer, bool value) =>
            writer.WriteBoolean(value);

        public override bool Deserialize(NetReader reader) =>
            reader.ReadBoolean();
    }
}