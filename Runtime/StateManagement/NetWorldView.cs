using System;
using System.Collections.Generic;
using Keel.RelayNet.GameObjects;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace Keel.RelayNet.StateManagement
{
    public sealed class NetWorldView : MonoBehaviour
    {
        public event Action<NetObjectIdentity> NewObjectInitialized = delegate { };

        public bool ForceRenderingOff { get; private set; }
        public bool ForceMute { get; private set; }
        public float DrawDistance { get; private set; } = 50f;

        private NetWorld _world;
        private bool _initialized;

        public IReadOnlyDictionary<NetObjectUid, NetObjectIdentity> CurrentObjectViews => _currentObjectViews;
        private readonly Dictionary<NetObjectUid, NetObjectIdentity> _currentObjectViews = new();

        public IReadOnlyDictionary<NetObjectUid, NetObjectIdentity> TemporaryUnregisteredViews => _temporaryUnregisteredViews;
        private readonly Dictionary<NetObjectUid, NetObjectIdentity> _temporaryUnregisteredViews = new();

        private Scene _worldScene;
        private readonly List<NetObjectUid> _currentObjectViewsIterator = new();

        private readonly Dictionary<NetObjectUid, AsyncInstantiateOperation<GameObject>> _pendingObjectViews = new();

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
                // No need to update the state, or the state reference was invalidated.
                if (!ShouldUpdateViewForState(uid) || !_currentObjectViews[uid].InstanceId.IsValid)
                    RemoveView(uid);
            }

            // Create, update, destroy objects.
            foreach (var (objectUid, state) in _world.ObjectStates)
            {
                // Skip state.
                if (!ShouldUpdateViewForState(objectUid))
                    continue;

                // Try to get or create a new state view.
                if (!_currentObjectViews.TryGetValue(objectUid, out var netObjectView))
                {
                    if (_temporaryUnregisteredViews.Remove(objectUid, out netObjectView))
                        _currentObjectViews.Add(objectUid, netObjectView);
                    else
                    {
                        // The instance lands some frames later, so there is nothing to initialize this time round.
                        // Only request the view if the object is owned by the local player or if it has view data.
                        if (state.IsOwnedByLocalPlayer || state.DataCollection.HasData<NetObjectViewStateData>())
                            RequestObjectView(state);

                        // Skip this iteration - we have to wait for the view to be created.
                        continue;
                    }
                }

                // Initialize a new view.
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

            // This is an intentional condition.
            // If the transform data is not available, we can't take into account its distance to the camera.
            if (!netObjectState.DataCollection.TryGetData<NetObjectTransformData>(out var transformData))
                return true;

            if (_temporaryUnregisteredViews.ContainsKey(uid))
                return true;

            // No camera, no distance to measure.
            var mainCamera = Camera.main;
            if (mainCamera == null)
                return true;

            var directionToCamera = mainCamera.transform.position - transformData.Position.Value;
            return directionToCamera.sqrMagnitude < DrawDistance * DrawDistance;
        }

        private void RequestObjectView(NetObjectState state)
        {
            if (state.PrefabUid == 0)
                return;

            // Already on its way. While in flight this is the answer on every world update, hence the early out.
            if (_pendingObjectViews.ContainsKey(state.Uid))
                return;

            var position = Vector3.zero;
            var rotation = Quaternion.identity;

            if (state.DataCollection.TryGetData<NetObjectTransformData>(out var transformData))
            {
                position = transformData.Position.Value;
                rotation = transformData.Rotation.Value;
            }

            // Start async operation.
            var asyncOperation = InstantiateAsync(original: GetViewPrefab(state.PrefabUid), position, rotation,
                new InstantiateParameters
                {
                    parent = transform,
                    worldSpace = true
                });

            _pendingObjectViews.Add(state.Uid, asyncOperation);
            asyncOperation.completed += _ => OnObjectViewInstantiated(state.Uid, asyncOperation);
        }

        private void RequestObjectViewOverrideTransform(NetObjectState state, Vector3 position, Quaternion rotation)
        {
            if (state.PrefabUid == 0)
                return;

            // Already on its way. While in flight this is the answer on every world update, hence the early out.
            if (_pendingObjectViews.ContainsKey(state.Uid))
                return;

            // Start async operation.
            var asyncOperation = InstantiateAsync(original: GetViewPrefab(state.PrefabUid), position, rotation,
                new InstantiateParameters
                {
                    parent = transform,
                    worldSpace = true
                });

            _pendingObjectViews.Add(state.Uid, asyncOperation);
            asyncOperation.completed += _ => OnObjectViewInstantiated(state.Uid, asyncOperation);
        }

        private void OnObjectViewInstantiated(NetObjectUid uid, AsyncInstantiateOperation<GameObject> operation)
        {
            // We may cancel all async operations in OnDestroy.
            // AsyncOperation.Completed event will be called in this case as well, thus we need to check for null to avoid running this code upon destruction.
            if (_world == null)
                return;

            var instance = operation.Result is { Length: > 0 } result ? result[0] : null;
            _pendingObjectViews.Remove(uid);

            if (instance == null)
                return;

            if (!ShouldUpdateViewForState(uid) || _currentObjectViews.ContainsKey(uid) || _temporaryUnregisteredViews.ContainsKey(uid))
            {
                Destroy(instance);
                return;
            }

            var netObjectView = instance.GetComponent<NetObjectIdentity>();
            _temporaryUnregisteredViews.Add(uid, netObjectView);
            InitializeObjectView(uid, netObjectView);
        }

        private NetObjectIdentity CreateObjectView(uint prefabId, Vector3 position, Quaternion rotation)
        {
            var prefabObject = GetViewPrefab(prefabId);
            var newGameObject = Instantiate(prefabObject, position, rotation);
            return newGameObject.GetComponent<NetObjectIdentity>();
        }

        private GameObject GetViewPrefab(uint prefabId)
        {
            if (!NetPrefabDatabase.TryGetPrefab(prefabId, out var prefabObject) || prefabObject == null)
                throw new NullReferenceException($"Object with prefab id '{prefabId}' is not registered in database.");

            if (!prefabObject.TryGetComponent<NetObjectIdentity>(out _))
                throw new InvalidOperationException($"Unable to create object without '{typeof(NetObjectIdentity).FullName}' component.");

            return prefabObject;
        }

        internal NetObjectIdentity CreateObjectViewForState(NetObjectState state, Vector3 position, Quaternion rotation)
        {
            var currentView = GetObjectView(state.Uid);
            if (currentView != null)
                throw new InvalidOperationException($"View '{currentView.gameObject.name}' is already registered for object with uid '{state.Uid}'.");

            var netObjectView = CreateObjectView(state.PrefabUid, position, rotation);
            _temporaryUnregisteredViews.Add(state.Uid, netObjectView);
            InitializeObjectView(state.Uid, netObjectView);
            return netObjectView;
        }

        internal void CreateObjectViewForStateAsync(NetObjectState state, Vector3 position, Quaternion rotation, Action<NetObjectIdentity> callback)
        {
            var currentView = GetObjectView(state.Uid);
            if (currentView != null)
                throw new InvalidOperationException($"View '{currentView.gameObject.name}' is already registered for object with uid '{state.Uid}'.");

            RequestObjectViewOverrideTransform(state, position, rotation);
            if (callback == null)
                return;

            _pendingObjectViews[state.Uid].completed += _ =>
            {
                var netObjectView = GetObjectView(state.Uid);
                if (netObjectView == null)
                    throw new InvalidOperationException($"View for object with uid '{state.Uid}' was not created.");

                callback(netObjectView);
            };
        }

        internal void RegisterExistedObjectView(NetObjectState state, NetObjectIdentity identity)
        {
            var currentView = GetObjectView(state.Uid);
            if (currentView != null)
                throw new InvalidOperationException($"View '{currentView.gameObject.name}' is already registered for object with uid '{state.Uid}'.");

            _temporaryUnregisteredViews.Add(state.Uid, identity);
            InitializeObjectView(state.Uid, identity);
        }

        private void InitializeObjectView(NetObjectUid uid, NetObjectIdentity identity)
        {
            if (identity.Initialized)
                throw new InvalidOperationException($"Identity '{identity.gameObject.name}' with id '{uid}' is already initialized.");

            // Set the world as view parent.
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
                DestroyView(view);

            if (_temporaryUnregisteredViews.Remove(uid, out var unregisteredView))
                DestroyView(unregisteredView);

            if (_pendingObjectViews.Remove(uid, out var asyncInstantiateOperation))
                asyncInstantiateOperation.Cancel();

            return;

            void DestroyView(NetObjectIdentity viewToDestroy)
            {
                foreach (var nestedView in viewToDestroy.GetComponentsInChildren<NetObjectIdentity>(includeInactive: true))
                {
                    // This will correctly unparent the object.
                    if (nestedView != viewToDestroy)
                        UpdateTransformInView(nestedView.transform);
                }

                Destroy(viewToDestroy.gameObject);
            }
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

            Destroy(gameObject);
            if (_worldScene.IsValid())
                SceneManager.UnloadSceneAsync(_worldScene);
        }

        private void OnDestroy()
        {
            _world = null;

            // Cancel all pending object instantiations after the world is destroyed (reference set to null).
            foreach (var asyncLoadOp in _pendingObjectViews.Values)
                asyncLoadOp.Cancel();

            _pendingObjectViews.Clear();
        }

        public NetObjectIdentity GetObjectView(NetObjectUid uid)
        {
            if (_currentObjectViews.TryGetValue(uid, out var netObjectView))
                return netObjectView;

            // Try to add pending view.
            if (_temporaryUnregisteredViews.Remove(uid, out netObjectView))
                _currentObjectViews.Add(uid, netObjectView);

            return netObjectView;
        }

        public T GetObjectView<T>(NetObjectUid instanceId) where T : NetObjectBehaviourBase
        {
            var networkObject = GetObjectView(instanceId);
            return networkObject == null ? null : networkObject.GetComponent<T>();
        }
    }
}