using Keel.RelayNet.Drawing;
using Keel.RelayNet.StateManagement;
using UnityEngine;

namespace Keel.RelayNet.Debug
{
    /// <summary>
    /// Marks every networked object that syncs its transform: a square and a circle in its owner's color, and its axes.
    /// Draws through <see cref="NetDebugDraw.Drawer"/>, so it shows wherever that drawer draws.
    /// </summary>
    internal static class NetWorldMarkers
    {
        private const int CIRCLE_SEGMENTS = 12;

        public static void Draw(INetDebugDrawer drawer, Camera camera)
        {
            var cameraTransform = camera.transform;

            foreach (var world in NetWorld.All.Values)
            {
                foreach (var objectState in world.ObjectStates.Values)
                {
                    if (!objectState.DataCollection.TryGetData<NetObjectTransformData>(out var data))
                        continue;

                    var position = data.Position.Value;
                    var rotation = data.Rotation.Value;
                    var color = NetDebugColors.ForPlayer(objectState.Owner);

                    // The owner: a square that keeps its size on screen, and a circle on the ground.
                    var size = 0.02f * Mathf.Max(1f, Vector3.Distance(cameraTransform.position, position));
                    DrawSquare(drawer, position, cameraTransform.right * size, cameraTransform.up * size, color);
                    DrawCircle(drawer, position, radius: 0.5f, color);

                    // Which way it faces.
                    drawer.DrawLine(position, position + rotation * Vector3.forward, Color.blue);
                    drawer.DrawLine(position, position + rotation * Vector3.right, Color.red);
                    drawer.DrawLine(position, position + rotation * Vector3.up, Color.green);
                }
            }
        }

        private static void DrawSquare(INetDebugDrawer drawer, Vector3 center, Vector3 right, Vector3 up, Color color)
        {
            var p0 = center + right + up;
            var p1 = center + right - up;
            var p2 = center - right - up;
            var p3 = center - right + up;

            drawer.DrawLine(p0, p1, color);
            drawer.DrawLine(p1, p2, color);
            drawer.DrawLine(p2, p3, color);
            drawer.DrawLine(p3, p0, color);
        }

        private static void DrawCircle(INetDebugDrawer drawer, Vector3 center, float radius, Color color)
        {
            var step = Quaternion.AngleAxis(360f / CIRCLE_SEGMENTS, Vector3.up);
            var offset = Vector3.forward * radius;
            var previous = center + offset;

            for (var i = 0; i < CIRCLE_SEGMENTS; i++)
            {
                offset = step * offset;
                var current = center + offset;
                drawer.DrawLine(previous, current, color);
                previous = current;
            }
        }
    }
}
