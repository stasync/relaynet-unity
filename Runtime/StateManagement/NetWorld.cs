using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public abstract class NetWorld
{
    public NetWorldUID UID { get; private set; }
    public NetWorldView WorldView { get; private set; }
    public IReadOnlyDictionary<NetObjectUID, NetObjectState> ObjectStates => CurrentObjectStates;
    protected Dictionary<NetObjectUID, NetObjectState> CurrentObjectStates { get; private set; }
    public NetWorldPlayersCollection Players { get; private set; }

    protected virtual bool RecordHistory => true;

    public NetWorld(NetWorldUID uid, string worldName)
    {
        if (WorldView != null)
            throw new InvalidOperationException($"{nameof(WorldView)} alreay created");

        UID = uid;
        CurrentObjectStates = new Dictionary<NetObjectUID, NetObjectState>();
        Players = new NetWorldPlayersCollection();

        WorldView = new GameObject($"{UID}:{worldName}").AddComponent<NetWorldView>();
        WorldView.Init(this);
    }

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

        CurrentObjectStates.Clear();
        CurrentObjectStates = null;

        Players.Clear();
        Players = null;

        WorldView.Destroy(); //Destroy all net object;
        WorldView = null;
    }

    public virtual bool ObjectExists(NetObjectUID uid) => CurrentObjectStates.ContainsKey(uid);

    public void ForEachStates(Action<NetObjectUID, NetObjectState> processor)
    {
        foreach (var UID in CurrentObjectStates.Keys.ToArray())
            processor(UID, CurrentObjectStates[UID]);
    }

    protected void ForEachLocalStates(Action<NetObjectUID, NetObjectState> processor) => ForEachStates((UID, state) =>
    {
        if (state.IsOwnedByLocalPlayer)
            processor(UID, state);
    });

    protected virtual void RemoveObject(NetObjectUID stateUID) =>
        CurrentObjectStates.Remove(stateUID);

    public NetWorldSnapshot ToSnapshot(float deltaTime = 0f) => NetWorldSnapshot.FromWorld(deltaTime, this);

    public void RestoreFromSnapshot(NetWorldSnapshot snapshot)
    {
        //Restore players;
        Players.RestoreFromSnapshot(snapshot.Players);

        //Add or update objects;
        foreach(var kvp in snapshot.Objects)
        {
            if (CurrentObjectStates.ContainsKey(kvp.Key))
                CurrentObjectStates[kvp.Key].ChangeObjectRemotely(kvp.Value);
            else
                CurrentObjectStates.Add(kvp.Key, new NetObjectState(kvp.Value, Players.Find));
        }

        //Remove absent obejets;
        ForEachStates((UID, state) => 
        {
            if (!snapshot.Objects.ContainsKey(UID))
                RemoveObject(UID);
        });
    }
}
