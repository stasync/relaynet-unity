using System;
using System.Collections.Generic;
using LROTech.Network.GameObjects;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace LROTech.Network.StateManagement
{
    public sealed class NetWorldView : MonoBehaviour
    {
        public event Action<NetObjectIdentity> NewObjectInitialized = delegate { };

        public bool ForceRenderingOff { get; private set; }
        public bool ForceMute { get; private set; }
        public float DrawDistance { get; private set; } = 50f;

        private NetWorld _world;
        private bool _initialized;

        private readonly NetObjectFactory _objectFactory = new();

        public IReadOnlyDictionary<NetObjectUid, NetObjectIdentity> CurrentObjectViews => _currentObjectViews;
        private readonly Dictionary<NetObjectUid, NetObjectIdentity> _currentObjectViews = new();

        public IReadOnlyDictionary<NetObjectUid, NetObjectIdentity> TemporaryUnregisteredViews => _temporaryUnregisteredViews;
        private readonly Dictionary<NetObjectUid, NetObjectIdentity> _temporaryUnregisteredViews = new();

        private Scene _worldScene;
        private readonly List<NetObjectUid> _currentObjectViewsIterator = new();

        internal void Init(NetWorld world, NetWorldViewInitMode initMode)
        {
            if (_initialized)
                throw new InvalidOperationException($"View for world {world.Name} already initialized");

            _initialized = true;

            _world = world;

            var sceneName = $"[{_world.Uid}] {_world.Name}";
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
            Profiler.BeginSample($"[{_world.Uid}] {_world.Name} View.{nameof(RebuildAccordingState)}");

            // Remove invalid views.
            _currentObjectViewsIterator.Clear();
            _currentObjectViewsIterator.AddRange(_currentObjectViews.Keys);
            foreach (var uid in _currentObjectViewsIterator)
            {
                // No need to update state, or the state reference was invalidated.
                if (!ShouldUpdateViewForState(uid) || !_currentObjectViews[uid].InstanceId.IsValid)
                    RemoveView(uid);
                
                // TODO: Remove also if object too outdated? Wold time much greater than the update time.
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
                        netObjectView.Deserialize(viewData.UnstructuredData, force: false);
                }
                else
                    InitializeObjectView(state.Uid, netObjectView);
            }

            Profiler.EndSample();
        }

        private bool ShouldUpdateViewForState(NetObjectUid uid)
        {
            var netObjectState = _world.GetObject(uid);
            
            if (netObjectState == null)
                return false;

            if (netObjectState.IsOwnedByLocalPlayer)
                return true;

            if (!netObjectState.DataCollection.TryGetData<NetObjectTransformData>(out var transformData))
                return true;

            if (_temporaryUnregisteredViews.ContainsKey(uid))
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

        internal NetObjectIdentity CreateObjectViewImmediate(NetObjectState state, Vector3 position, Quaternion rotation)
        {
            var currentView = GetView(state.Uid);
            if (currentView != null)
                throw new InvalidOperationException($"View '{currentView.gameObject.name}' is already registered for object with uid '{state.Uid}'.");

            var netObjectView = _objectFactory.Create(state.PrefabId, position, rotation);
            InitializeObjectView(state.Uid, netObjectView);
            _temporaryUnregisteredViews.Add(state.Uid, netObjectView);
            return netObjectView;
        }

        internal void RegisterExistedObjectView(NetObjectState state, NetObjectIdentity identity)
        {
            var currentView = GetView(state.Uid);
            if (currentView != null)
                throw new InvalidOperationException($"View '{currentView.gameObject.name}' is already registered for object with uid '{state.Uid}'.");

            InitializeObjectView(state.Uid, identity);
            _temporaryUnregisteredViews.Add(state.Uid, identity);
        }

        private void InitializeObjectView(NetObjectUid uid, NetObjectIdentity identity)
        {
            if (identity.Initialized)
                throw new InvalidOperationException($"Identity '{identity.gameObject.name}' with id '{uid}' is already initialized.");

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

        private void RemoveView(NetObjectUid uid)
        {
            if (_currentObjectViews.Remove(uid, out var view))
                Destroy(view.gameObject);

            if (_temporaryUnregisteredViews.Remove(uid, out var unregisteredView))
                Destroy(unregisteredView.gameObject);
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
            foreach (var audioSource in targetTransform.GetComponentsInChildren<AudioSource>(includeInactive: true))
                audioSource.mute = ForceMute;
        }

        internal void Destroy()
        {
            NewObjectInitialized = null;

            _currentObjectViews.Clear();

            foreach (var pendingView in _temporaryUnregisteredViews.Values)
            {
                if (pendingView != null)
                    Destroy(pendingView.gameObject);
            }

            _temporaryUnregisteredViews.Clear();

            _world = null;

            Destroy(gameObject);
            if (_worldScene.IsValid())
                SceneManager.UnloadSceneAsync(_worldScene);
        }

        public NetObjectIdentity GetObjectView(NetObjectUid instanceId) =>
            _currentObjectViews.GetValueOrDefault(instanceId);

        public T GetObjectView<T>(NetObjectUid instanceId) where T : NetObjectBehaviourBase
        {
            var networkObject = GetObjectView(instanceId);
            return networkObject == null ? null : networkObject.GetComponent<T>();
        }
    }
}