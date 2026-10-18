using System.Net;
using Keel.RelayNet.GameObjects;
using UnityEngine;

namespace Keel.RelayNet.Samples
{
    /// <summary>
    /// Starts a session and spawns one capsule for the local player. Without a host the session runs offline.
    /// </summary>
    public sealed class CapsulesDemo : MonoBehaviour
    {
        [SerializeField] private NetPrefabObjectWeakRef _capsule;

        [Tooltip("Address of the relay room. Leave empty to play offline.")]
        [SerializeField] private string _host;
        [SerializeField] private ushort _port;
        [SerializeField] private string _connectionToken;

        private NetSession _session;
        private NetPrefabDatabaseLoadRequest _prefabs;
        private bool _spawned;

        private void Start()
        {
            _prefabs = NetPrefabDatabaseLoadRequest.Create();
            _session = string.IsNullOrEmpty(_host)
                ? NetSession.CreateOffline()
                : new NetSession(IPAddress.Parse(_host), _port, _connectionToken);
        }

        private void Update()
        {
            _session.Update(Time.deltaTime);

            // Spawn once the relay has accepted us and the prefabs are loaded.
            var world = _session.MainWorld;
            if (_spawned || world?.Players.LocalPlayer == null || _prefabs is { IsDone: false })
                return;

            world.OnSceneLoaded();
            var position = new Vector3(Random.Range(-4f, 4f), 1f, Random.Range(-4f, 4f));
            world.CreateObject(_capsule, isSceneObject: false, position, Quaternion.identity);
            _spawned = true;
        }

        private void OnDestroy() =>
            _session?.Dispose();
    }
}
