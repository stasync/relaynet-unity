using Core.Networking;

namespace LROTech.Network.StateManagement.Values.Serialization
{
    internal sealed class UInt16Serializer : VariableSerializer<ushort>
    {
        public override void Serialize(NetWriter writer, ushort value) =>
            writer.WriteUInt16(value);

        public override ushort Deserialize(NetReader reader) =>
            reader.ReadUInt16();
    }
}