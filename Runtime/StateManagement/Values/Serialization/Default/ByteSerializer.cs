using Keel.Networking;

namespace Keel.RelayNet.StateManagement.Values.Serialization.Default
{
    internal sealed class ByteSerializer : VariableSerializer<byte>
    {
        public override void Serialize(NetWriter writer, byte value) =>
            writer.WriteByte(value);

        public override byte Deserialize(NetReader reader) =>
            reader.ReadByte();
    }
}