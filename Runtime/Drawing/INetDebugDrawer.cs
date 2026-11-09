using UnityEngine;

namespace Keel.RelayNet.Drawing
{
    /// <summary>
    /// Draws the lines of RelayNet's debug markers - who owns which object, and which way it faces. Implement it to
    /// draw them with your own renderer, for example one that also works in builds, and install it as
    /// <see cref="NetDebugDraw.Drawer"/>.
    /// </summary>
    public interface INetDebugDrawer
    {
        /// <summary>
        /// Draws a line for the current frame only, on top of the scene. Called every frame for every line, between
        /// <c>Update</c> and rendering.
        /// </summary>
        void DrawLine(Vector3 start, Vector3 end, Color color);
    }
}
