using System.Runtime.CompilerServices;
using Core.Networking;

namespace LROTech.Network
{
    public static class LLNetWriterExtension
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteClientUid(this NetWriter writer, ClientUid connectionUid) =>
            writer.WritePackedUInt32((uint)connectionUid);
    }
}
