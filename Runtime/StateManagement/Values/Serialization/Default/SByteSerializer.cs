using Keel.Networking;

namespace LROTech.Network.StateManagement.Values.Serialization.Default
{
    internal sealed class SByteSerializer : VariableSerializer<sbyte>
    {
        public override void Serialize(NetWriter writer, sbyte value) =>
            writer.WriteSByte(value);

        public override sbyte Deserialize(NetReader reader) =>
            reader.ReadSByte();
    }
}