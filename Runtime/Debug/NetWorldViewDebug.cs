using Keel.RelayNet.StateManagement;
using UnityEngine;

namespace Keel.RelayNet.Debug
{
    [DisallowMultipleComponent]
    internal sealed class NetWorldViewDebug : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            var container = new GameObject(typeof(NetWorldViewDebug).FullName);
            container.AddComponent<NetWorldViewDebug>();
            DontDestroyOnLoad(container);
        }

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

        private static bool s_enabled = true;

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.LeftShift))
                return;

            if (Input.GetKeyDown(KeyCode.V))
                s_enabled = !s_enabled;
        }

        private void LateUpdate()
        {
            if (!s_enabled)
                return;

            // The markers face the main camera - without one there is nothing to draw them for.
            if (Camera.main == null)
                return;

            foreach (var world in NetWorld.All.Values)
            {
                foreach (var objectState in world.ObjectStates.Values)
                {
                    var color = GetColorForPlayer(objectState.Owner);

                    if (objectState.DataCollection.TryGetData<NetObjectTransformData>(out var data))
                    {
                        // Ownership indication.
                        {
                            var directionToCamera = Camera.main!.transform.position - data.Position.Value;
                            var sizeDistanceFactor = directionToCamera.magnitude;
                            if (sizeDistanceFactor < 1)
                                sizeDistanceFactor = 1f;

                            DrawQuad(data.Position.Value, color, size: 0.02f * sizeDistanceFactor);
                            DrawCircle(data.Position.Value, radius: 0.5f, normal: Vector3.up, color);
                        }

                        // Transformation.
                        {
                            var forward = data.Rotation.Value * Vector3.forward;
                            var right = data.Rotation.Value * Vector3.right;
                            var up = data.Rotation.Value * Vector3.up;

                            UnityEngine.Debug.DrawRay(data.Position.Value, forward, Color.blue, duration: 0f, depthTest: false);
                            UnityEngine.Debug.DrawRay(data.Position.Value, right, Color.red, duration: 0f, depthTest: false);
                            UnityEngine.Debug.DrawRay(data.Position.Value, up, Color.green, duration: 0f, depthTest: false);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// A camera-facing square outline. 'Debug.DrawLine' only shows in the editor (Scene view, and Game view with gizmos on).
        /// </summary>
        private static void DrawQuad(Vector3 position, Color color, float size)
        {
            var cameraTransform = Camera.main!.transform;
            var right = cameraTransform.right * size;
            var up = cameraTransform.up * size;

            var p0 = position + right + up;
            var p1 = position + right - up;
            var p2 = position - right - up;
            var p3 = position - right + up;

            UnityEngine.Debug.DrawLine(p0, p1, color, duration: 0f, depthTest: false);
            UnityEngine.Debug.DrawLine(p1, p2, color, duration: 0f, depthTest: false);
            UnityEngine.Debug.DrawLine(p2, p3, color, duration: 0f, depthTest: false);
            UnityEngine.Debug.DrawLine(p3, p0, color, duration: 0f, depthTest: false);
        }

        private static void DrawCircle(Vector3 position, float radius, Vector3 normal, Color color)
        {
            var from = Vector3.Cross(normal, Vector3.up);
            if (from.sqrMagnitude < 0.001f)
                from = Vector3.Cross(normal, Vector3.right);

            const int count = 12;
            var rotation = Quaternion.AngleAxis(360f / (count - 1), normal);
            var vector = from.normalized * radius;
            var previous = position + vector;

            for (var i = 1; i < count; i++)
            {
                vector = rotation * vector;
                var current = position + vector;
                UnityEngine.Debug.DrawLine(previous, current, color, duration: 0f, depthTest: false);
                previous = current;
            }
        }

        internal static Color GetColorForPlayer(NetWorldPlayer player) =>
            player == null ? Color.gray : s_perClientColors[player.Uid.GetHashCode() % s_perClientColors.Length];
    }
}