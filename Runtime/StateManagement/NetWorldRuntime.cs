using System;
using System.Collections.Generic;
using UnityEngine;
using Core.Networking;

namespace LROTech.Network.StateManagement
{
    public sealed class NetWorldRuntime : NetWorld
    {
        public struct IncomingStateChangeRequest
        {
            public readonly uint OpCode;
            public readonly byte[] Data;

            internal IncomingStateChangeRequest(uint opCode, byte[] data)
            {
                OpCode = opCode;
                Data = data;
            }
        }

        public delegate void DataSendRequestDelegate(uint userDataOpCode, byte[] data, DeliveryMethod deliveryMethod);

        private const float HISTORY_BUFFER_MAX_LENGTH_IN_SECONDS = 10f; // Record last 'N' seconds.
        private const float MIN_UPDATE_INTERVAL = 0.1f; // 10 times per second.

        public event Action<NetWorldPlayerSnapshot> PlayerAdded = delegate { };
        public event Action<NetWorldPlayerSnapshot> PlayerRemoved = delegate { };

        public event Action<NetWorldPlayerSnapshot> PlayerBecomesReady = delegate { }; // Player loads game scene;
        public event DataSendRequestDelegate DataSendRequested = delegate { };

        public IReadOnlyCollection<IncomingStateChangeRequest> IncomingStateChangeRequests => m_IncomingStateChangeRequests;
        private readonly Queue<IncomingStateChangeRequest> m_IncomingStateChangeRequests = new();

        public IReadOnlyDictionary<NetObjectUID, NetObjectState> TemporaryUnregisteredObjects => m_TemporaryUnregisteredObjects;
        private readonly Dictionary<NetObjectUID, NetObjectState> m_TemporaryUnregisteredObjects = new();

        public readonly NetWorldHistoryBuffer HistoryBuffer = new();

        private readonly HashSet<ClientUID> m_LocalPlayerRegisterQueue = new();
        private readonly HashSet<ClientUID> m_PlayerUnregisterQueue = new();

        private float m_LastUpdateTime;

        private readonly NetWriter m_Writer = new();
        private readonly NetReader m_Reader = new();

