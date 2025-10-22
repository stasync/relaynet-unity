using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Core.Networking;

public sealed class NetWorldRuntime : NetWorld
{
    public delegate void DataSendRequestDelegate(uint userDataOpCode, byte[] data, DeliveryMethod deliveryMethod);

    private const float HISTORY_BUFFER_MAX_LENGTH_IN_SECONDS = 10f; // Record last 'N' seconds.
    private const float MIN_UPDATE_INTERVAL = 0.1f; // 10 times per second.
    
    public event Action<NetWorldPlayerSnapshot> PlayerAdded = delegate { };
    public event Action<NetWorldPlayerSnapshot> PlayerRemoved = delegate { };

    public event Action<NetWorldPlayerSnapshot> PlayerBecomesReady = delegate { }; // Player loads game scene;
    public event DataSendRequestDelegate DataSendRequested = delegate { };

    public IReadOnlyDictionary<NetObjectUID, NetObjectState> ObjectsRegisterRequests => m_ObjectsRegisterRequests;
    private Dictionary<NetObjectUID, NetObjectState> m_ObjectsRegisterRequests = new();

    public IReadOnlyDictionary<NetObjectUID, NetObjectStateSnapshot> RemoteObjectUpdateRequests => m_RemoteObjectUpdateRequests;    
    private Dictionary<NetObjectUID, NetObjectStateSnapshot> m_RemoteObjectUpdateRequests = new();

    public IReadOnlyDictionary<ClientUID, NetWorldPlayerSnapshot> RemotePlayersUpdateRequests => m_RemotePlayersUpdateRequests;
    private Dictionary<ClientUID, NetWorldPlayerSnapshot> m_RemotePlayersUpdateRequests = new();

    private Queue<byte[]> m_RemoteWorldDataUpdateRequests = new();

    public IReadOnlyHistoryBuffer HistoryBuffer => m_HistoryBuffer;
    private HistoryBuffer m_HistoryBuffer = new ();

    private HashSet<ClientUID> m_LocalPlayerRegisterQueue = new ();
    private HashSet<ClientUID> m_PlayerUnregisterQueue = new();

    private float m_LastUpdateTime;
    
    private readonly NetWriter m_Writer = new();
    private readonly NetReader m_Reader = new();

    public NetWorldRuntime(string worldName = "NetworkWorldRoot") : base(NetWorldID.Allocate(), worldName)
    {
        NetObjectUID.Reset();
        m_LastUpdateTime = UnityEngine.Time.fixedUnscaledTime;
    }

    public NetWorldEvaluator Replicate() => 
        new(runtime: this);

