using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Core.Networking;

public sealed class NetWorldRuntime : NetWorld
{
    private const float HISTORY_BUFFER_MAX_LENGTH_IN_SECONDS = 10f; // Record last 'N' seconds.
    private const float MIN_UPDATE_INTERVAL = 0.1f; // 10 times per second.
    
    public event Action<NetWorldPlayerSnapshot> PlayerRegistered = delegate { };
    public event Action<NetWorldPlayerSnapshot> PlayerBecomesReady = delegate { }; // Player loads game scene;

    public IReadOnlyDictionary<NetObjectUID, NetObjectState> ObjectsRegisterRequests => m_ObjectsRegisterRequests;
    private Dictionary<NetObjectUID, NetObjectState> m_ObjectsRegisterRequests;

    public IReadOnlyDictionary<NetObjectUID, NetObjectStateSnapshot> RemoteObjectUpdateRequests => m_RemoteObjectUpdateRequests;    
    private Dictionary<NetObjectUID, NetObjectStateSnapshot> m_RemoteObjectUpdateRequests;

    public IReadOnlyDictionary<ClientUID, NetWorldPlayerSnapshot> RemotePlayersUpdateRequests => m_RemotePlayersUpdateRequests;
    private Dictionary<ClientUID, NetWorldPlayerSnapshot> m_RemotePlayersUpdateRequests;

    private Queue<byte[]> m_RemoteWorldDataUpdateRequests;

    public IReadOnlyHistoryBuffer HistoryBuffer => m_HistoryBuffer;
    private HistoryBuffer m_HistoryBuffer;

    private HashSet<ClientUID> m_LocalPlayerRegisterQueue;
    private HashSet<ClientUID> m_PlayerUnregisterQueue;

    public int OutgoingMessageQueueCount => m_OutgoingMessageQueue.Count;
    private Queue<Action> m_OutgoingMessageQueue;

    private readonly Action<uint, byte[], DeliveryMethod> m_Sender;
    private float m_LastUpdateTime;

    public float FixedTime => UnityEngine.Time.fixedUnscaledTime;
    
    private readonly NetWriter m_Writer = new();
    private readonly NetReader m_Reader = new();

    public NetWorldRuntime(Action<uint, byte[], DeliveryMethod> sender, string worldName = "NetworkWorldRoot") : base(NetWorldID.Allocate(), worldName)
    {
        m_Sender = sender ?? throw new ArgumentNullException(nameof(sender));

        NetObjectUID.Reset();

        m_LastUpdateTime = FixedTime;

        // Must be cleaned, when player disconnects;
        {
            m_RemoteObjectUpdateRequests = new Dictionary<NetObjectUID, NetObjectStateSnapshot>();
            m_RemotePlayersUpdateRequests = new Dictionary<ClientUID, NetWorldPlayerSnapshot>();
            m_ObjectsRegisterRequests = new Dictionary<NetObjectUID, NetObjectState>();
            m_RemoteWorldDataUpdateRequests = new Queue<byte[]>();
        }

        m_LocalPlayerRegisterQueue = new HashSet<ClientUID>();
        m_PlayerUnregisterQueue = new HashSet<ClientUID>();

        m_HistoryBuffer = new HistoryBuffer();

        m_OutgoingMessageQueue = new Queue<Action>();
    }

    public NetWorldEvaluator Replicate() => 
        new(runtime: this);

    public void ScheduleUpdateForRemoteObject(NetObjectStateSnapshot snapshot)
    {
        if (!snapshot.ObjectUID.IsValid)
        {
            Debug.LogError($"Unable to schedule update for remote object '{snapshot.ObjectUID}' '{snapshot.PrefabId}', invalid UID.");
            return;
        }

        if (m_RemoteObjectUpdateRequests.TryAdd(snapshot.ObjectUID, snapshot)) 
            return;
        
        var current = m_RemoteObjectUpdateRequests[snapshot.ObjectUID];
        if (current.StateChangeType < snapshot.StateChangeType)
            m_RemoteObjectUpdateRequests[snapshot.ObjectUID] = snapshot;
        else
            Debug.LogWarning($"Unable to schedule update for remote object '{snapshot.ObjectUID}' '{snapshot.PrefabId}' with '{snapshot.StateChangeType}', already scheduled update with the higher priority.");
    }

