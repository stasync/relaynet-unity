using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public abstract class NetWorld
{
    public bool IsValid => UID.IsValid;
    public NetWorldUID UID { get; private set; }
    public NetWorldView View { get; private set; }
    public IReadOnlyDictionary<NetObjectUID, NetObjectState> ObjectStates => m_CurrentObjectStates;
    Dictionary<NetObjectUID, NetObjectState> m_CurrentObjectStates;

    public IReadOnlyNetWorldPlayersCollection Players => PlayersInternal;
    protected NetWorldPlayersCollection PlayersInternal { get; private set; }

    Queue<Action> m_UpdateQueue;

    protected NetWorld(NetWorldUID uid, string worldName)
    {        
        UID = uid;
        m_CurrentObjectStates = new Dictionary<NetObjectUID, NetObjectState>();
        PlayersInternal = new NetWorldPlayersCollection();

        m_UpdateQueue = new Queue<Action>();

        View = new GameObject($"{UID}:{worldName}").AddComponent<NetWorldView>();
        View.Init(this);
    }

    public void FixedUpdate()
    {
        if (!IsValid)
            return;

        while (m_UpdateQueue.Count > 0)
            m_UpdateQueue.Dequeue()();

        UpdateImplementation();
    }

    protected abstract void UpdateImplementation();

    protected void AddObject(NetObjectUID uid, NetObjectState state) => m_CurrentObjectStates.Add(uid, state);

    protected void Evaluate(float worldDelta)
    {
        OnUpdateWorldState(worldDelta);
        OnBeforeExecuteView();
        View.Execute();

        PlayersInternal.FinalizeIteration();
        ForEachStates((UID, state) => state.FinalizeIteration());
    }

    protected abstract void OnUpdateWorldState(float worldDelta);

    protected virtual void OnBeforeExecuteView() { }

    public virtual void Destroy()
    {
        UID = NetWorldUID.Invalid;

        m_CurrentObjectStates.Clear();
        m_CurrentObjectStates = null;

        PlayersInternal.Clear();
        PlayersInternal = null;

        m_UpdateQueue.Clear();
        m_UpdateQueue = null;

        View.Destroy(); //Destroy all net object;
        View = null;
    }

    public void ScheduleOnUpdate(Action a) => m_UpdateQueue.Enqueue(a);

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

    protected void ForEachRemoteStates(Action<NetObjectUID, NetObjectState> processor) => ForEachStates((UID, state) =>
    {
        if (!state.IsOwnedByLocalPlayer)
            processor(UID, state);
    });

    protected virtual void RemoveObject(NetObjectUID stateUID) =>
        m_CurrentObjectStates.Remove(stateUID);

    public NetWorldSnapshot ToSnapshot(float deltaTime = 0f) => NetWorldSnapshot.FromWorld(deltaTime, this);

    public void RestoreFromSnapshot(NetWorldSnapshot snapshot)
    {
        //Restore players;
        PlayersInternal.RestoreFromSnapshot(snapshot.Players);

        //Add or update objects;
        foreach(var kvp in snapshot.Objects)
        {
            if (m_CurrentObjectStates.ContainsKey(kvp.Key))
                m_CurrentObjectStates[kvp.Key].ChangeObjectRemotely(kvp.Value);
            else
                m_CurrentObjectStates.Add(kvp.Key, new NetObjectState(kvp.Value, PlayersInternal.Find));
        }

        //Remove absent obejets;
        ForEachStates((UID, state) => 
        {
            if (!snapshot.Objects.ContainsKey(UID))
                RemoveObject(UID);
        });
    }
}
