using LROTech.Network.StateManagement;
using LROTech.RuntimeDebug;
using UnityEngine;

namespace LROTech.Network.Debug
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

                            Draw.Quad(data.Position.Value, color, size: 0.02f * sizeDistanceFactor, depthTest: false);
                            Draw.Circle(data.Position.Value, radius: 0.5f, normal: Vector3.up, color, depthTest: false);
                        }

                        // Transformation.
                        {
                            var forward = data.Rotation.Value * Vector3.forward;
                            var right = data.Rotation.Value * Vector3.right;
                            var up = data.Rotation.Value * Vector3.up;

                            Draw.Ray(data.Position.Value, direction: forward, color: Color.blue);
                            Draw.Ray(data.Position.Value, direction: right, color: Color.red);
                            Draw.Ray(data.Position.Value, direction: up, color: Color.green);
                        }
                    }
                }
            }
        }

        internal static Color GetColorForPlayer(NetWorldPlayer player) =>
            player == null ? Color.gray : s_perClientColors[player.Uid.GetHashCode() % s_perClientColors.Length];
    }
}