using System;
using System.Collections.Generic;
using System.Linq;
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

        private NetWorld m_World;
        private NetObjectFactory m_ObjectFactory;
        private bool m_Initialized;

        public IReadOnlyDictionary<NetObjectUid, NetObjectIdentity> CurrentObjectViews => m_CurrentObjectViews;
        private Dictionary<NetObjectUid, NetObjectIdentity> m_CurrentObjectViews;
        public IReadOnlyDictionary<NetObjectUid, NetObjectIdentity> TemporaryUnregisteredViews => m_TemporaryUnregisteredViews;
        private Dictionary<NetObjectUid, NetObjectIdentity> m_TemporaryUnregisteredViews;

        private Scene m_WorldScene;

        public void Init(NetWorld world, NetWorldViewInitMode initMode)
        {
            if (m_Initialized)
                throw new InvalidOperationException($"View for world {world.Name} already initialized");

            m_Initialized = true;

            m_World = world;
            m_ObjectFactory = new NetObjectFactory();
            m_CurrentObjectViews = new Dictionary<NetObjectUid, NetObjectIdentity>();
            m_TemporaryUnregisteredViews = new Dictionary<NetObjectUid, NetObjectIdentity>();

            var sceneName = $"{m_World.Name} {gameObject.GetInstanceID()}";
            switch (initMode)
            {
                case NetWorldViewInitMode.Default:
                    m_WorldScene = SceneManager.CreateScene(sceneName);
                    break;

                case NetWorldViewInitMode.SeparatePhysicsSimulation:
                    m_WorldScene = SceneManager.CreateScene(sceneName, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                    break;
            }

            SceneManager.MoveGameObjectToScene(gameObject, m_WorldScene);
        }

        public void RebuildAccordingState()
        {
            Profiler.BeginSample($"{m_World.Name} View.Execute");

            // Remove invalid views.
            foreach (var uid in m_CurrentObjectViews.Keys.ToArray())
            {
                if (!ShouldUpdateViewForState(uid))
                    RemoveView(uid);
            }

            // Create, update, destroy objects.
            foreach (var (objectUid, state) in m_World.ObjectStates)
            {
                // Skip state.
                if (!ShouldUpdateViewForState(objectUid))
                    continue;

                // Try get or create new state view.
                if (!m_CurrentObjectViews.TryGetValue(objectUid, out var netObjectView))
                {
                    if (m_TemporaryUnregisteredViews.Remove(objectUid, out netObjectView))
                        m_CurrentObjectViews.Add(objectUid, netObjectView);
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

                        netObjectView = m_ObjectFactory.Create(state.PrefabId, position, rotation);
                        m_CurrentObjectViews.Add(objectUid, netObjectView);
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
            if (!m_World.TryGetObject(uid, out var netObjectState))
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
            if (m_CurrentObjectViews.TryGetValue(uid, out var netObjectView))
                return netObjectView;

            // Try to add pending view.
            if (m_TemporaryUnregisteredViews.Remove(uid, out netObjectView))
                m_CurrentObjectViews.Add(uid, netObjectView);

            return netObjectView;
        }

        public NetObjectIdentity CreateImmediate(NetObjectState state, Vector3 position, Quaternion rotation)
        {
            var netObjectView = m_ObjectFactory.Create(state.PrefabId, position, rotation);
            InitializeObjectView(netObjectView, state.Uid);
            m_TemporaryUnregisteredViews.Add(state.Uid, netObjectView);
            return netObjectView;
        }

        public void InitializeObjectView(NetObjectIdentity identity, NetObjectUid uid)
        {
            if (identity.Initialized)
                throw new InvalidOperationException($"Identity {identity.gameObject.name}:{uid} already initialized.");

            // Set world as view parent.
            UpdateTransformInView(identity.transform);

            // Init.
            identity.Initialize(m_World, uid);

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
            m_TemporaryUnregisteredViews.Add(uid, identity);

        private void RemoveView(NetObjectUid uid)
        {
            if (m_CurrentObjectViews.Remove(uid, out var cView))
                Destroy(cView.gameObject);

            if (m_TemporaryUnregisteredViews.Remove(uid, out var pView))
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

            m_CurrentObjectViews.Clear();
            m_CurrentObjectViews = null;

            foreach (var pendingView in m_TemporaryUnregisteredViews.Values)
            {
                if (pendingView != null)
                    Destroy(pendingView.gameObject);
            }

            m_TemporaryUnregisteredViews.Clear();
            m_TemporaryUnregisteredViews = null;

            m_ObjectFactory = null;
            m_World = null;

            Destroy(gameObject);

            if (m_WorldScene.IsValid())
                SceneManager.UnloadSceneAsync(m_WorldScene);
        }

        public NetObjectIdentity FindObjectView(NetObjectUid instanceId) =>
            m_CurrentObjectViews.GetValueOrDefault(instanceId);

        public T FindObjectView<T>(NetObjectUid instanceId) where T : GSInstanceBehaviourBase
        {
            var networkObject = FindObjectView(instanceId);
            return networkObject == null ? null : networkObject.GetComponent<T>();
        }

        public IEnumerable<T> FindObjectViewsOfType<T>() where T : GSInstanceBehaviourBase =>
            m_CurrentObjectViews.Values
                .Select(v => v.GetBehaviour<T>())
                .Where(b => b != null);
    }
}