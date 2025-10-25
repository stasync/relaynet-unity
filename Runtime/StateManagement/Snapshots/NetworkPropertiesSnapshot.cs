namespace LROTech.Network.StateManagement.Snapshots
{
    public readonly struct NetworkPropertiesSnapshot
    {
        public readonly byte[] Data;

        internal NetworkPropertiesSnapshot(byte[] properties) =>
            Data = properties;

        public int GetSize() =>
            Data.Length;
    }
}