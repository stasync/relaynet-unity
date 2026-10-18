namespace Keel.RelayNet.StateManagement.Snapshots
{
    public readonly struct NetworkPropertiesSnapshot
    {
        internal readonly byte[] Data;

        internal NetworkPropertiesSnapshot(byte[] properties) =>
            Data = properties;

        public int GetSize() =>
            Data.Length;
    }
}