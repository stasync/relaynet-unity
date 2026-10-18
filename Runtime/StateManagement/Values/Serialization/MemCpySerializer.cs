using Keel.Networking;

namespace LROTech.Network.StateManagement.Values.Serialization
{
    internal sealed class MemCpySerializer<T> : VariableSerializer<T> where T : unmanaged
    {
        public override void Serialize(NetWriter writer, T value) =>
            writer.WriteUnmanaged(value);

        public override T Deserialize(NetReader reader) =>
            reader.ReadUnmanaged<T>();
    }
}