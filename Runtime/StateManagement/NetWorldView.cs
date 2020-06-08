using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

public enum NetWorldViewInitMode
{
    Default, SeparateScene,
}

public class NetWorldView : MonoBehaviour
{
    public event Action<NetObjectIdentity> NewObjectInitialized = delegate { };

    public bool ForceRenderingOff { get; private set; }
    public bool ForceMute { get; private set; }

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

        switch (initMode)
        {
            case NetWorldViewInitMode.Default:
                DontDestroyOnLoad(gameObject);
                break;

            case NetWorldViewInitMode.SeparateScene:
                m_WorldScene = SceneManager.CreateScene($"{m_World.Name} scene", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                SceneManager.MoveGameObjectToScene(gameObject, m_WorldScene);
                break;
        }
    }

    public void Execute()
    {
        Profiler.BeginSample($"{m_World.Name} View.Execute");

        //Remove invalid views;
        ForEachViews((UID, view) =>
        {
            if (!m_World.ObjectExists(UID))
                RemoveView(UID);
        });

        //Create, update, destroy objects;
        ForEachStates((UID, state) =>
        {
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

                    netObjectView = m_ObjectFactory.Create(state.PrefabId, Vector3.zero, Quaternion.identity);
                    m_CurrentObjectViews.Add(UID, netObjectView);
                }
            }

            //Initialize new view;
            if (netObjectView.Initialized)
            {
                if (!state.IsOwnedByLocalPlayer)
                    netObjectView.Deserialize(state.Data, false, false);
            }
            else
                InitializeObjectView(netObjectView, state.UID, null);
        });

        Profiler.EndSample();
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

    public NetObjectIdentity CreateImmidiate(NetObjectState state, Vector3 position, Quaternion rotation, object[] additionalData)
    {
        var netObjectView = m_ObjectFactory.Create(state.PrefabId, position, rotation);
        InitializeObjectView(netObjectView, state.UID, additionalData);
        m_PendingObjectViews.Add(state.UID, netObjectView);
        return netObjectView;
    }

    public void InitializeObjectView(NetObjectIdentity identity, NetObjectUID stateUID, object[] additionalData)
    {
        if (identity.Initialized)
            throw new InvalidOperationException($"Indentity {identity.gameObject.name}:{stateUID} already initialized");

        //Set world as view parent;
        UpdateTransformInView(identity.transform);

        //Init;
        identity.Initialze(m_World, stateUID, additionalData);

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

    public void SetVisibleImmediate(bool enabled)
    {
        ForceRenderingOff = !enabled;
        ForEachViews((UID, v) => UpdateVisibilityFor(v.transform));
    }

    public void SetSoundEnabledImmediate(bool enabled)
    {
        ForceMute = !enabled;
        ForEachViews((UID, v) => UpdateSoundFor(v.transform));
    }

    void UpdateVisibilityFor(Transform targetTransform)
    {
        foreach (var r in targetTransform.GetComponentsInChildren<Renderer>(true))
            r.forceRenderingOff = ForceRenderingOff;
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

        if(m_WorldScene.IsValid())
            SceneManager.UnloadSceneAsync(m_WorldScene);
    }
}