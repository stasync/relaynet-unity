using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Core.Networking;

public class NetWorldRuntime : NetWorld
{
    public Action<NetWorldPlayer, bool> NewPlayerRegisterd = delegate { };

    public IReadOnlyDictionary<NetObjectUID, NetObjectState> ObjectsRegisterRequests => m_ObjectsRegisterRequests;
    Dictionary<NetObjectUID, NetObjectState> m_ObjectsRegisterRequests;

    public IReadOnlyDictionary<NetObjectUID, NetObjectStateSnapshot> RemoteObjectUpdateRequests => m_RemoteObjectUpdateRequests;    
    Dictionary<NetObjectUID, NetObjectStateSnapshot> m_RemoteObjectUpdateRequests;

    public IReadOnlyDictionary<ClientUID, NetWorldPlayerSnapshot> RemotePlayersUpdateRequests => m_RemotePlayersUpdateRequests;
    Dictionary<ClientUID, NetWorldPlayerSnapshot> m_RemotePlayersUpdateRequests;

    Queue<byte[]> m_RemoteWorldDataUpdateRequests;

    public IReadOnlyCollection<NetWorldSnapshot> HistroyBuffer => m_HistoryBuffer;
    HistoryBiffer m_HistoryBuffer;

    HashSet<ClientUID> m_LocalPlayerRegisterQueue;
    HashSet<ClientUID> m_PlayerUnregisterQueue;

    Queue<Action> m_ViewInitQueue;

    Action<uint, DeliveryIntent, byte[]> m_Sender;

    const float c_HistoryBufferSecondsLength = 10f; //Record last 30 seconds;
    const float c_MinUpdateInteraval = 0.1f; //10 times per second;
    float m_LastUpdateTime;

    public float FixedTime => UnityEngine.Time.fixedUnscaledTime;

    public NetWorldRuntime(Action<uint, DeliveryIntent, byte[]> sender, string worldName = "NetworkWorldRoot") : base(NetWorldUID.Allocate(), worldName)
    {
        m_Sender = sender ?? throw new ArgumentNullException(nameof(sender));

        NetObjectUID.Reset();

        m_LastUpdateTime = FixedTime;

        /// Must be cleaned, when player disconnects;
        {
            m_RemoteObjectUpdateRequests = new Dictionary<NetObjectUID, NetObjectStateSnapshot>();
            m_RemotePlayersUpdateRequests = new Dictionary<ClientUID, NetWorldPlayerSnapshot>();
            m_ObjectsRegisterRequests = new Dictionary<NetObjectUID, NetObjectState>();
            m_RemoteWorldDataUpdateRequests = new Queue<byte[]>();
        }

        m_LocalPlayerRegisterQueue = new HashSet<ClientUID>();
        m_PlayerUnregisterQueue = new HashSet<ClientUID>();
        m_ViewInitQueue = new Queue<Action>();

        m_HistoryBuffer = new HistoryBiffer();
    }

    public NetWorldEvaluator Replicate() => new NetWorldEvaluator(this);

    public void ScheduleUpdateForRemoteObject(NetObjectStateSnapshot snapshot)
    {
        if(!snapshot.ObjectUID.IsValid)
        {
            Debug.LogError($"Unable to schedule update for remote object {snapshot.ObjectUID} {snapshot.PrefabId}, invalid UID");
            return;
        }

        if (m_RemoteObjectUpdateRequests.ContainsKey(snapshot.ObjectUID))
        {
            var current = m_RemoteObjectUpdateRequests[snapshot.ObjectUID];
            if (current.StateChangeType < snapshot.StateChangeType)
                m_RemoteObjectUpdateRequests[snapshot.ObjectUID] = snapshot;
            else
                Debug.LogWarning($"Unable to schedule update for remote object {snapshot.ObjectUID} {snapshot.PrefabId} with {snapshot.StateChangeType}, already scheduled update witht high priority");
        }
        else
            m_RemoteObjectUpdateRequests.Add(snapshot.ObjectUID, snapshot);
    }

