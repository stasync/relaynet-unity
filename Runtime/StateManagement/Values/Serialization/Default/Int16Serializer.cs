using Core.Networking;

namespace LROTech.Network.StateManagement.Values.Serialization.Default
{
    internal sealed class Int16Serializer : VariableSerializer<short>
    {
        public override void Serialize(NetWriter writer, short value) =>
            writer.WriteInt16(value);

        public override short Deserialize(NetReader reader) =>
            reader.ReadInt16();
    }
}