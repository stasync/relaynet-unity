using LROTech.Network.StateManagement;
using LROTech.RuntimeDebug;
using UnityEngine;

namespace LROTech.NetworkDebug
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

        private static bool s_enabled;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.LeftShift))
            {
                if (Input.GetKeyDown(KeyCode.V))
                    s_enabled = !s_enabled;
            }
        }

        private void LateUpdate()
        {
            //if (!s_enabled)
            //   return;

            foreach (var world in NetWorld.All.Values)
            {
                foreach (var objectState in world.ObjectStates.Values)
                {
                    if (objectState.DataCollection.TryGetData<NetObjectTransformData>(out var data))
                    {
                        Draw.Quad(data.Position.Value, Color.red, size: 0.25f, depthTest: false);

                        var forward = data.Rotation.Value * Vector3.forward;
                        var right = data.Rotation.Value * Vector3.right;
                        var up = data.Rotation.Value * Vector3.up;

                        Draw.DrawRay(data.Position.Value, direction: forward, color: Color.blue);
                        Draw.DrawRay(data.Position.Value, direction: right, color: Color.red);
                        Draw.DrawRay(data.Position.Value, direction: up, color: Color.green);
                    }
                }
            }
        }
    }
}