    public void ScheduleUpdateForRemotePlayer(NetWorldPlayerSnapshot snapshot)
    {
        if (m_RemotePlayersUpdateRequests.ContainsKey(snapshot.PlayerUID))
             Debug.LogWarning($"Unable to schedule update for remote player {snapshot.PlayerUID}: already scheduled");
        else
            m_RemotePlayersUpdateRequests.Add(snapshot.PlayerUID, snapshot);
    }

    public void ScheduleRemoteWorldDataUpdate(byte[] data) => m_RemoteWorldDataUpdateRequests.Enqueue(data);

    protected override void UpdateImplementation()
    {
        var worldDelta = FixedTime - m_LastUpdateTime;
        if (worldDelta < c_MinUpdateInteraval)
            return;

        m_LastUpdateTime = FixedTime;
        Evaluate(worldDelta);
    }

    #region Loop
    protected override void OnUpdateWorldState(float worldDelta)
    {
        base.OnUpdateWorldState(worldDelta);

        TrySendWorldData();
        TryReceiveWorldData();

        UpdatePlayerStates();
        UpdateStateBasedOnRemovedPlayers();
        UpdateStateBasedOnRemoteData();
        UpdateRemoteRPCRequests();
        UpdateLocalObjectState();

        #region Internal
        void TrySendWorldData()
        {
            if (!PlayersInternal.LocalPlayerIsMaster)
                return;

            var writer = new Core.Networking.NetWriter();
            writer.Write(Time);
            m_Sender(MessageCodes.WorldCommunication, DeliveryIntent.Unreliable, writer.ToArray());
        }

        void TryReceiveWorldData()
        {
            var reader = new Core.Networking.NetReader();
            while(m_RemoteWorldDataUpdateRequests.Count > 0)
            {
                reader.Replace(m_RemoteWorldDataUpdateRequests.Dequeue());
                Time = reader.ReadSingle();
            }
        }

        void UpdatePlayerStates()
        {
            //Add local
            {
                foreach (var p in m_LocalPlayerRegisterQueue)
                    NewPlayerRegisterd(PlayersInternal.RegisterImmidiate(p, true), true); //Add player and notify;

                m_LocalPlayerRegisterQueue.Clear();
            }

            //Remove all
            {
                foreach (var p in m_PlayerUnregisterQueue)
                    PlayersInternal.UnregisterImmidiate(p);

                m_PlayerUnregisterQueue.Clear();
            }

            //Update remotes;
            {
                foreach (var UID in m_RemotePlayersUpdateRequests.Keys.ToArray())
                {
                    var snapshot = m_RemotePlayersUpdateRequests[UID];
                    var player = Players.Find(UID);

                    //Player not exists;
                    if (player == null)
                    {
                        //Create new;
                        player = PlayersInternal.RegisterImmidiate(snapshot.PlayerUID, false);

                        //Notify player registred;
                        NewPlayerRegisterd(player, false);

                        ForEachStates((stateUID, state) => 
                        {
                            if (state.IsOwnedByLocalPlayer)
                                state.ScheduleUpdate(ObjectStateChangeRequestType.InitializationUpdate);
                        });
                    }

                    var wasInGameScene = player.IsInGameScene;
                    //Restore player;
                    player.RestoreFromSnapshot(snapshot);

                    if (player.IsInGameScene && !wasInGameScene)
                    {
                        //Remote player loads scene
                    }
                }
                m_RemotePlayersUpdateRequests.Clear();
            }

            //Send local player;
            {
                var locaPlayer = Players.LocalPlayer;
                if (locaPlayer == null)
                    Debug.LogError("Unable to send local player: doesn't exists");
                else
                    m_Sender(MessageCodes.PlayerStateUpdate, DeliveryIntent.Reliable, locaPlayer.Serialize());
            }
        }

        /// Update states based on removed player, if player leave, remove object;
        void UpdateStateBasedOnRemovedPlayers()
        {
            ForEachStates((stateUID, state) =>
            {
                foreach (var playerUID in PlayersInternal.RemovedPlayers.ToArray())
                {
                    //Object owned by disconnected player;
                    if (state.IsOwnedBy(playerUID))
                    {
                        //Change owner, if object is scene obeject;
                        if (state.IsSceneObject)
                        {
                            //Is local client becomes master client, set object's owner and sync it.
                            if (PlayersInternal.LocalPlayer != null && PlayersInternal.LocalPlayer.IsMaster)
                                state.ScheduleOwnerChanged(PlayersInternal.LocalPlayer.UID);
                            else
                                state.InvalidateOwner();

                            TryNotifyViewAboutDisconnectedPlayer(playerUID, stateUID);
                        }
                        //Destroy object;
                        else
                            RemoveObject(stateUID);
                    }
                    //Notify view;
                    else
                        TryNotifyViewAboutDisconnectedPlayer(playerUID, stateUID);
                }
            });

            void TryNotifyViewAboutDisconnectedPlayer(ClientUID clientUID, NetObjectUID objectUID)
            {
                if (View.CurrentObjectViews.TryGetValue(objectUID, out var view))
                    view.OnDisconnected(clientUID);
            }
        }

        /// Update state based on remote incoming data;
        void UpdateStateBasedOnRemoteData()
        {
            ForEachRemoteRequests((UID, snapshot) =>
            {
                if (!snapshot.ObjectUID.IsValid)
                    throw new InvalidOperationException($"Invalid UID: '{snapshot.ObjectUID}'  Prefab: '{snapshot.PrefabId}' Data size: '{snapshot.Data.Length}'");

                /// Check if owner exists;
                if(!PlayersInternal.Contains(snapshot.OwnerUID))
                {
                    m_RemoteObjectUpdateRequests.Remove(UID);
                    return;
                }

                /// Object exists, just update;
                if (ObjectStates.TryGetValue(snapshot.ObjectUID, out var objState))
                {
                    objState.ChangeObjectRemotely(snapshot);
                    m_RemoteObjectUpdateRequests.Remove(UID);

                    if (objState.StateChangeType == ObjectStateChangeRequestType.RemoveUpdateHard)
                        RemoveObject(snapshot.ObjectUID);

                    return;
                }

                /// Object doesn't exists;
                if (snapshot.StateChangeType == ObjectStateChangeRequestType.RemoveUpdateHard || snapshot.StateChangeType == ObjectStateChangeRequestType.RemoveUpdateSoft)
                {
                    Debug.LogError($"Remote update snapshot form object with UID: {snapshot.ObjectUID}:{snapshot.PrefabId} has state {snapshot.StateChangeType}, but object doesn't exists");
                    m_ObjectsRegisterRequests.Remove(snapshot.ObjectUID);
                    return;
                }

                /// Add new or get pending;
                if (snapshot.StateChangeType != ObjectStateChangeRequestType.InitializationUpdate)
                {
                    m_ObjectsRegisterRequests.Remove(snapshot.ObjectUID);
                    return;
                }

                if (m_ObjectsRegisterRequests.TryGetValue(snapshot.ObjectUID, out var newState))
                {
                    if (newState.IsOwnedBy(snapshot.OwnerUID))
                    {
                        newState.ChangeObjectRemotely(snapshot);
                        m_ObjectsRegisterRequests.Remove(snapshot.ObjectUID);
                    }
                    else
                        Debug.LogError($"Unable to restore object {newState.ToString()} form {snapshot.ToString()}");
                }
                else
                    newState = new NetObjectState(snapshot, PlayersInternal.Find);

                AddObject(snapshot.ObjectUID, newState);
                m_RemoteObjectUpdateRequests.Remove(UID);
            });
        }

        /// Update RPC requests (Calling rpc without authority);
        void UpdateRemoteRPCRequests()
        {
            ForEachRemoteStates((UID, state) => 
            {
                foreach (var r in state.RPCWithoutAuthorityRequests)
                    m_Sender(MessageCodes.RPC, r.DeliveryIntent, r.State.ToData());
            });
        }

        /// Update state based on local object changes;
        void UpdateLocalObjectState()
        {
            //Register new local object, that was just created;
            foreach (var UID in m_ObjectsRegisterRequests.Keys.ToArray())
            {
                var state = m_ObjectsRegisterRequests[UID];
                //Skip remote objects;
                if (!state.IsOwnedByLocalPlayer)
                    continue;

                if (ObjectExists(state.UID))
                    Debug.LogError($"Object with UID {state.UID} already exists");
                else
                {
                    state.ScheduleUpdate(ObjectStateChangeRequestType.InitializationUpdate);
                    AddObject(state.UID, state);
                }

                m_ObjectsRegisterRequests.Remove(UID);
            }

            /// Update and send local objects;
            ForEachLocalStates((UID, state) =>
            {
                var deliveryIntent = DeliveryIntent.Unreliable;
                if (state.StateChangeType >= ObjectStateChangeRequestType.ReliableUpdate)
                    deliveryIntent = DeliveryIntent.Reliable;

                state.PrepareForSerialization();
                m_Sender(MessageCodes.ObjectStateUpdate, deliveryIntent, state.Serialize());

                switch (state.StateChangeType)
                {
                    case ObjectStateChangeRequestType.RemoveUpdateHard:
                        RemoveObject(UID);
                        break;

                    case ObjectStateChangeRequestType.RemoveUpdateSoft:
                        state.ScheduleUpdate(ObjectStateChangeRequestType.RemoveUpdateHard);
                        break;
                }
            });
        }
        #endregion
    }