        public NetWorldRuntime(string worldName = "NetworkWorldRoot") : base(NetWorldID.Allocate(), worldName)
        {
            NetObjectUID.Reset();
            m_LastUpdateTime = UnityEngine.Time.fixedUnscaledTime;
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

        #region Update

        protected override void OnUpdateWorldState(float worldDelta)
        {
            base.OnUpdateWorldState(worldDelta);

            var remotePlayersUpdateRequests = new Dictionary<ClientUID, NetWorldPlayerSnapshot>();
            var remoteObjectUpdateRequests = new Dictionary<NetObjectUID, NetObjectStateSnapshot>();
            var remoteWorldDataUpdateRequests = new Queue<byte[]>();
            var removedPlayers = new Queue<ClientUID>();

            TrySendWorldData();

            ProcessIncomingData();

            UpdateRemoteMasterWorldData();
            UpdatePlayerStates();
            UpdateObjectStateBasedOnRemovedPlayers();
            UpdateObjectStateBasedOnRemoteData();
            UpdateRPCRequestsWithoutAuthority();
            UpdateLocalObjectState();

            #region Internal

            void ProcessIncomingData()
            {
                while (m_IncomingStateChangeRequests.TryDequeue(out var stateChangeRequest))
                {
                    m_Reader.Replace(stateChangeRequest.Data);
                    switch (stateChangeRequest.OpCode)
                    {
                        // Other client wants to tell us about its player state.
                        case MessageCodes.PlayerStateUpdate:
                        {
                            // Add player, if not exists.
                            // Deserialize other player.
                            // Adding player id duplicated logic.
                            var snapshot = NetWorldPlayerSnapshot.FormReader(m_Reader);
                            if (!remotePlayersUpdateRequests.TryAdd(snapshot.UID, snapshot))
                                Debug.LogWarning($"Unable to schedule update for remote player '{snapshot.UID}': already scheduled.");

                            break;
                        }
                        case MessageCodes.ObjectStateUpdate:
                        {
                            var snapshot = NetObjectStateSnapshot.FormReader(m_Reader);
                            if (!snapshot.ObjectUID.IsValid)
                            {
                                Debug.LogError(
                                    $"Unable to schedule update for remote object '{snapshot.ObjectUID}' '{snapshot.PrefabId}', invalid Uid.");
                                break;
                            }

                            if (remoteObjectUpdateRequests.TryAdd(snapshot.ObjectUID, snapshot))
                                break;

                            var current = remoteObjectUpdateRequests[snapshot.ObjectUID];
                            if (current.StateChangeType < snapshot.StateChangeType)
                                remoteObjectUpdateRequests[snapshot.ObjectUID] = snapshot;
                            else
                                Debug.LogWarning(
                                    $"Unable to schedule update for remote object '{snapshot.ObjectUID}' '{snapshot.PrefabId}' with '{snapshot.StateChangeType}', already scheduled update with the higher priority.");

                            break;
                        }

                        case MessageCodes.WorldCommunication:
                        {
                            remoteWorldDataUpdateRequests.Enqueue(stateChangeRequest.Data);
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
                                var rpcHandlerMethod = NetObjectRPCUtils.TryFindMethodInfoByHashSlow(state.RPCState.Hash);
                                var rpcHandlerMethodName = rpcHandlerMethod == null
                                    ? "UNKNOWN"
                                    : $"{rpcHandlerMethod.DeclaringType!.FullName}.{rpcHandlerMethod.Name}";

                                Debug.LogError(
                                    $"Trying to receive rpc with code '{state.RPCState.Hash}' '{rpcHandlerMethodName}' for object '{state.ObjectUID}' that doesnt exists, maybe it is already destroyed.");
#endif
                                break;
                            }

                            switch (state.Type)
                            {
                                case NoAuthorityRPCType.ToOwner:
                                {
                                    // Check if we are this object's owner.
                                    if (objectIdentity.IsLocal)
                                        objectIdentity.ReceiveRPCLocally(state);

                                    break;
                                }
                                case NoAuthorityRPCType.ToMaster:
                                {
                                    if (objectIdentity.IsOwnedByMasterClient)
                                        objectIdentity.ReceiveRPCOnMasterClient(state);

                                    break;
                                }
                                case NoAuthorityRPCType.ToAll:
                                {
                                    objectIdentity.ReceiveRPC(state);
                                    break;
                                }
                            }

                            break;
                        }
                    }
                }
            }

            void TrySendWorldData()
            {
                if (!Players.LocalPlayerIsMaster)
                    return;

                m_Writer.SeekZero();
                m_Writer.WriteSingle(Time);
                Properties.Serialize(m_Writer);
                DataSendRequested(userDataOpCode: MessageCodes.WorldCommunication, data: m_Writer.ToArray(), DeliveryMethod.Unreliable);
            }

            void UpdateRemoteMasterWorldData()
            {
                if (!Players.LocalPlayerIsMaster)
                    return;

                while (remoteWorldDataUpdateRequests.Count > 0)
                {
                    m_Reader.Replace(buffer: remoteWorldDataUpdateRequests.Dequeue());
                    Time = m_Reader.ReadSingle();
                    Properties.Deserialize(m_Reader, firstTime: false);
                }
            }

            void UpdatePlayerStates()
            {
                // Add local.
                {
                    foreach (var clientUid in m_LocalPlayerRegisterQueue)
                    {
                        // Add player and notify.
                        var player = Players.RegisterImmediate(clientUid, isLocal: true);
                        PlayerAdded(player.ToSnapshot());
                    }

                    m_LocalPlayerRegisterQueue.Clear();
                }

                // Remove all.
                {
                    foreach (var clientUid in m_PlayerUnregisterQueue)
                    {
                        var player = Players.Find(clientUid);
                        var playerSnapshot = player?.ToSnapshot() ?? default;
                        if (Players.UnregisterImmediate(clientUid))
                            removedPlayers.Enqueue(clientUid);

                        if (playerSnapshot.UID.IsValid)
                            PlayerRemoved(playerSnapshot);
                    }

                    m_PlayerUnregisterQueue.Clear();
                }

                // Update remotes.
                {
                    foreach (var clientUid in new List<ClientUID>(remotePlayersUpdateRequests.Keys))
                    {
                        var snapshot = remotePlayersUpdateRequests[clientUid];
                        var player = Players.Find(clientUid);

                        // Player not exists.
                        if (player == null)
                        {
                            // Create new.
                            player = Players.RegisterImmediate(snapshot.UID, isLocal: false);

                            // Notify player registered.
                            PlayerAdded(player.ToSnapshot());

                            foreach (var objectUid in new List<NetObjectUID>(ObjectStates.Keys))
                            {
                                var state = ObjectStates[objectUid];
                                if (state.IsOwnedByLocalPlayer)
                                    state.ScheduleUpdate(ObjectStateChangeRequestType.InitializationUpdate);
                            }
                        }

                        var wasInGameScene = player.IsReady;
                        // Restore player.
                        player.RestoreFromSnapshot(snapshot);

                        // Remote player loads scene.
                        if (player.IsReady && !wasInGameScene)
                            PlayerBecomesReady(player.ToSnapshot());
                    }

                    remotePlayersUpdateRequests.Clear();
                }

                // Send local player.
                {
                    var localPlayer = Players.LocalPlayer;
                    if (localPlayer == null)
                        Debug.LogError("Unable to send local player: doesn't exists");
                    else
                        DataSendRequested(userDataOpCode: MessageCodes.PlayerStateUpdate, data: localPlayer.Serialize(),
                            DeliveryMethod.Reliable);
                }
            }

            // Update states based on removed player, if player leave, remove object.
            void UpdateObjectStateBasedOnRemovedPlayers()
            {
                foreach (var objectUid in new List<NetObjectUID>(ObjectStates.Keys))
                {
                    var state = ObjectStates[objectUid];
                    while (removedPlayers.TryDequeue(out var removePlayerUid))
                    {
                        // Object owned by disconnected player.
                        if (state.IsOwnedBy(removePlayerUid))
                        {
                            // Change owner, if object is scene object.
                            if (state.IsSceneObject)
                            {
                                //Is local client becomes master client, set object's owner and sync it.
                                if (Players.LocalPlayerIsMaster)
                                    state.ChangedOwner(Players.LocalPlayer.Uid);
                                else
                                    state.InvalidateOwner();

                                if (View != null && View.CurrentObjectViews.TryGetValue(objectUid, out var view))
                                    view.OnDisconnected(removePlayerUid);
                            }
                            // Destroy object.
                            else
                                RemoveObject(objectUid);
                        }
                        // Notify view.
                        else
                        {
                            if (View != null && View.CurrentObjectViews.TryGetValue(objectUid, out var view))
                                view.OnDisconnected(removePlayerUid);
                        }
                    }
                }
            }

            // Update state based on remote incoming data.
            void UpdateObjectStateBasedOnRemoteData()
            {
                foreach (var uid in new List<NetObjectUID>(remoteObjectUpdateRequests.Keys))
                {
                    var snapshot = remoteObjectUpdateRequests[uid];

                    if (!snapshot.ObjectUID.IsValid)
                        throw new InvalidOperationException(
                            $"Invalid Uid: '{snapshot.ObjectUID}'  Prefab: '{snapshot.PrefabId}'");

                    // Check if owner exists.
                    if (!Players.Contains(snapshot.OwnerUID))
                    {
                        remoteObjectUpdateRequests.Remove(uid);
                        continue;
                    }

                    // Object exists, just update.
                    if (ObjectStates.TryGetValue(snapshot.ObjectUID, out var objState))
                    {
                        objState.ChangeObjectRemotely(snapshot);
                        remoteObjectUpdateRequests.Remove(uid);

                        if (objState.StateChangeType == ObjectStateChangeRequestType.RemoveUpdateHard)
                            RemoveObject(snapshot.ObjectUID);

                        continue;
                    }

                    // Object doesn't exists.
                    if (snapshot.StateChangeType is ObjectStateChangeRequestType.RemoveUpdateHard or ObjectStateChangeRequestType.RemoveUpdateSoft)
                    {
                        Debug.LogError(
                            $"Remote update snapshot form object with Uid: {snapshot.ObjectUID}:{snapshot.PrefabId} has state {snapshot.StateChangeType}, but object doesn't exists");

                        m_TemporaryUnregisteredObjects.Remove(snapshot.ObjectUID);
                        remoteObjectUpdateRequests.Remove(uid);
                        continue;
                    }

                    // Add new or get pending.
                    if (snapshot.StateChangeType != ObjectStateChangeRequestType.InitializationUpdate)
                    {
                        m_TemporaryUnregisteredObjects.Remove(snapshot.ObjectUID);
                        continue;
                    }

                    if (m_TemporaryUnregisteredObjects.TryGetValue(snapshot.ObjectUID, out var newState))
                    {
                        if (newState.IsOwnedBy(snapshot.OwnerUID))
                        {
                            newState.ChangeObjectRemotely(snapshot);
                            m_TemporaryUnregisteredObjects.Remove(snapshot.ObjectUID);
                        }
                        else
                            Debug.LogError($"Unable to restore object {newState} form {snapshot}");
                    }
                    else
                        newState = new NetObjectState(snapshot, Players.Find);

                    TryAddObject(newState);
                    remoteObjectUpdateRequests.Remove(uid);
                }
            }

            // Update RPC requests - calling rpc without authority.
            void UpdateRPCRequestsWithoutAuthority()
            {
                foreach (var uid in new List<NetObjectUID>(ObjectStates.Keys))
                {
                    var state = ObjectStates[uid];
                    foreach (var request in state.RPCWithoutAuthorityRequests)
                        DataSendRequested(userDataOpCode: MessageCodes.RPC, data: request.State.ToData(), request.DeliveryMethod);
                }
            }

            // Update state based on local object changes.
            void UpdateLocalObjectState()
            {
                // Register new local object, that was just created.
                foreach (var uid in new List<NetObjectUID>(m_TemporaryUnregisteredObjects.Keys))
                {
                    var state = m_TemporaryUnregisteredObjects[uid];
                    // Skip remote objects.
                    if (!state.IsOwnedByLocalPlayer)
                        continue;

                    if (ObjectExists(state.UID))
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Debug.LogError($"Object with Uid '{state.UID}' already exists");
#endif
                    }
                    else
                    {
                        state.ScheduleUpdate(ObjectStateChangeRequestType.InitializationUpdate);
                        TryAddObject(state);
                    }

                    m_TemporaryUnregisteredObjects.Remove(uid);
                }

                // Update and send local objects.
                foreach (var uid in new List<NetObjectUID>(ObjectStates.Keys))
                {
                    var state = ObjectStates[uid];

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
                }
            }

