namespace Keel.RelayNet.Debug
{
    /// <summary>
    /// What <see cref="NetDebugOverlay"/> shows. Each mode adds to the one before it, in the order the hotkey steps
    /// through them.
    /// </summary>
    public enum NetDebugOverlayMode
    {
        /// <summary>
        /// Nothing. The overlay only listens for its hotkey.
        /// </summary>
        Off,

        /// <summary>
        /// A small panel in the corner: the connection, the traffic with a graph, and what compression saves.
        /// </summary>
        Stats,

        /// <summary>
        /// The panel, plus markers and labels on every networked object in the world, in its owner's color.
        /// </summary>
        World,

        /// <summary>
        /// All of that, plus a window to browse the session, the players and every object's data.
        /// </summary>
        Inspector
    }
}
