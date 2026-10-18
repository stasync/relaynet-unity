using Keel.RelayNet.GameObjects;
using UnityEngine;

namespace Keel.RelayNet.Samples
{
    /// <summary>
    /// Spins the object on its owner. It's placed in the scene with a NetObjectSceneIdentity, so the master owns it at first,
    /// and another client takes it over when the owner leaves. NetObjectTransformSync sends the rotation.
    /// </summary>
    public sealed class Spinner : MonoBehaviour
    {
        [SerializeField] private float _degreesPerSecond = 90f;

        private NetObjectIdentity _identity;

        private void Awake() =>
            _identity = GetComponent<NetObjectIdentity>();

        private void Update()
        {
            if (_identity.IsLocal)
                transform.Rotate(0f, _degreesPerSecond * Time.deltaTime, 0f);
        }
    }
}
