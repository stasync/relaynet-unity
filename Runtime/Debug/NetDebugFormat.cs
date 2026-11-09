namespace Keel.RelayNet.Debug
{
    /// <summary>
    /// Formats the numbers the debug tools show, the same way in the editor window and in the overlay.
    /// </summary>
    internal static class NetDebugFormat
    {
        private const float KILOBYTE = 1024f;
        private const float MEGABYTE = KILOBYTE * KILOBYTE;

        public static string Bytes(float bytes) =>
            bytes switch
            {
                >= MEGABYTE => $"{bytes / MEGABYTE:0.00} MB",
                >= KILOBYTE => $"{bytes / KILOBYTE:0.0} KB",
                _ => $"{bytes:0} B"
            };

        public static string Rate(float bytesPerSecond) =>
            $"{Bytes(bytesPerSecond)}/s";

        /// <summary>
        /// How much smaller the compressed bytes are than the original ones, like "29%", or "-" before there's anything to compare.
        /// </summary>
        public static string Savings(int bytes, int compressedBytes) =>
            bytes <= 0 ? "-" : $"{(1f - compressedBytes / (float)bytes) * 100f:0}%";

        /// <summary>
        /// The share of a total, like "34%", or "-" for an empty total.
        /// </summary>
        public static string Share(int part, int total) =>
            total <= 0 ? "-" : $"{part / (float)total * 100f:0}%";
    }
}
