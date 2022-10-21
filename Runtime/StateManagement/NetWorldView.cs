using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

public enum NetWorldViewInitMode
{
    Default, SeparatePhysicsSimulation,
}

public class NetWorldView : MonoBehaviour
{
    public event Action<NetObjectIdentity> NewObjectInitialized = delegate { };

    public bool ForceRenderingOff { get; private set; }
    public bool ForceMute { get; private set; }
    public float DrawDistance { get; private set; } = 50f;

    NetWorld m_World;
    NetObjectFactory m_ObjectFactory;
    bool m_Initialized;

    public IReadOnlyDictionary<NetObjectUID, NetObjectIdentity> CurrentObjectViews => m_CurrentObjectViews;
    Dictionary<NetObjectUID, NetObjectIdentity> m_CurrentObjectViews;
    public IReadOnlyDictionary<NetObjectUID, NetObjectIdentity> PendingObjectViews => m_PendingObjectViews;
    Dictionary<NetObjectUID, NetObjectIdentity> m_PendingObjectViews;

    Scene m_WorldScene;

    public void Init(NetWorld world, NetWorldViewInitMode initMode)
    {
        if (m_Initialized)
            throw new InvalidOperationException($"View for world {world.Name} already initialized");

        m_Initialized = true;

        m_World = world;
        m_ObjectFactory = new NetObjectFactory();
        m_CurrentObjectViews = new Dictionary<NetObjectUID, NetObjectIdentity>();
        m_PendingObjectViews = new Dictionary<NetObjectUID, NetObjectIdentity>();

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

        //Remove invalid views;
        ForEachViews((UID, view) =>
        {
            if (!ShoudUpdateViewForState(UID))
                RemoveView(UID);
        });

        //Create, update, destroy objects;
        ForEachStates((UID, state) =>
        {
            //Skip state;
            if (!ShoudUpdateViewForState(UID))
                return;

            //Try get or create new state view;
            if (!m_CurrentObjectViews.TryGetValue(UID, out var netObjectView))
            {
                if (m_PendingObjectViews.TryGetValue(UID, out netObjectView))
                {
                    m_PendingObjectViews.Remove(UID);
                    m_CurrentObjectViews.Add(UID, netObjectView);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(state.PrefabId))
                        return;

                    var position = Vector3.zero;
                    var rotation = Quaternion.identity;

                    if (state.DataCollection.TryGetData<NetObjectTransformData>(out var transformData))
                    {
                        position = transformData.Position.Value;
                        rotation = transformData.Rotation.Value;
                    }

                    netObjectView = m_ObjectFactory.Create(state.PrefabId, position, rotation);
                    m_CurrentObjectViews.Add(UID, netObjectView);
                }
            }

            //Initialize new view;
            if (netObjectView.Initialized)
            {
                if (!state.IsOwnedByLocalPlayer && state.DataCollection.TryGetData<NetObjectViewStateData>(out var viewData))
                    netObjectView.Deserialize(viewData.UnstructuredData, false, false);
            }
            else
                InitializeObjectView(netObjectView, state.UID);
        });

