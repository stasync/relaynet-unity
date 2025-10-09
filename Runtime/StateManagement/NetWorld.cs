using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public abstract class NetWorld
{
    public event Action Updated = delegate { };
    public event Action<NetObjectUID> ObjectCreated = delegate { };

    public bool IsValid => ID.IsValid;

    public NetWorldID UID { get; private set; }
    public NetWorldID ID { get; private set; }
    public NetWorldID SourceID { get; private set; }
    public float Time { get; protected set; }
    public float LocalTime { get; private set; }
    public string Name { get; private set; }
    public NetWorldView View { get; private set; }

    public bool IsReplica => SourceID.IsValid;

    public IReadOnlyDictionary<NetObjectUID, NetObjectState> ObjectStates => m_CurrentObjectStates;
    private Dictionary<NetObjectUID, NetObjectState> m_CurrentObjectStates;

    public IExposedNetWorldPlayersCollection Players => PlayersInternal;
    protected NetWorldPlayersCollection PlayersInternal { get; private set; }

    private NetWorldUpdateQueue m_UpdateQueue;
    private Queue<Action> m_ViewInitQueue;

    protected NetWorld(NetWorldID uid, string worldName) : this(uid, worldName, NetWorldID.Invalid)
    {
    }

    protected NetWorld(NetWorldID uid, string worldName, NetWorldID sourceId)
    {
        UID = NetWorldID.Allocate();
        ID = uid;
        SourceID = sourceId;

        Time = 0f;
        LocalTime = 0f;
        Name = $"{ID}:{worldName}";

        m_CurrentObjectStates = new Dictionary<NetObjectUID, NetObjectState>();
        PlayersInternal = new NetWorldPlayersCollection(() => IsValid);
        m_UpdateQueue = new NetWorldUpdateQueue();
        m_ViewInitQueue = new Queue<Action>();
    }

    public void InitializeView(NetWorldViewInitMode viewInitMode = NetWorldViewInitMode.Default)
    {
        if (View != null)
            throw new InvalidOperationException($"View for world {Name} already initialized");

        View = new GameObject(Name).AddComponent<NetWorldView>();
        View.Init(this, viewInitMode);

        while (m_ViewInitQueue.Count > 0)
            m_ViewInitQueue.Dequeue()();
    }

    public void Update(float deltaTime)
    {
        if (!IsValid)
            return;

        m_UpdateQueue.ProcessQueue();
        UpdateImplementation(deltaTime);
    }

    protected abstract void UpdateImplementation(float deltaTime);

    protected bool TryAddObject(NetObjectState state)
    {
        if (!state.IsValid)
            throw new InvalidOperationException();

        var canAddObject = !ObjectExists(state.UID);
        if (canAddObject)
        {
            m_CurrentObjectStates.Add(state.UID, state);
            ObjectCreated(state.UID);
        }

        return canAddObject;
    }

    protected void UpdateWorldState(float worldDelta)
    {
        OnBeforeUpdateWorldState(worldDelta);
        OnUpdateWorldState(worldDelta);

        if(View != null)
        {
            OnBeforeExecuteView(worldDelta);
            View.RebuildAccordingState();
        }

        Updated();
    }

    protected virtual void OnBeforeUpdateWorldState(float worldDelta)
    {
        ForEachStates((_, state) => state.OnBeforeUpdateWorldState(worldDelta));
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
        UID = NetWorldID.Invalid;
        ID = NetWorldID.Invalid;
        SourceID = NetWorldID.Invalid;

        Updated = delegate { };

        foreach (var netObject in m_CurrentObjectStates.Values)
            netObject.Invalidate();

        m_CurrentObjectStates.Clear();
        m_CurrentObjectStates = null;

        PlayersInternal.Clear();
        PlayersInternal = null;

        m_UpdateQueue.Clear();
        m_UpdateQueue = null;

        m_ViewInitQueue.Clear();
        m_ViewInitQueue = null;

        if (View == null)
            Debug.LogError($"View already destroyed (Maybe it's done by unity via exiting play mode)");
        else
        {
            View.Destroy(); //Destroy all net object views;
            View = null;
        }
    }

    public void ScheduleOnUpdate(Action action) => 
        m_UpdateQueue.Enqueue(action);
    
    public void ScheduleOnUpdateUntil(Func<bool> condition, Action action) => 
        m_UpdateQueue.EnqueueUntil(condition, action);
    
    public void ScheduleOnUpdateIfObjectExists(NetObjectUID objectUID, Action<NetObjectState> a) => 
        ScheduleOnUpdate(() => 
        {
            if(!TryGetObject(objectUID, out var state))
            {
                Debug.LogError($"Skipping scheduled action for absent object {objectUID}");
                return;
            }

            a(state);
        });

    public virtual bool ObjectExists(NetObjectUID uid) => 
        m_CurrentObjectStates.ContainsKey(uid);

    public NetObjectIdentity FindObjectView(NetObjectUID instanceId) => 
        View == null ? null :
        View.FindObjectView(instanceId);

    public T FindObjectView<T>(NetObjectUID instanceId) where T : GSInstanceBehaviourBase =>
        View == null ? null :
        View.FindObjectView<T>(instanceId);

    public IEnumerable<T> FindObjectViewsOfType<T>() where T : GSInstanceBehaviourBase =>
        View == null ? null :
        View.FindObjectViewsOfType<T>();
    
    public T FindObjectViewOfType<T>() where T : GSInstanceBehaviourBase
    {
        if (View == null)
            return null;

        foreach(var view in View.CurrentObjectViews.Values)
        {
            var component = view.GetBehaviour<T>();
            if (component != null)
                return component;
        }
        return null;
    }

    public virtual NetObjectState FindObject(NetObjectUID instanceId) =>
        m_CurrentObjectStates.GetValueOrDefault(instanceId);

    public bool TryGetObject(NetObjectUID instanceId, out NetObjectState state) =>
        m_CurrentObjectStates.TryGetValue(instanceId, out state);

    public void ForEachStates(Action<NetObjectUID, NetObjectState> processor)
    {
        foreach (var uid in m_CurrentObjectStates.Keys.ToArray())
            processor(uid, m_CurrentObjectStates[uid]);
    }

    protected void ForEachLocalStates(Action<NetObjectUID, NetObjectState> processor) => 
        ForEachStates((uid, state) =>
        {
            if (state.IsOwnedByLocalPlayer)
                processor(uid, state);
        });

    protected void ForEachRemoteStates(Action<NetObjectUID, NetObjectState> processor) => 
        ForEachStates((uid, state) =>
        {
            if (!state.IsOwnedByLocalPlayer)
                processor(uid, state);
        });

    protected virtual void RemoveObject(NetObjectUID stateUID)
    {
        if (!m_CurrentObjectStates.TryGetValue(stateUID, out var state))
            return;

        state.Invalidate();
        m_CurrentObjectStates.Remove(stateUID);
    }
        
    public NetWorldSnapshot ToSnapshot(float deltaTime = 0f) => 
        NetWorldSnapshot.FromWorld(deltaTime, this);

    public void RestoreFromSnapshot(NetWorldSnapshot snapshot)
    {
        // Update time.
        Time = snapshot.Time;
        LocalTime = snapshot.LocalTime;

        // Restore players.
        PlayersInternal.RestoreFromSnapshot(snapshot.Players);

        // Add or update objects.
        foreach(var kvp in snapshot.Objects)
        {
            if (m_CurrentObjectStates.ContainsKey(kvp.Key))
                m_CurrentObjectStates[kvp.Key].ChangeObjectRemotely(kvp.Value);
            else
                TryAddObject(new NetObjectState(kvp.Value, PlayersInternal.Find));
        }

        // Remove absent objects.
        ForEachStates((uid, _) => 
        {
            if (!snapshot.Objects.ContainsKey(uid))
                RemoveObject(uid);
        });
    }

    public void ScheduleOnViewInit(Action action)
    {
        if (View != null)
            action();
        else
            m_ViewInitQueue.Enqueue(action);
    }

    public override int GetHashCode()
    {
        var hash = 13;
        hash = (hash * 7) + ID.GetHashCode();
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
}
