using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class NetWorldView : MonoBehaviour
{
    public event Action<NetObjectIdentity> NewObjectInitialized = delegate { };

    NetWorld m_World;
    NetObjectFactory m_ObjectFactory;

    public IReadOnlyDictionary<NetObjectUID, NetObjectIdentity> CurrentObjectViews => m_CurrentObjectViews;
    Dictionary<NetObjectUID, NetObjectIdentity> m_CurrentObjectViews;
    public IReadOnlyDictionary<NetObjectUID, NetObjectIdentity> PendingObjectViews => m_PendingObjectViews;
    Dictionary<NetObjectUID, NetObjectIdentity> m_PendingObjectViews;

    public void Init(NetWorld world)
    {
        DontDestroyOnLoad(gameObject);

        m_World = world;
        m_ObjectFactory = new NetObjectFactory();
        m_CurrentObjectViews = new Dictionary<NetObjectUID, NetObjectIdentity>();
        m_PendingObjectViews = new Dictionary<NetObjectUID, NetObjectIdentity>();
    }

    public void Execute()
    {
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
                    if (state.UID.IsSceneObjectSpaceId)
                        Debug.LogError($"Unable to replicate state {state.ToString()} becase it is a scene object");
                    else
                    {
                        netObjectView = m_ObjectFactory.Create(state.PrefabId, Vector3.zero, Quaternion.identity);
                        m_CurrentObjectViews.Add(UID, netObjectView);
                    }
                }
            }

            if (netObjectView == null)
                return;

            //Initialize new view;
            if (netObjectView.Initialized)
            {
                if (!state.IsOwnedByLocalPlayer)
                    netObjectView.Deserialize(state.Data, false, false);
            }
            else
                InitializeView(netObjectView, state, null);

            //Report to player;
            {
                var senderPlayer = m_Sender.FindPlayer(state.OwnerUID);
                if (senderPlayer == null)
                {
                    Debug.LogError($"Unable to update object state for player with UID {state.OwnerUID}. Player doesn't exists");
                    return;
                }

                if (!senderPlayer.IsLocal)
                    senderPlayer.OnObjectCreateUpdate(netObjectView);
            }
        });
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
        InitializeView(netObjectView, state, additionalData);
        m_PendingObjectViews.Add(state.UID, netObjectView);
        return netObjectView;
    }

    public void InitializeView(NetObjectIdentity identity, NetObjectState state, object[] additionalData)
    {
        if (identity.Initialized)
            throw new InvalidOperationException($"Indentity {identity.gameObject.name}:{state.UID} already initialized");

        //Put to world view;
        DontDestroyOnLoad(identity.gameObject);
        identity.transform.SetParent(transform);

        //Init;
        identity.Initialze(state, m_World, additionalData);

        //Push event;
        NewObjectInitialized(identity);
    }

    public void RegisterAsPending(NetObjectUID uid, NetObjectIdentity identity) => m_PendingObjectViews.Add(uid, identity);

    public void OnRemotePlayerBecomesReady(ClientUID peerId)
    {
        foreach (var obj in m_CurrentObjectViews.Values)
            obj.OnBecomesReady(peerId);
    }

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
    }
}