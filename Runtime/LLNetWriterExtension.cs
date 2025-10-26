using Core.Networking;

namespace LROTech.Network
{
    public static class LLNetWriterExtension
    {
        public static void WriteClientUid(this NetWriter writer, ClientUid connectionUID) =>
            writer.WritePackedUInt32(connectionUID);
    }
}