            #endregion
        }

        protected override void OnBeforeExecuteView(float worldDelta)
        {
            base.OnBeforeExecuteView(worldDelta);

            HistoryBuffer.Enqueue(ToSnapshot(worldDelta));
            while (HistoryBuffer.LengthInSeconds > HISTORY_BUFFER_MAX_LENGTH_IN_SECONDS)
                HistoryBuffer.Dequeue();
        }

        #endregion

        /// <summary>
        /// Destroy all network objects and cleanup scene.
        /// </summary>
        public override void Dispose()
        {
            base.Dispose();

            m_LocalPlayerRegisterQueue.Clear();
            m_PlayerUnregisterQueue.Clear();
            HistoryBuffer.Clear();

            PlayerAdded = null;
        }

        /// <summary>
        /// Create another world that could evaluate and re-player this world's history buffer.
        /// </summary>
        /// <returns></returns>
        public NetWorldEvaluator Replicate() =>
            new(runtime: this);

        protected override void RemoveObject(NetObjectUID stateUID)
        {
            base.RemoveObject(stateUID);
            m_TemporaryUnregisteredObjects.Remove(stateUID);
        }

        internal void RegisterLocalPlayer(ClientUID clientUid) =>
            m_LocalPlayerRegisterQueue.Add(clientUid);