    public void ScheduleUpdateForRemotePlayer(NetWorldPlayerSnapshot snapshot)
    {
        if (!m_RemotePlayersUpdateRequests.TryAdd(snapshot.UID, snapshot))
             Debug.LogWarning($"Unable to schedule update for remote player '{snapshot.UID}': already scheduled.");
    }

    public void ScheduleRemoteWorldDataUpdate(byte[] data) => m_RemoteWorldDataUpdateRequests.Enqueue(data);

    protected override void UpdateImplementation(float deltaTime)
    {
        var worldDelta = FixedTime - m_LastUpdateTime;
        if (worldDelta >= MIN_UPDATE_INTERVAL)
        {
            m_LastUpdateTime = FixedTime;
            UpdateWorldState(worldDelta);
        }

        var deltaRatio = (MIN_UPDATE_INTERVAL - (FixedTime - m_LastUpdateTime)) / deltaTime;
        var messageToSendCount = m_OutgoingMessageQueue.Count / deltaRatio;

        var sent = 0f;
        while (sent < messageToSendCount && m_OutgoingMessageQueue.Count > 0)
        {
            m_OutgoingMessageQueue.Dequeue()();
            sent++;
        }
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
        UpdateRPCRequestsWithoutAuthority();
        UpdateLocalObjectState();

        #region Internal
        void TrySendWorldData()
        {
            if (!PlayersInternal.LocalPlayerIsMaster)
                return;

            m_Writer.SeekZero();
            m_Writer.WriteSingle(Time);
            ScheduleSend(opCode: MessageCodes.WorldCommunication, DeliveryMethod.Unreliable, data: m_Writer.ToArray());
        }

        void TryReceiveWorldData()
        {
            while (m_RemoteWorldDataUpdateRequests.Count > 0)
            {
                m_Reader.Replace(buffer: m_RemoteWorldDataUpdateRequests.Dequeue());
                Time = m_Reader.ReadSingle();
            }
        }

        void UpdatePlayerStates()
        {
            // Add local.
            {
                foreach (var p in m_LocalPlayerRegisterQueue)
                    PlayerRegistered(PlayersInternal.RegisterImmediate(p, isLocal: true).ToSnapshot()); //Add player and notify;

                m_LocalPlayerRegisterQueue.Clear();
            }

            // Remove all.
            {
                foreach (var clientUid in m_PlayerUnregisterQueue)
                    PlayersInternal.UnregisterImmediate(clientUid);

                m_PlayerUnregisterQueue.Clear();
            }

            // Update remotes.
            {
                foreach (var uid in m_RemotePlayersUpdateRequests.Keys.ToArray())
                {
                    var snapshot = m_RemotePlayersUpdateRequests[uid];
                    var player = Players.Find(uid);

                    // Player not exists.
                    if (player == null)
                    {
                        // Create new.
                        player = PlayersInternal.RegisterImmediate(snapshot.UID, isLocal: false);

                        //Notify player registered.
                        PlayerRegistered(player.ToSnapshot());

                        ForEachStates((_, state) => 
                        {
                            if (state.IsOwnedByLocalPlayer)
                                state.ScheduleUpdate(ObjectStateChangeRequestType.InitializationUpdate);
                        });
                    }

                    var wasInGameScene = player.IsReady;
                    // Restore player.
                    player.RestoreFromSnapshot(snapshot);

                    // Remote player loads scene.
                    if (player.IsReady && !wasInGameScene)
                        PlayerBecomesReady(player.ToSnapshot());
                }
                m_RemotePlayersUpdateRequests.Clear();
            }

            // Send local player.
            {
                var localPlayer = Players.LocalPlayer;
                if (localPlayer == null)
                    Debug.LogError("Unable to send local player: doesn't exists");
                else
                    ScheduleSend(MessageCodes.PlayerStateUpdate, DeliveryMethod.Reliable, localPlayer.Serialize());
            }
        }

        // Update states based on removed player, if player leave, remove object.
        void UpdateStateBasedOnRemovedPlayers()
        {
            ForEachStates(processor: (stateUID, state) =>
            {
                foreach (var playerUID in PlayersInternal.RemovedPlayers.ToArray())
                {
                    //Object owned by disconnected player.
                    if (state.IsOwnedBy(playerUID))
                    {
                        //Change owner, if object is scene object.
                        if (state.IsSceneObject)
                        {
                            //Is local client becomes master client, set object's owner and sync it.
                            if (PlayersInternal.LocalPlayer != null && PlayersInternal.LocalPlayer.IsMaster)
                                state.ScheduleOwnerChanged(PlayersInternal.LocalPlayer.UID);
                            else
                                state.InvalidateOwner();

                            TryNotifyViewAboutDisconnectedPlayer(playerUID, stateUID);
                        }
                        //Destroy object.
                        else
                            RemoveObject(stateUID);
                    }
                    //Notify view.
                    else
                        TryNotifyViewAboutDisconnectedPlayer(playerUID, stateUID);
                }
            });
            return;

            void TryNotifyViewAboutDisconnectedPlayer(ClientUID clientUID, NetObjectUID objectUID)
            {
                if (View != null && View.CurrentObjectViews.TryGetValue(objectUID, out var view))
                    view.OnDisconnected(clientUID);
            }
        }

        // Update state based on remote incoming data.
        void UpdateStateBasedOnRemoteData()
        {
            ForEachRemoteRequests((uid, snapshot) =>
            {
                if (!snapshot.ObjectUID.IsValid)
                    throw new InvalidOperationException($"Invalid UID: '{snapshot.ObjectUID}'  Prefab: '{snapshot.PrefabId}'");

                // Check if owner exists.
                if (!PlayersInternal.Contains(snapshot.OwnerUID))
                {
                    m_RemoteObjectUpdateRequests.Remove(uid);
                    return;
                }

                // Object exists, just update.
                if (ObjectStates.TryGetValue(snapshot.ObjectUID, out var objState))
                {
                    objState.ChangeObjectRemotely(snapshot);
                    m_RemoteObjectUpdateRequests.Remove(uid);

                    if (objState.StateChangeType == ObjectStateChangeRequestType.RemoveUpdateHard)
                        RemoveObject(snapshot.ObjectUID);

                    return;
                }

                // Object doesn't exists.
                if (snapshot.StateChangeType == ObjectStateChangeRequestType.RemoveUpdateHard || snapshot.StateChangeType == ObjectStateChangeRequestType.RemoveUpdateSoft)
                {
                    Debug.LogError($"Remote update snapshot form object with UID: {snapshot.ObjectUID}:{snapshot.PrefabId} has state {snapshot.StateChangeType}, but object doesn't exists");
                    
                    m_ObjectsRegisterRequests.Remove(snapshot.ObjectUID);
                    m_RemoteObjectUpdateRequests.Remove(uid);
                    return;
                }

                // Add new or get pending.
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
                        Debug.LogError($"Unable to restore object {newState} form {snapshot}");
                }
                else
                    newState = new NetObjectState(snapshot, PlayersInternal.Find);

                TryAddObject(newState);
                m_RemoteObjectUpdateRequests.Remove(uid);
            });
        }

