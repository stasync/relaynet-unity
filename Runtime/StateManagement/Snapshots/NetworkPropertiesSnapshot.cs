namespace LROTech.Network.StateManagement
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