    protected override void OnBeforeExecuteView(float worldDelta)
    {
        base.OnBeforeExecuteView(worldDelta);

        var elementsCount = c_HistoryBufferSecondsLength / c_MinUpdateInteraval;
        m_HistoryBuffer.Enqueue(ToSnapshot(worldDelta));
        if (m_HistoryBuffer.Count > elementsCount)
            m_HistoryBuffer.Dequeue();
    }
    #endregion

    protected override void RemoveObject(NetObjectUID stateUID)
    {
        base.RemoveObject(stateUID);

        m_ObjectsRegisterRequests.Remove(stateUID);
        m_RemoteObjectUpdateRequests.Remove(stateUID);
    }

    /// <summary>
    /// Destroy all network objects and cleanup scene.
    /// </summary>
    public override void Destroy()
    {
        base.Destroy();

        m_RemoteObjectUpdateRequests.Clear();
        m_RemoteObjectUpdateRequests = null;

        m_RemotePlayersUpdateRequests.Clear();
        m_RemotePlayersUpdateRequests = null;

        m_ObjectsRegisterRequests.Clear();
        m_ObjectsRegisterRequests = null;

        m_RemoteWorldDataUpdateRequests.Clear();
        m_RemoteWorldDataUpdateRequests = null;

        m_LocalPlayerRegisterQueue.Clear();
        m_LocalPlayerRegisterQueue = null;

        m_PlayerUnregisterQueue.Clear();
        m_PlayerUnregisterQueue = null;

        m_ViewInitQueue.Clear();
        m_ViewInitQueue = null;

        m_HistoryBuffer.Clear();
        m_HistoryBuffer = null;

        NewPlayerRegisterd = null;
    }

