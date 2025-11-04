using Core.Networking;

namespace LROTech.Network
{
    public static class LLNetReaderExtension
    {
        public static ClientUid ReadClientUid(this NetReader reader) => new(reader.ReadPackedUInt32());
    }
}