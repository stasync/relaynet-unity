using System.Runtime.CompilerServices;
using Keel.Networking;

namespace LROTech.Network
{
    public static class LLNetReaderExtension
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ClientUid ReadClientUid(this NetReader reader) => 
            new(reader.ReadPackedUInt32());
    }
}