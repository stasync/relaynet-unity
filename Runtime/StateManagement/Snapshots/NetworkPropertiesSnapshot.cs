using Core.Networking;

namespace LROTech.Network.StateManagement
{
    public readonly struct NetworkPropertiesSnapshot
    {
        public readonly byte[] Data;

        public NetworkPropertiesSnapshot(byte[] properties) =>
            Data = properties;

        public int GetSize() =>
            Data.Length;

        public static NetworkPropertiesSnapshot FromProperties(NetworkProperties properties) =>
            properties.ToSnapshot();

        public static NetworkPropertiesSnapshot FromReader(NetReader reader) =>
            new(reader.ReadBytesAndSize());
    }
}