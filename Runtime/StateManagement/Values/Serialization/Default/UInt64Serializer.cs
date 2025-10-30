using Core.Networking;

namespace LROTech.Network.StateManagement.Values.Serialization
{
    internal sealed class ULongSerializer : VariableSerializer<ulong>
    {
        public override void Serialize(NetWriter writer, ulong value) =>
            writer.WritePackedUInt64(value);

        public override ulong Deserialize(NetReader reader) =>
            reader.ReadPackedUInt64();
    }
}