    public void ScheduleRemoteUpdate(uint userDataOpCode, byte[] data)
    {
        m_Reader.Replace(data);
        switch (userDataOpCode)
        {
            // Other client wants to tell us about its player state.
            case MessageCodes.PlayerStateUpdate:
            {
                // Add player, if not exists.
                // Deserialize other player.
                // Adding player id duplicated logic.
                var snapshot = NetWorldPlayerSnapshot.FormReader(m_Reader);
                if (!m_RemotePlayersUpdateRequests.TryAdd(snapshot.UID, snapshot))
                    Debug.LogWarning($"Unable to schedule update for remote player '{snapshot.UID}': already scheduled.");
                
                break;
            }
            case MessageCodes.ObjectStateUpdate:
            {
                var snapshot = NetObjectStateSnapshot.FormReader(m_Reader);
                if (!snapshot.ObjectUID.IsValid)
                {
                    Debug.LogError($"Unable to schedule update for remote object '{snapshot.ObjectUID}' '{snapshot.PrefabId}', invalid Uid.");
                    break;
                }

                if (m_RemoteObjectUpdateRequests.TryAdd(snapshot.ObjectUID, snapshot)) 
                    break;
        
                var current = m_RemoteObjectUpdateRequests[snapshot.ObjectUID];
                if (current.StateChangeType < snapshot.StateChangeType)
                    m_RemoteObjectUpdateRequests[snapshot.ObjectUID] = snapshot;
                else
                    Debug.LogWarning($"Unable to schedule update for remote object '{snapshot.ObjectUID}' '{snapshot.PrefabId}' with '{snapshot.StateChangeType}', already scheduled update with the higher priority.");
                
                break;
            }

            case MessageCodes.WorldCommunication:
            {
                m_RemoteWorldDataUpdateRequests.Enqueue(data);
                break;
            }

            case MessageCodes.RPC:
            {
                var state = RPCWithoutAuthorityState.FromReader(m_Reader);
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
                    break;
                }

                var receiver = (INoAuthorityRPCReceiver)objectIdentity;

                switch (state.Type)
                {
                    case NoAuthorityRPCType.ToOwner:
                    {
                        // Check if we are this object's owner.
                        if (objectIdentity.IsLocal)
                            receiver.ReceiveRPCLocally(state);
                        
                        break;
                    }
                    case NoAuthorityRPCType.ToMaster:
                    {
                        if (objectIdentity.IsOwnedByMasterClient)
                            receiver.ReceiveRPCOnMasterClient(state);
                        
                        break;
                    }
                    case NoAuthorityRPCType.ToAll:
                    {
                        receiver.ReceiveRPC(state);
                        break;
                    }
                }
                
                break;
            }
        }
    }

    protected override void UpdateImplementation(float deltaTime)
    {
        var worldDelta = UnityEngine.Time.fixedUnscaledTime - m_LastUpdateTime;
        if (worldDelta >= MIN_UPDATE_INTERVAL)
        {
            m_LastUpdateTime = UnityEngine.Time.fixedUnscaledTime;
            UpdateWorldState(worldDelta);
        }
    }

    #region Loop
    protected override void OnUpdateWorldState(float worldDelta)
    {
        base.OnUpdateWorldState(worldDelta);

        TrySendWorldData();
        
        UpdateRemoteMasterWorldData();
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
            DataSendRequested(userDataOpCode: MessageCodes.WorldCommunication, data: m_Writer.ToArray(), DeliveryMethod.Unreliable);
        }

        void UpdateRemoteMasterWorldData()
        {
            if (!PlayersInternal.LocalPlayerIsMaster)
                return;
            
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
                foreach (var clientUid in m_LocalPlayerRegisterQueue)
                {
                    // Add player and notify.
                    var player = PlayersInternal.RegisterImmediate(clientUid, isLocal: true);
                    PlayerAdded(player.ToSnapshot());
                }

                m_LocalPlayerRegisterQueue.Clear();
            }

            // Remove all.
            {
                foreach (var clientUid in m_PlayerUnregisterQueue)
                {
                    var player = PlayersInternal.Find(clientUid);
                    var playerSnapshot = player?.ToSnapshot() ?? default;
                    PlayersInternal.UnregisterImmediate(clientUid);
                    
                    if (playerSnapshot.UID.IsValid)
                        PlayerRemoved(playerSnapshot);
                }

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

                        // Notify player registered.
                        PlayerAdded(player.ToSnapshot());

                        ForEachStates(processor: (_, state) => 
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
                    DataSendRequested(userDataOpCode: MessageCodes.PlayerStateUpdate, data: localPlayer.Serialize(), DeliveryMethod.Reliable);
            }
        }

        // Update states based on removed player, if player leave, remove object.
        void UpdateStateBasedOnRemovedPlayers()
        {
            ForEachStates(processor: (stateUID, state) =>
            {
                foreach (var playerUID in PlayersInternal.RemovedPlayers.ToArray())
                {
                    // Object owned by disconnected player.
                    if (state.IsOwnedBy(playerUID))
                    {
                        // Change owner, if object is scene object.
                        if (state.IsSceneObject)
                        {
                            //Is local client becomes master client, set object's owner and sync it.
                            if (PlayersInternal.LocalPlayerIsMaster)
                                state.ScheduleOwnerChanged(PlayersInternal.LocalPlayer.Uid);
                            else
                                state.InvalidateOwner();

                            TryNotifyViewAboutDisconnectedPlayer(playerUID, stateUID);
                        }
                        // Destroy object.
                        else
                            RemoveObject(stateUID);
                    }
                    // Notify view.
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
            foreach (var uid in m_RemoteObjectUpdateRequests.Keys.ToArray())
            {
                var snapshot = m_RemoteObjectUpdateRequests[uid];
                
                if (!snapshot.ObjectUID.IsValid)
                    throw new InvalidOperationException($"Invalid Uid: '{snapshot.ObjectUID}'  Prefab: '{snapshot.PrefabId}'");

                // Check if owner exists.
                if (!PlayersInternal.Contains(snapshot.OwnerUID))
                {
                    m_RemoteObjectUpdateRequests.Remove(uid);
                    continue;
                }

                // Object exists, just update.
                if (ObjectStates.TryGetValue(snapshot.ObjectUID, out var objState))
                {
                    objState.ChangeObjectRemotely(snapshot);
                    m_RemoteObjectUpdateRequests.Remove(uid);

                    if (objState.StateChangeType == ObjectStateChangeRequestType.RemoveUpdateHard)
                        RemoveObject(snapshot.ObjectUID);

                    continue;
                }

                // Object doesn't exists.
                if (snapshot.StateChangeType == ObjectStateChangeRequestType.RemoveUpdateHard || snapshot.StateChangeType == ObjectStateChangeRequestType.RemoveUpdateSoft)
                {
                    Debug.LogError($"Remote update snapshot form object with Uid: {snapshot.ObjectUID}:{snapshot.PrefabId} has state {snapshot.StateChangeType}, but object doesn't exists");
                    
                    m_ObjectsRegisterRequests.Remove(snapshot.ObjectUID);
                    m_RemoteObjectUpdateRequests.Remove(uid);
                    continue;
                }

                // Add new or get pending.
                if (snapshot.StateChangeType != ObjectStateChangeRequestType.InitializationUpdate)
                {
                    m_ObjectsRegisterRequests.Remove(snapshot.ObjectUID);
                    continue;
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
            }
        }

        // Update RPC requests (Calling rpc without authority).
        void UpdateRPCRequestsWithoutAuthority()
        {
            ForEachStates((_, state) => 
            {
                foreach (var request in state.RPCWithoutAuthorityRequests)
                    DataSendRequested(userDataOpCode: MessageCodes.RPC, data: request.State.ToData(), request.DeliveryMethod);
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
                    Debug.LogError($"Object with Uid {state.UID} already exists");
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
                var deliveryMethod = DeliveryMethod.Unreliable;
                if (state.StateChangeType >= ObjectStateChangeRequestType.ReliableUpdate)
                    deliveryMethod = DeliveryMethod.Reliable;

                state.PrepareForSerialization();
                DataSendRequested(userDataOpCode: MessageCodes.ObjectStateUpdate, data: state.Serialize(), deliveryMethod);

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
    
    protected override void RemoveObject(NetObjectUID stateUID)
    {
        base.RemoveObject(stateUID);

        m_ObjectsRegisterRequests.Remove(stateUID);
        m_RemoteObjectUpdateRequests.Remove(stateUID);
    }

    /// <summary>
    /// Destroy all network objects and cleanup scene.
    /// </summary>
    public override void Dispose()
    {
        base.Dispose();

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

        PlayerAdded = null;
    }

    // Connected.
    internal void RegisterLocalPlayer(ClientUID peerId) => 
        m_LocalPlayerRegisterQueue.Add(peerId);

    // Disconnected.
    internal void OnDisconnect(ClientUID uid)
    {
        if (m_RemotePlayersUpdateRequests.Remove(uid))
            Debug.LogError($"Removed absent update request for player '{uid}'.");

        m_LocalPlayerRegisterQueue.Remove(uid);
        m_PlayerUnregisterQueue.Add(uid);
    }

    // Scene loaded.
    public void OnSceneLoaded()
    {
        // Check for player exists.
        var player = PlayersInternal.LocalPlayer;
        if (player != null)
        {
            player.IsReady = true;
            PlayerBecomesReady(player.ToSnapshot());
        }
        
        // Create view.
        InitializeView();
    }
    
    /// <summary>
    /// Create object with position and rotation input.
    /// Should be called on local client to create object across network.
    /// </summary>
    public NetObjectIdentity CreateObject(NetWorldPlayer owner, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation)
    {
        if (!owner.IsLocal)
            throw new InvalidOperationException();
        
        var state = new NetObjectState(new NetObjectUID(owner.Uid), prefabId, isSceneObject, owner.Uid, PlayersInternal.Find);
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
        var newState = new NetObjectState(sceneIdentity.SceneObjectId, sceneIdentity.PrefabID, isSceneObject: true, PlayersInternal.MasterPlayerUid, PlayersInternal.Find);
        m_ObjectsRegisterRequests.Add(newState.UID, newState);
        var newStateUID = newState.UID;

        // Wait for view initialization.
        ScheduleOnViewInit(() =>
        {
            if (TryGetExistedOrPendingState(newStateUID, out var state))
            {
                // Initialize immateriality on master client.
                if (PlayersInternal.LocalPlayerIsMaster)
                    View.InitializeObjectView(sceneIdentity.Identity, state.UID);

                // Add view to be used in the future.
                View.RegisterAsPending(newStateUID, sceneIdentity.Identity);
            }
            else
            {
                UnityEngine.Object.Destroy(sceneIdentity.gameObject);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError($"Unable to initialize '{sceneIdentity.gameObject.name}'. Object or pending state with Uid '{newStateUID}' not exist.");
#endif
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
            var state = new NetObjectState(instanceId, prefabId, isSceneObject, owner.Uid, PlayersInternal.Find);
            m_ObjectsRegisterRequests.Add(instanceId, state);
            return View.CreateImmediate(state, position, rotation);
        }
    }

    public T FindObjectViewOrCreateMock<T>(NetWorldPlayer owner, NetObjectUID instanceId, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation) where T : GSInstanceBehaviourBase
    {
        var networkObject = FindObjectViewOrCreateMock(owner, instanceId, prefabId, isSceneObject, position, rotation);
        return networkObject == null ? null : networkObject.GetComponent<T>();
    }

    public int GetHistoryBufferSize() => 
        m_HistoryBuffer.Size;
}
