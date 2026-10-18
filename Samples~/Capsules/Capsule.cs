using Keel.RelayNet.GameObjects;
using UnityEngine;

namespace Keel.RelayNet.Samples
{
    /// <summary>
    /// Moves the capsule with WASD or the arrow keys, on the owner only. NetObjectTransformSync sends the movement to the others.
    /// </summary>
    public sealed class CapsuleMover : MonoBehaviour
    {
        [SerializeField] private float _speed = 5f;

        private NetObjectIdentity _identity;

        private void Awake() =>
            _identity = GetComponent<NetObjectIdentity>();

        private void Update()
        {
            if (!_identity.IsLocal)
                return;

            var input = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
            transform.position += _speed * Time.deltaTime * input;
        }
    }
}
