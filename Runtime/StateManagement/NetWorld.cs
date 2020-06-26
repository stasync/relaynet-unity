using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class UpdateQueue
{
    Queue<Action> m_UpdateQueue;
    Queue<Action> m_RepeatUpdateQueue;

    public UpdateQueue()
    {
        m_UpdateQueue = new Queue<Action>();
        m_RepeatUpdateQueue = new Queue<Action>();
    }

    public void Enqueue(Action a) => m_UpdateQueue.Enqueue(a);
    public void EnqueueUntil(Func<bool> condition, Action a)
    {
        m_UpdateQueue.Enqueue(InternalAction);

        void InternalAction()
        {
            if (condition())
                a();
            else
                m_RepeatUpdateQueue.Enqueue(InternalAction);
        }
    }

    public void ProcessQueue()
    {
        while (m_UpdateQueue.Count > 0)
            m_UpdateQueue.Dequeue()();

        while (m_RepeatUpdateQueue.Count > 0)
            m_UpdateQueue.Enqueue(m_RepeatUpdateQueue.Dequeue());
    }

    public void Clear()
    {
        m_UpdateQueue.Clear();
        m_UpdateQueue = null;

        m_RepeatUpdateQueue.Clear();
        m_RepeatUpdateQueue = null;
    }
}

public abstract class NetWorld : IJSONSerializable
{
    public event Action Evaluated = delegate { };

    public bool IsValid => UID.IsValid;
    public NetWorldUID UID { get; private set; }
    public float Time { get; protected set; }
    public float LocalTime { get; private set; }
    public string Name { get; private set; }
    public NetWorldView View { get; private set; }
    public IReadOnlyDictionary<NetObjectUID, NetObjectState> ObjectStates => m_CurrentObjectStates;
    Dictionary<NetObjectUID, NetObjectState> m_CurrentObjectStates;

    public IReadOnlyNetWorldPlayersCollection Players => PlayersInternal;
    protected NetWorldPlayersCollection PlayersInternal { get; private set; }

    UpdateQueue m_UpdateQueue;

    protected NetWorld(NetWorldUID uid, string worldName)
    {        
        UID = uid;
        Time = 0f;
        LocalTime = 0f;
        Name = $"{UID}:{worldName}";

        m_CurrentObjectStates = new Dictionary<NetObjectUID, NetObjectState>();
        PlayersInternal = new NetWorldPlayersCollection(() => IsValid);
        m_UpdateQueue = new UpdateQueue();
    }

    public void InitializeView(NetWorldViewInitMode viewInitMode = NetWorldViewInitMode.Default)
    {
        if (View != null)
            throw new InvalidOperationException($"View for world {Name} already initialized");

        View = new GameObject(Name).AddComponent<NetWorldView>();
        View.Init(this, viewInitMode);
    }

    public void Update(float deltaTime)
    {
        if (!IsValid)
            return;

        m_UpdateQueue.ProcessQueue();
        UpdateImplementation(deltaTime);
    }

    protected abstract void UpdateImplementation(float deltaTime);

    protected void AddObject(NetObjectUID uid, NetObjectState state) => m_CurrentObjectStates.Add(uid, state);

    protected void Evaluate(float worldDelta)
    {
        OnBeforeUpdateWorldState(worldDelta);
        OnUpdateWorldState(worldDelta);

        if(View != null)
        {
            OnBeforeExecuteView(worldDelta);
            View.Execute();
        }

        Evaluated();
    }

    protected virtual void OnBeforeUpdateWorldState(float worldDelta)
    {
        ForEachStates((UID, state) => state.OnBeforeUpdateWorldState(worldDelta));
        PlayersInternal.OnBeforeUpdateWorldState(worldDelta);
    }

    protected virtual void OnUpdateWorldState(float worldDelta)
    {
        Time += worldDelta;
        LocalTime += worldDelta;
    }

    protected virtual void OnBeforeExecuteView(float worldDelta) { }

    public virtual void Destroy()
    {
        UID = NetWorldUID.Invalid;
        Evaluated = delegate { };

        m_CurrentObjectStates.Clear();
        m_CurrentObjectStates = null;

        PlayersInternal.Clear();
        PlayersInternal = null;

        m_UpdateQueue.Clear();
        m_UpdateQueue = null;

        if (View == null)
            Debug.LogError($"View already destroyed (Maybe it's done by unity via exiting play mode)");
        else
        {
            Debug.LogError($"Destroy world {View.gameObject.name}");
            View.Destroy(); //Destroy all net object;
            View = null;
        }
    }

    public void ScheduleOnUpdate(Action action) => m_UpdateQueue.Enqueue(action);
    public void ScheduleOnUpdateUntil(Func<bool> condition, Action action) => m_UpdateQueue.EnqueueUntil(condition, action);
    public void ScheduleOnUpdateIfObejctExists(NetObjectUID objectUID, Action<NetObjectState> a) => ScheduleOnUpdate(() => 
    {
        if(!TryGetObject(objectUID, out var state))
        {
            Debug.LogError($"Skipping scheduled action for absent object {objectUID}");
            return;
        }

        a(state);
    });

    public virtual bool ObjectExists(NetObjectUID uid) => m_CurrentObjectStates.ContainsKey(uid);

    public NetObjectIdentity FindObjectView(NetObjectUID instanceId) => View == null ? null :
        View.CurrentObjectViews.TryGetValue(instanceId, out NetObjectIdentity identity) ? identity : null;

    public T FindObjectView<T>(NetObjectUID instanceId) where T : GSInstanceBehaviourBase
    {
        var networkObject = FindObjectView(instanceId);
        return networkObject == null ? null : networkObject.GetComponent<T>();
    }

    public IEnumerable<T> FindObjectViewsOfType<T>() where T : GSInstanceBehaviourBase => View == null ? null :
        View.CurrentObjectViews.Values
            .Select(v => v.GetBehaviour<T>())
            .Where(b => b != null);

    public virtual NetObjectState FindObject(NetObjectUID instanceId) =>
        m_CurrentObjectStates.TryGetValue(instanceId, out NetObjectState state) ? state : null;

    public bool TryGetObject(NetObjectUID instanceId, out NetObjectState state) =>
        m_CurrentObjectStates.TryGetValue(instanceId, out state);

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

    protected virtual void RemoveObject(NetObjectUID stateUID)
    {
        if (!m_CurrentObjectStates.TryGetValue(stateUID, out var state))
            return;

        state.Invalidate();
        m_CurrentObjectStates.Remove(stateUID);
    }
        

    public NetWorldSnapshot ToSnapshot(float deltaTime = 0f) => NetWorldSnapshot.FromWorld(deltaTime, this);

    public void RestoreFromSnapshot(NetWorldSnapshot snapshot)
    {
        //Update time;
        Time = snapshot.Time;
        LocalTime = snapshot.LocalTime;

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

    public override int GetHashCode()
    {
        var hash = 13;
        hash = (hash * 7) + UID.GetHashCode();
        hash = (hash * 7) + Time.GetHashCode();
        hash = (hash * 7) + LocalTime.GetHashCode(); //Do we need local time in hash code?
        foreach (var kvp in m_CurrentObjectStates)
        {
            hash += kvp.Key.GetHashCode();
            hash += kvp.Value.GetHashCode();
        }
        hash = (hash * 7) + PlayersInternal.GetHashCode();
        return hash;
    }

    public string ToJSONProperty() => "{" + RealtimeJSONUtils.ToJSONValue("objects") + ":" + RealtimeJSONUtils.ToJSONCollection(View.CurrentObjectViews.Values.Select(o => o.ToJSONProperty()).ToArray()) + "}";
}