        internal void OnDisconnect(ClientUID clientUid)
        {
            m_LocalPlayerRegisterQueue.Remove(clientUid);
            m_PlayerUnregisterQueue.Add(clientUid);
        }

        internal void ScheduleRemoteUpdate(uint userDataOpCode, byte[] data) =>
            m_IncomingStateChangeRequests.Enqueue(new IncomingStateChangeRequest(userDataOpCode, data));

        private bool TryGetExistedOrPendingState(NetObjectUID instanceId, out NetObjectState state) =>
            TryGetObject(instanceId, out state) || m_TemporaryUnregisteredObjects.TryGetValue(instanceId, out state);

        /// <summary>
        /// Create object with position and rotation input.
        /// Should be called on local client to create object across network.
        /// </summary>
        public NetObjectIdentity CreateObject(NetWorldPlayer owner, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation)
        {
            if (!owner.IsLocal)
                throw new InvalidOperationException();

            var state = new NetObjectState(new NetObjectUID(owner.Uid), prefabId, isSceneObject, owner.Uid, Players.Find);
            m_TemporaryUnregisteredObjects.Add(state.UID, state);
            return View.CreateImmediate(state, position, rotation);
        }

        /// <summary>
        /// Register object, which is not created via main object creation pipeline.
        /// See 'CreateObject' method.
        /// This is not an initialized object, so object will be invalid, until the very first update happens.
        /// </summary>
        public void TryRegisterOrInitializeSceneObjectIdentity(GSSceneObjectIdentity sceneIdentity)
        {
            if (ObjectExists(sceneIdentity.SceneObjectId))
            {
#if NETWORK_DEBUG
                Debug.LogError(
                    $"Trying to register scene identity '{sceneIdentity.gameObject.name}' '{sceneIdentity.PrefabID}': but object with id '{sceneIdentity.SceneObjectId}' already registered.");
#endif
                UnityEngine.Object.Destroy(sceneIdentity.gameObject);
                return;
            }

            if (sceneIdentity.Identity.Initialized)
                throw new InvalidOperationException($"'{sceneIdentity.gameObject.name}' already initialized.");

            // Create state from scene object identity.
            // We don't need to know scene object prefab id, because it's already in scene, but prefab id can't be null.
            var newState = new NetObjectState(sceneIdentity.SceneObjectId, sceneIdentity.PrefabID, isSceneObject: true, Players.MasterPlayerUid,
                Players.Find);
            m_TemporaryUnregisteredObjects.Add(newState.UID, newState);
            var newStateUID = newState.UID;

            // Wait for view initialization.
            ScheduleOnViewInit(() =>
            {
                if (TryGetExistedOrPendingState(newStateUID, out var state))
                {
                    // Initialize immateriality on master client.
                    if (Players.LocalPlayerIsMaster)
                        View.InitializeObjectView(sceneIdentity.Identity, state.UID);

                    // Add view to be used in the future.
                    View.RegisterAsPending(newStateUID, sceneIdentity.Identity);
                }
                else
                {
                    UnityEngine.Object.Destroy(sceneIdentity.gameObject);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogError(
                        $"Unable to initialize '{sceneIdentity.gameObject.name}'. Object or pending state with Uid '{newStateUID}' not exist.");
#endif
                }
            });
        }