    /// Connect;
    public void RegisterLocalPlayer(ClientUID peerId) => 
        m_LocalPlayerRegisterQueue.Add(peerId);

    /// Disconnected;
    public void OnDisconnect(ClientUID uid)
    {
        if (m_RemotePlayersUpdateRequests.Remove(uid))
            Debug.LogError($"Removed absend update request for player {uid}");

        m_LocalPlayerRegisterQueue.Remove(uid);
        m_PlayerUnregisterQueue.Add(uid);
    }

    /// Scene loaded;
    public void OnSceneLoaded() => 
        ScheduleOnUpdate(() => 
        {
            //Create view
            InitializeView();

            //Check for player exists;
            var player = PlayersInternal.LocalPlayer; 
            if (player != null)
                player.IsInGameScene = true;

            while (m_ViewInitQueue.Count > 0)
                m_ViewInitQueue.Dequeue()();
        });

    protected void ForEachRemoteRequests(Action<NetObjectUID, NetObjectStateSnapshot> processor)
    {
        foreach (var UID in m_RemoteObjectUpdateRequests.Keys.ToArray())
            processor(UID, m_RemoteObjectUpdateRequests[UID]);
    }

    /// <summary>
    /// Create object with position and rotation input.
    /// Sould be called on local client to create object across network.
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="prefabId"></param>
    /// <param name="position"></param>
    /// <param name="rotation"></param>
    /// <param name="baseData"></param>
    /// <returns></returns>
    public NetObjectIdentity CreateObject(NetWorldPlayer owner, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation, object[] additionalData)
    {
        var state = new NetObjectState(new NetObjectUID(owner.UID), prefabId, isSceneObject, owner.UID, PlayersInternal.Find);
        m_ObjectsRegisterRequests.Add(state.UID, state);
        return View.CreateImmidiate(state, position, rotation, additionalData);      
    }