        Profiler.EndSample();
    }

    bool ShoudUpdateViewForState(NetObjectUID UID)
    {
        if (!m_World.TryGetObject(UID, out var netObjectState))
            return false;

        if (netObjectState.IsOwnedByLocalPlayer)
            return true;

        if (!netObjectState.DataCollection.TryGetData<NetObjectTransformData>(out var transformData))
            return true;

        var directionToCamera = Camera.main.transform.position - transformData.Position.Value;
        return directionToCamera.sqrMagnitude < DrawDistance * DrawDistance;
    }

    public NetObjectIdentity GetView(NetObjectUID uid)
    {
        if (m_CurrentObjectViews.TryGetValue(uid, out var netObjectView))
            return netObjectView;

        //Try add pending view;
        if (m_PendingObjectViews.TryGetValue(uid, out netObjectView))
        {
            m_PendingObjectViews.Remove(uid);
            m_CurrentObjectViews.Add(uid, netObjectView);
        }
        return netObjectView;
    }

    public NetObjectIdentity CreateImmidiate(NetObjectState state, Vector3 position, Quaternion rotation)
    {
        var netObjectView = m_ObjectFactory.Create(state.PrefabId, position, rotation);
        InitializeObjectView(netObjectView, state.UID);
        m_PendingObjectViews.Add(state.UID, netObjectView);
        return netObjectView;
    }

    public void InitializeObjectView(NetObjectIdentity identity, NetObjectUID stateUID)
    {
        if (identity.Initialized)
            throw new InvalidOperationException($"Indentity {identity.gameObject.name}:{stateUID} already initialized");

        //Set world as view parent;
        UpdateTransformInView(identity.transform);

        //Init;
        identity.Initialze(m_World, stateUID);

        //Push event;
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

    public void RegisterAsPending(NetObjectUID uid, NetObjectIdentity identity) => m_PendingObjectViews.Add(uid, identity);

    void ForEachViews(Action<NetObjectUID, NetObjectIdentity> processor)
    {
        foreach (var UID in m_CurrentObjectViews.Keys.ToArray())
            processor(UID, m_CurrentObjectViews[UID]);
    }

    void ForEachStates(Action<NetObjectUID, NetObjectState> processor) => m_World.ForEachStates(processor);

    void RemoveView(NetObjectUID uid)
    {
        if (m_CurrentObjectViews.TryGetValue(uid, out var cView))
        {
            m_CurrentObjectViews.Remove(uid);
            Destroy(cView.gameObject);
        }

        if (m_PendingObjectViews.TryGetValue(uid, out var pView))
        {
            m_PendingObjectViews.Remove(uid);
            Destroy(pView.gameObject);
        }
    }

    public void SetEnabledImmidiate(bool enabled)
    {
        SetVisibleImmediate(enabled);
        SetSoundEnabledImmediate(enabled);
    }

    public void SetDrawDistance(float value) => DrawDistance = value;

    public void SetVisibleImmediate(bool enabled)
    {
        ForceRenderingOff = !enabled;
        UpdateVisibilityFor(transform);
    }

    public void SetSoundEnabledImmediate(bool enabled)
    {
        ForceMute = !enabled;
        UpdateSoundFor(transform);
    }

    void UpdateVisibilityFor(Transform targetTransform)
    {
        foreach (var r in targetTransform.GetComponentsInChildren<Renderer>(true))
            r.forceRenderingOff = ForceRenderingOff;

        foreach(var l in targetTransform.GetComponentsInChildren<Light>(true))
            l.enabled = !ForceRenderingOff;
    }

    void UpdateSoundFor(Transform targetTransform)
    {
        foreach (var s in targetTransform.GetComponentsInChildren<AudioSource>(true))
            s.mute = ForceMute;
    }

    public void Destroy()
    {
        NewObjectInitialized = null;

        m_CurrentObjectViews.Clear();
        m_CurrentObjectViews = null;

        foreach (var pendingView in m_PendingObjectViews.Values)
            if (pendingView != null)
                Destroy(pendingView.gameObject);

        m_PendingObjectViews.Clear();
        m_PendingObjectViews = null;

        m_ObjectFactory = null;
        m_World = null;

        Destroy(gameObject);

        if (m_WorldScene.IsValid())
            SceneManager.UnloadSceneAsync(m_WorldScene);
    }

    public NetObjectIdentity FindObjectView(NetObjectUID instanceId) =>
       m_CurrentObjectViews.TryGetValue(instanceId, out NetObjectIdentity identity) ? identity : null;

    public T FindObjectView<T>(NetObjectUID instanceId) where T : GSInstanceBehaviourBase
    {
        var networkObject = FindObjectView(instanceId);
        return networkObject == null ? null : networkObject.GetComponent<T>();
    }

    public IEnumerable<T> FindObjectViewsOfType<T>() where T : GSInstanceBehaviourBase =>
        m_CurrentObjectViews.Values
            .Select(v => v.GetBehaviour<T>())
            .Where(b => b != null);
}