        /// <summary>
        /// Returns an object state that corresponds to <param name="instanceId"></param>.
        /// </summary>
        public override NetObjectState FindObject(NetObjectUID instanceId)
        {
            var existed = base.FindObject(instanceId);
            if (existed == null)
                m_TemporaryUnregisteredObjects.TryGetValue(instanceId, out existed);

            return existed;
        }

        /// <summary>
        /// Get object before in was actually created by the world replication.
        /// Note that this object will not be in valid state, until update happens.
        /// Use in only if it's highly necessary.
        /// </summary>
        public NetObjectIdentity FindObjectViewOrCreateMock(NetWorldPlayer owner, NetObjectUID instanceId, string prefabId, bool isSceneObject,
            Vector3 position, Quaternion rotation)
        {
            var identity = FindObjectView(instanceId);
            if (identity != null)
                return identity;

            var state = new NetObjectState(instanceId, prefabId, isSceneObject, owner.Uid, Players.Find);
            m_TemporaryUnregisteredObjects.Add(instanceId, state);
            return View.CreateImmediate(state, position, rotation);
        }

        /// <inheritdoc cref="FindObjectViewOrCreateMock"/>
        public T FindObjectViewOrCreateMock<T>(NetWorldPlayer owner, NetObjectUID instanceId, string prefabId, bool isSceneObject, Vector3 position,
            Quaternion rotation) where T : GSInstanceBehaviourBase
        {
            var networkObject = FindObjectViewOrCreateMock(owner, instanceId, prefabId, isSceneObject, position, rotation);
            return networkObject == null ? null : networkObject.GetComponent<T>();
        }

        public int GetHistoryBufferSize() =>
            HistoryBuffer.Size;

        /// <summary>
        /// World has to be notified, when game world is loaded.
        /// </summary>
        public void OnSceneLoaded()
        {
            // Check for player exists.
            var player = Players.LocalPlayer;
            if (player != null)
            {
                player.IsReady = true;
                PlayerBecomesReady(player.ToSnapshot());
            }

            // Create view.
            InitializeView();
        }
    }
}