using Core.Networking;

namespace LROTech.Network.StateManagement.Values.Serialization
{
    internal sealed class UInt32Serializer : VariableSerializer<uint>
    {
        public override void Serialize(NetWriter writer, uint value) =>
            writer.WritePackedUInt32(value);

        public override uint Deserialize(NetReader reader) =>
            reader.ReadPackedUInt32();
    }
}