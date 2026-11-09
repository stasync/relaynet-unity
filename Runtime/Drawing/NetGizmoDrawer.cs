using UnityEngine;

namespace Keel.RelayNet.Drawing
{
    /// <summary>
    /// The default <see cref="INetDebugDrawer"/>. It draws with <c>Debug.DrawLine</c>, like gizmos: the lines show in
    /// the editor's Scene view, and in the Game view while Gizmos is on. In a build, they draw nothing.
    /// </summary>
    public sealed class NetGizmoDrawer : INetDebugDrawer
    {
        public void DrawLine(Vector3 start, Vector3 end, Color color) =>
            UnityEngine.Debug.DrawLine(start, end, color, duration: 0f, depthTest: false);
    }
}
