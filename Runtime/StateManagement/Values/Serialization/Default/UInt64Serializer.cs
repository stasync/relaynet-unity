using Keel.Networking;

namespace Keel.RelayNet.StateManagement.Values.Serialization.Default
{
    internal sealed class UInt64Serializer : VariableSerializer<ulong>
    {
        public override void Serialize(NetWriter writer, ulong value) =>
            writer.WritePackedUInt64(value);

        public override ulong Deserialize(NetReader reader) =>
            reader.ReadPackedUInt64();
    }
}