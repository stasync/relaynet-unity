using Keel.RelayNet.GameObjects;
using Keel.RelayNet.StateManagement;
using Keel.RelayNet.StateManagement.Values;
using UnityEngine;

namespace Keel.RelayNet.Samples
{
    /// <summary>
    /// The capsule's networked state. The owner writes it, and every client receives it.
    /// </summary>
    public sealed class CapsuleData : NetObjectStateData
    {
        public readonly NetworkValue<Color> Color = new();
    }

    /// <summary>
    /// The owner picks the capsule's color and moves it with WASD or the arrow keys.
    /// Every client paints the capsule with the networked color. NetObjectTransformSync sends the movement.
    /// </summary>
    public sealed class Capsule : NetObjectBehaviour<CapsuleData>
    {
        [SerializeField] private float _speed = 5f;

        // Runs once, on the client that creates the capsule.
        protected override void OnPostInitializeOnce()
        {
            base.OnPostInitializeOnce();

            if (IsLocal)
                Data.Color.Value = Random.ColorHSV(hueMin: 0f, hueMax: 1f, saturationMin: 0.6f, saturationMax: 0.9f, valueMin: 0.9f, valueMax: 1f);
        }

        // Runs on every client, once the capsule's state is applied.
        protected override void OnPostInitialize()
        {
            base.OnPostInitialize();
            GetComponent<Renderer>().material.color = Data.Color.Value;
        }

        private void Update()
        {
            if (!IsLocal)
                return;

            var input = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
            transform.position += _speed * Time.deltaTime * input;
        }
    }
}
