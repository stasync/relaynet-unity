using Keel.RelayNet.StateManagement;
using UnityEngine;

namespace Keel.RelayNet.Debug
{
    /// <summary>
    /// The colors the debug tools mark each player's objects with, the same in the editor window and in the overlay.
    /// </summary>
    internal static class NetDebugColors
    {
        public static readonly Color Sent = new(0.35f, 0.65f, 1f);
        public static readonly Color Received = new(0.45f, 0.85f, 0.55f);
        public static readonly Color Good = new(0.48f, 0.85f, 0.56f);
        public static readonly Color Bad = new(1f, 0.42f, 0.42f);
        public static readonly Color Dim = new(0.6f, 0.63f, 0.65f);

        private static readonly Color[] s_perClientColors =
        {
            Color.red,
            Color.blue,
            Color.magenta,
            Color.green,
            Color.cyan,
            Color.yellow,

            // Custom colors:
            new(1.0f, 0.5f, 0.0f), // Orange
            new(0.5f, 0.0f, 1.0f), // Violet
            new(1.0f, 0.0f, 0.5f), // Rose/Hot pink
            new(0.0f, 0.5f, 1.0f), // Azure/Sky blue
            new(1.0f, 1.0f, 0.5f), // Light yellow
            new(0.5f, 0.0f, 0.0f), // Dark red / Maroon
            new(0.0f, 0.5f, 0.0f), // Dark green
            new(0.5f, 0.5f, 0.0f), // Olive
            new(0.5f, 0.0f, 0.5f), // Purple
            new(0.0f, 0.5f, 0.5f), // Teal
            new(1.0f, 0.75f, 0.8f), // Pink
            new(0.6f, 0.4f, 0.2f) // Brown
        };

        /// <summary>
        /// The player's color, or gray for an object nobody owns.
        /// </summary>
        public static Color ForPlayer(NetWorldPlayer player) =>
            player == null ? Color.gray : s_perClientColors[(uint)player.Uid % (uint)s_perClientColors.Length];

        /// <summary>
        /// The color as a rich text hex code, like <c>#FF8000</c>.
        /// </summary>
        public static string ToHex(Color color) =>
            "#" + ColorUtility.ToHtmlStringRGB(color);
    }
}