        // Update RPC requests (Calling rpc without authority).
        void UpdateRPCRequestsWithoutAuthority()
        {
            ForEachStates((_, state) => 
            {
                foreach (var r in state.RPCWithoutAuthorityRequests)
                    ScheduleSend(MessageCodes.RPC, r.DeliveryMethod, r.State.ToData());
            });
        }

        // Update state based on local object changes.
        void UpdateLocalObjectState()
        {
            // Register new local object, that was just created.
            foreach (var uid in m_ObjectsRegisterRequests.Keys.ToArray())
            {
                var state = m_ObjectsRegisterRequests[uid];
                // Skip remote objects.
                if (!state.IsOwnedByLocalPlayer)
                    continue;

                if (ObjectExists(state.UID))
                    Debug.LogError($"Object with UID {state.UID} already exists");
                else
                {
                    state.ScheduleUpdate(ObjectStateChangeRequestType.InitializationUpdate);
                    TryAddObject(state);
                }

                m_ObjectsRegisterRequests.Remove(uid);
            }

            // Update and send local objects.
            ForEachLocalStates((uid, state) =>
            {
                var deliveryIntent = DeliveryMethod.Unreliable;
                if (state.StateChangeType >= ObjectStateChangeRequestType.ReliableUpdate)
                    deliveryIntent = DeliveryMethod.Reliable;

                state.PrepareForSerialization();
                ScheduleSend(MessageCodes.ObjectStateUpdate, deliveryIntent, state.Serialize());

                switch (state.StateChangeType)
                {
                    case ObjectStateChangeRequestType.RemoveUpdateHard:
                        RemoveObject(uid);
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

        m_HistoryBuffer.Enqueue(ToSnapshot(worldDelta));
        while (m_HistoryBuffer.LengthInSeconds > HISTORY_BUFFER_MAX_LENGTH_IN_SECONDS)
            m_HistoryBuffer.Dequeue();
    }
    #endregion

    private void ScheduleSend(uint opCode, DeliveryMethod method, byte[] data) =>
        m_OutgoingMessageQueue.Enqueue(() =>
        {
            try
            {
                m_Sender(opCode, data, method);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"{data.Length} + {opCode}");
            }
        });

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

        m_HistoryBuffer.Clear();
        m_HistoryBuffer = null;

        m_OutgoingMessageQueue.Clear();
        m_OutgoingMessageQueue = null;

        PlayerRegistered = null;
    }

    // Connected.
    public void RegisterLocalPlayer(ClientUID peerId) => 
        m_LocalPlayerRegisterQueue.Add(peerId);

    // Disconnected.
    public void OnDisconnect(ClientUID uid)
    {
        if (m_RemotePlayersUpdateRequests.Remove(uid))
            Debug.LogError($"Removed absent update request for player '{uid}'.");

        m_LocalPlayerRegisterQueue.Remove(uid);
        m_PlayerUnregisterQueue.Add(uid);
    }

    // Scene loaded.
    public void OnSceneLoaded() => 
        ScheduleOnUpdate(() =>
        {            
            //Check for player exists.
            var player = PlayersInternal.LocalPlayer;
            if (player != null)
                player.IsReady = true;

            //Create view.
            InitializeView();
        });

    private void ForEachRemoteRequests(Action<NetObjectUID, NetObjectStateSnapshot> processor)
    {
        foreach (var uid in m_RemoteObjectUpdateRequests.Keys.ToArray())
            processor(uid, m_RemoteObjectUpdateRequests[uid]);
    }

    /// <summary>
    /// Create object with position and rotation input.
    /// Should be called on local client to create object across network.
    /// </summary>
    public NetObjectIdentity CreateObject(NetWorldPlayer owner, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation)
    {
        if (!owner.IsLocal)
            throw new InvalidOperationException();
        
        var state = new NetObjectState(new NetObjectUID(owner.UID), prefabId, isSceneObject, owner.UID, PlayersInternal.Find);
        m_ObjectsRegisterRequests.Add(state.UID, state);
        return View.CreateImmediate(state, position, rotation);      
    }

    /// <summary>
    /// Register object, which is not created via main object creation pipline.
    /// See 'CreateObject' method.
    /// This is not initialize object, so object will be invalid, untill first update happens.
    /// </summary>
    public void TryRegisterOrInitializeSceneObjectIdentity(GSSceneObjectIdentity sceneIdentity)
    {
        if (ObjectExists(sceneIdentity.SceneObjectId))
        {
#if NETWORK_DEBUG
            Debug.LogError($"Trying to register scene identity '{sceneIdentity.gameObject.name}' '{sceneIdentity.PrefabID}': but object with id '{sceneIdentity.SceneObjectId}' already registered.");
#endif
            UnityEngine.Object.Destroy(sceneIdentity.gameObject);
            return;
        }

        if (sceneIdentity.Identity.Initialized)
            throw new InvalidOperationException($"'{sceneIdentity.gameObject.name}' already initialized.");

        // Create state from scene object identity.
        // We don't need to know scene object prefab id, because it's already in scene, but prefab id can't be null.
        var newState = new NetObjectState(sceneIdentity.SceneObjectId, sceneIdentity.PrefabID, true, PlayersInternal.MasterPlayerUID, PlayersInternal.Find);
        m_ObjectsRegisterRequests.Add(newState.UID, newState);
        var newStateUID = newState.UID;

        //Wait for view initialization.
        ScheduleOnViewInit(() =>
        {
            if (TryGetExistedOrPendingState(newStateUID, out var state))
            {
                //Initialize immateriality on master client.
                if (PlayersInternal.LocalPlayerIsMaster)
                    View.InitializeObjectView(sceneIdentity.Identity, state.UID);

                //Add view to be used in the future.
                View.RegisterAsPending(newStateUID, sceneIdentity.Identity);
            }
            else
            {
                UnityEngine.Object.Destroy(sceneIdentity.gameObject);
                Debug.LogError($"Unable to initialize '{sceneIdentity.gameObject.name}'. Object or pending state with UID '{newStateUID}' not exist.");
            }
        });
    }

    private bool TryGetExistedOrPendingState(NetObjectUID instanceId, out NetObjectState state) => 
        TryGetObject(instanceId, out state) || m_ObjectsRegisterRequests.TryGetValue(instanceId, out state);

    //TODO: Think how to avoid this override;
    public override NetObjectState FindObject(NetObjectUID instanceId)
    {
        var existed = base.FindObject(instanceId);
        if (existed == null)
            m_ObjectsRegisterRequests.TryGetValue(instanceId, out existed);

        return existed;
    }
    
    /// <summary>
    /// Get object before in was actually created by transport system.
    /// Note that this object will not be in valid state, until update happens.
    /// Use in only if it's highly necessary.
    /// </summary>
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
            return View.CreateImmediate(state, position, rotation);
        }
    }

    public T FindObjectViewOrCreateMock<T>(NetWorldPlayer owner, NetObjectUID instanceId, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation) where T : GSInstanceBehaviourBase
    {
        var networkObject = FindObjectViewOrCreateMock(owner, instanceId, prefabId, isSceneObject, position, rotation);
        return networkObject == null ? null : networkObject.GetComponent<T>();
    }

    public void ReceiveRPCWithoutAuthority(RPCWithoutAuthorityState state)
    {
        if (!state.IsValid)
            throw new ArgumentException();

        var objectIdentity = FindObjectView(state.ObjectUID);

        // We need to handle rpc receive for objects, which are doesn't exist.
        // It is probably already destroyed.
        // No exceptions here, just warning.
        if (objectIdentity == null)
        {
#if NETWORK_DEBUG
            var rpcName = RPCUtils.TryFindStringByHashSlow(state.RPCState.Hash);
            if (string.IsNullOrWhiteSpace(rpcName))
                rpcName = "empty";

            Debug.LogError($"Trying to receive rpc with code '{state.RPCState.Hash}' '{rpcName}' for object '{state.ObjectUID}' that doesnt exists, maybe it is already destroyed.");
#endif
            return;
        }

        var receiver = (INoAuthorityRPCReceiver)objectIdentity;

        switch (state.Type)
        {
            case NoAuthorityRPCType.ToOwner:
                //Check if we are this object's owner
                if (objectIdentity.IsLocal)
                    receiver.ReceiveRPCLocally(state);
                break;
            case NoAuthorityRPCType.ToMaster:
                if (objectIdentity.IsOwnedByMasterClient)
                    receiver.ReceiveRPCOnMasterClient(state);
                break;

            case NoAuthorityRPCType.ToAll:
                receiver.ReceiveRPC(state);
                break;
        }
    }

    public int GetHistoryBufferSize() => 
        m_HistoryBuffer.Size;
}
