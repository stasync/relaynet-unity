using Core.Networking;

namespace LROTech.Network.Sessions
{
    public static class LLNetReaderExtension 
    {
        public static ClientUid ReadClientUid(this NetReader reader) =>
            reader.ReadPackedUInt32();
    }
}
