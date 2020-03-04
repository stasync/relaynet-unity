using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public abstract class NetWorld
{
    public NetWorldUID UID { get; private set; }
    public NetWorldView WorldView { get; private set; }
    public IReadOnlyDictionary<NetObjectUID, NetObjectState> ObjectStates => m_CurrentObjectStates;
    Dictionary<NetObjectUID, NetObjectState> m_CurrentObjectStates;
    public NetWorldPlayersCollection Players { get; private set; }

    protected NetWorld(NetWorldUID uid, string worldName)
    {
        UID = uid;
        m_CurrentObjectStates = new Dictionary<NetObjectUID, NetObjectState>();
        Players = new NetWorldPlayersCollection();

        WorldView = new GameObject($"{UID}:{worldName}").AddComponent<NetWorldView>();
        WorldView.Init(this);
    }

    protected void AddObject(NetObjectUID uid, NetObjectState state) => m_CurrentObjectStates.Add(uid, state);

    protected void Evaluate(float worldDelta)
    {
        OnUpdateWorldState(worldDelta);
        OnBeforeExecuteView();
        WorldView.Execute();

        Players.FinalizeIteration();
        ForEachStates((UID, state) => state.FinalizeIteration());
    }

    protected abstract void OnUpdateWorldState(float worldDelta);

    protected virtual void OnBeforeExecuteView() { }

    public virtual void Destroy()
    {
        UID = NetWorldUID.Invalid;

        m_CurrentObjectStates.Clear();
        m_CurrentObjectStates = null;

        Players.Clear();
        Players = null;

        WorldView.Destroy(); //Destroy all net object;
        WorldView = null;
    }

    public virtual bool ObjectExists(NetObjectUID uid) => m_CurrentObjectStates.ContainsKey(uid);

    public void ForEachStates(Action<NetObjectUID, NetObjectState> processor)
    {
        foreach (var UID in m_CurrentObjectStates.Keys.ToArray())
            processor(UID, m_CurrentObjectStates[UID]);
    }

    protected void ForEachLocalStates(Action<NetObjectUID, NetObjectState> processor) => ForEachStates((UID, state) =>
    {
        if (state.IsOwnedByLocalPlayer)
            processor(UID, state);
    });

    protected virtual void RemoveObject(NetObjectUID stateUID) =>
        m_CurrentObjectStates.Remove(stateUID);

    public NetWorldSnapshot ToSnapshot(float deltaTime = 0f) => NetWorldSnapshot.FromWorld(deltaTime, this);

    public void RestoreFromSnapshot(NetWorldSnapshot snapshot)
    {
        //Restore players;
        Players.RestoreFromSnapshot(snapshot.Players);

        //Add or update objects;
        foreach(var kvp in snapshot.Objects)
        {
            if (m_CurrentObjectStates.ContainsKey(kvp.Key))
                m_CurrentObjectStates[kvp.Key].ChangeObjectRemotely(kvp.Value);
            else
                m_CurrentObjectStates.Add(kvp.Key, new NetObjectState(kvp.Value, Players.Find));
        }

        //Remove absent obejets;
        ForEachStates((UID, state) => 
        {
            if (!snapshot.Objects.ContainsKey(UID))
                RemoveObject(UID);
        });
    }
}
