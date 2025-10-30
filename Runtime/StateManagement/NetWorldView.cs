using System;
using System.Collections.Generic;
using System.Linq;
using LROTech.Network.GameObjects;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace LROTech.Network.StateManagement
{
    public class NetWorldView : MonoBehaviour
    {
        public event Action<NetObjectIdentity> NewObjectInitialized = delegate { };

        public bool ForceRenderingOff { get; private set; }
        public bool ForceMute { get; private set; }
        public float DrawDistance { get; private set; } = 50f;

        private NetWorld _world;
        private NetObjectFactory _objectFactory;
        private bool _initialized;

        public IReadOnlyDictionary<NetObjectUid, NetObjectIdentity> CurrentObjectViews => _currentObjectViews;
        private Dictionary<NetObjectUid, NetObjectIdentity> _currentObjectViews;
        public IReadOnlyDictionary<NetObjectUid, NetObjectIdentity> TemporaryUnregisteredViews => _temporaryUnregisteredViews;
        private Dictionary<NetObjectUid, NetObjectIdentity> _temporaryUnregisteredViews;

        private Scene _worldScene;

        public void Init(NetWorld world, NetWorldViewInitMode initMode)
        {
            if (_initialized)
                throw new InvalidOperationException($"View for world {world.Name} already initialized");

            _initialized = true;

            _world = world;
            _objectFactory = new NetObjectFactory();
            _currentObjectViews = new Dictionary<NetObjectUid, NetObjectIdentity>();
            _temporaryUnregisteredViews = new Dictionary<NetObjectUid, NetObjectIdentity>();

            var sceneName = $"{_world.Name} {gameObject.GetInstanceID()}";
            switch (initMode)
            {
                case NetWorldViewInitMode.Default:
                    _worldScene = SceneManager.CreateScene(sceneName);
                    break;

                case NetWorldViewInitMode.SeparatePhysicsSimulation:
                    _worldScene = SceneManager.CreateScene(sceneName, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                    break;
            }

            SceneManager.MoveGameObjectToScene(gameObject, _worldScene);
        }

        public void RebuildAccordingState()
        {
            Profiler.BeginSample($"{_world.Name} View.Execute");

            // Remove invalid views.
            foreach (var uid in _currentObjectViews.Keys.ToArray())
            {
                if (!ShouldUpdateViewForState(uid))
                    RemoveView(uid);
            }

            // Create, update, destroy objects.
            foreach (var (objectUid, state) in _world.ObjectStates)
            {
                // Skip state.
                if (!ShouldUpdateViewForState(objectUid))
                    continue;

                // Try get or create new state view.
                if (!_currentObjectViews.TryGetValue(objectUid, out var netObjectView))
                {
                    if (_temporaryUnregisteredViews.Remove(objectUid, out netObjectView))
                        _currentObjectViews.Add(objectUid, netObjectView);
                    else
                    {
                        if (string.IsNullOrWhiteSpace(state.PrefabId))
                            continue;

                        var position = Vector3.zero;
                        var rotation = Quaternion.identity;

                        if (state.DataCollection.TryGetData<NetObjectTransformData>(out var transformData))
                        {
                            position = transformData.Position.Value;
                            rotation = transformData.Rotation.Value;
                        }

                        netObjectView = _objectFactory.Create(state.PrefabId, position, rotation);
                        _currentObjectViews.Add(objectUid, netObjectView);
                    }
                }

                // Initialize new view.
                if (netObjectView.Initialized)
                {
                    if (!state.IsOwnedByLocalPlayer && state.DataCollection.TryGetData<NetObjectViewStateData>(out var viewData))
                        netObjectView.Deserialize(viewData.UnstructuredData, force: false, firstTime: false);
                }
                else
                    InitializeObjectView(netObjectView, state.Uid);
            }

            Profiler.EndSample();
        }

        private bool ShouldUpdateViewForState(NetObjectUid uid)
        {
            if (!_world.TryGetObject(uid, out var netObjectState))
                return false;

            if (netObjectState.IsOwnedByLocalPlayer)
                return true;

            if (!netObjectState.DataCollection.TryGetData<NetObjectTransformData>(out var transformData))
                return true;

            var directionToCamera = Camera.main!.transform.position - transformData.Position.Value;
            return directionToCamera.sqrMagnitude < DrawDistance * DrawDistance;
        }

        public NetObjectIdentity GetView(NetObjectUid uid)
        {
            if (_currentObjectViews.TryGetValue(uid, out var netObjectView))
                return netObjectView;

            // Try to add pending view.
            if (_temporaryUnregisteredViews.Remove(uid, out netObjectView))
                _currentObjectViews.Add(uid, netObjectView);

            return netObjectView;
        }

        public NetObjectIdentity CreateImmediate(NetObjectState state, Vector3 position, Quaternion rotation)
        {
            var netObjectView = _objectFactory.Create(state.PrefabId, position, rotation);
            InitializeObjectView(netObjectView, state.Uid);
            _temporaryUnregisteredViews.Add(state.Uid, netObjectView);
            return netObjectView;
        }

        public void InitializeObjectView(NetObjectIdentity identity, NetObjectUid uid)
        {
            if (identity.Initialized)
                throw new InvalidOperationException($"Identity {identity.gameObject.name}:{uid} already initialized.");

            // Set world as view parent.
            UpdateTransformInView(identity.transform);

            // Init.
            identity.Initialize(_world, uid);

            // Push event.
            NewObjectInitialized(identity);
        }

        public GameObject InstantiateGameObjectInView(GameObject original, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            var newInstance = Instantiate(original, position, rotation);
            UpdateTransformInView(newInstance.transform);
            if (parent != null)
                newInstance.transform.SetParent(parent);

            return newInstance;
        }

        public void UpdateTransformInView(Transform targetTransform)
        {
            targetTransform.SetParent(transform);
            UpdateVisibilityFor(targetTransform);
            UpdateSoundFor(targetTransform);
        }

        public void RegisterAsPending(NetObjectUid uid, NetObjectIdentity identity) =>
            _temporaryUnregisteredViews.Add(uid, identity);

        private void RemoveView(NetObjectUid uid)
        {
            if (_currentObjectViews.Remove(uid, out var cView))
                Destroy(cView.gameObject);

            if (_temporaryUnregisteredViews.Remove(uid, out var pView))
                Destroy(pView.gameObject);
        }

        public void SetEnabledImmediate(bool value)
        {
            SetVisibleImmediate(value);
            SetSoundEnabledImmediate(value);
        }

        public void SetDrawDistance(float value) => DrawDistance = value;

        public void SetVisibleImmediate(bool value)
        {
            ForceRenderingOff = !value;
            UpdateVisibilityFor(transform);
        }

        public void SetSoundEnabledImmediate(bool value)
        {
            ForceMute = !value;
            UpdateSoundFor(transform);
        }

        private void UpdateVisibilityFor(Transform targetTransform)
        {
            foreach (var renderedComponent in targetTransform.GetComponentsInChildren<Renderer>(true))
                renderedComponent.forceRenderingOff = ForceRenderingOff;

            foreach (var lightComponent in targetTransform.GetComponentsInChildren<Light>(true))
                lightComponent.enabled = !ForceRenderingOff;
        }

        private void UpdateSoundFor(Transform targetTransform)
        {
            foreach (var audioSource in targetTransform.GetComponentsInChildren<AudioSource>(true))
                audioSource.mute = ForceMute;
        }

        public void Destroy()
        {
            NewObjectInitialized = null;

            _currentObjectViews.Clear();
            _currentObjectViews = null;

            foreach (var pendingView in _temporaryUnregisteredViews.Values)
            {
                if (pendingView != null)
                    Destroy(pendingView.gameObject);
            }

            _temporaryUnregisteredViews.Clear();
            _temporaryUnregisteredViews = null;

            _objectFactory = null;
            _world = null;

            Destroy(gameObject);

            if (_worldScene.IsValid())
                SceneManager.UnloadSceneAsync(_worldScene);
        }

        public NetObjectIdentity FindObjectView(NetObjectUid instanceId) =>
            _currentObjectViews.GetValueOrDefault(instanceId);

        public T FindObjectView<T>(NetObjectUid instanceId) where T : NetObjectBehaviourBase
        {
            var networkObject = FindObjectView(instanceId);
            return networkObject == null ? null : networkObject.GetComponent<T>();
        }

        public IEnumerable<T> FindObjectViewsOfType<T>() where T : NetObjectBehaviourBase =>
            _currentObjectViews.Values
                .Select(v => v.GetBehaviour<T>())
                .Where(b => b != null);
    }
}