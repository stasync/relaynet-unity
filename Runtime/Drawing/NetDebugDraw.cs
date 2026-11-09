namespace Keel.RelayNet.Drawing
{
    /// <summary>
    /// Where RelayNet's debug tools draw. It's in every build, so a game can install its drawer without checking for
    /// the debug tools, which only exist in the editor and in builds with <c>RELAYNET_DEBUG</c>.
    /// </summary>
    public static class NetDebugDraw
    {
        /// <summary>
        /// The drawer the debug markers go through. A <see cref="NetGizmoDrawer"/> by default; null draws nothing.
        /// </summary>
        public static INetDebugDrawer Drawer { get; set; } = new NetGizmoDrawer();
    }
}