    /// <summary>
    /// Register object, which is not created via main object creation pipline.
    /// See 'CreateObject' method.
    /// This is not initialize object, so object will be invalid, untill first update happens.
    /// </summary>
    /// <param name="sceneIdentity"></param>
    public void TryRegisterOrInitializeSceneObjectIdentity(GSSceneObjectIdentity sceneIdentity)
    {
        if (ObjectExists(sceneIdentity.SceneObjectId))
        {
#if NETWORK_DEBUG
            Debug.LogError($"Trying to register scene identity {sceneIdentity.gameObject.name} {sceneIdentity.PrefabID}: but object with id {sceneIdentity.SceneObjectId} already registered");
#endif
            return;
        }

        if (sceneIdentity.Identity.Initialized)
            throw new InvalidOperationException($"{sceneIdentity.gameObject.name} alredy initialized");

        //Create state from scen object identity;
        //We don't need to know scene object prefab id, because it's already in scene, but prefab id can't be null.
        var newState = new NetObjectState(sceneIdentity.SceneObjectId, sceneIdentity.PrefabID, true, PlayersInternal.MasterPlayerUID, PlayersInternal.Find);
        m_ObjectsRegisterRequests.Add(newState.UID, newState);
        var newStateUID = newState.UID;

        //Wait for view initialization;
        ScheduleOnViewInit(() =>
        {
            if(TryGetExistedOrPendingState(newStateUID, out var state))
            {
                //Initialize immidiatlty on master client;
                if (PlayersInternal.LocalPlayerIsMaster)
                    View.InitializeObjectView(sceneIdentity.Identity, state, null);

                //Add view to be used in future;
                View.RegisterAsPending(newStateUID, sceneIdentity.Identity);
            }
        });
    }

    bool TryGetExistedOrPendingState(NetObjectUID instanceId, out NetObjectState state) => TryGetState(instanceId, out state) || m_ObjectsRegisterRequests.TryGetValue(instanceId, out state);

    /// <summary>
    /// Get object before in was actually created by transport system.
    /// Note that this object will not be in valid state, until update happens.
    /// Use in only if its highly nessesery.
    public NetObjectIdentity FindObjectViewOrCreateMock(NetWorldPlayer owner, NetObjectUID instanceId, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation)
    {
        var identity = FindObjectView(instanceId);
        return 
            identity == null ?
            CreatePendingObject() : 
            identity;

        NetObjectIdentity CreatePendingObject()
        {
            var state = new NetObjectState(instanceId, prefabId, isSceneObject, owner.UID, PlayersInternal.Find);
            m_ObjectsRegisterRequests.Add(instanceId, state);
            return View.CreateImmidiate(state, position, rotation, null);
        }
    }

    public T FindObjectViewOrCreateMock<T>(NetWorldPlayer owner, NetObjectUID instanceId, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation) where T : GSInstanceBehaviourBase
    {
        var networkObject = FindObjectViewOrCreateMock(owner, instanceId, prefabId, isSceneObject, position, rotation);
        return networkObject == null ? null : networkObject.GetComponent<T>();
    } 

    public int GetHistoryBufferSize() => m_HistoryBuffer.Sum(s => s.GetSize());

    public void ScheduleOnViewInit(Action action)
    {
        if (View != null)
            action();
        else
            m_ViewInitQueue.Enqueue(action);
    }
}
