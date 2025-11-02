using System;
using System.Collections.Generic;
using UnityEngine;
using Core.Networking;
using LROTech.Network.GameObjects;
using LROTech.Network.Sessions;
using LROTech.Network.StateManagement.Snapshots;
using UnityEngine.SceneManagement;

namespace LROTech.Network.StateManagement
{
    public sealed class NetWorldRuntime : NetWorld
    {
        public delegate void DataSendRequestDelegate(byte[] data, DeliveryMethod deliveryMethod);

        private const float HISTORY_BUFFER_MAX_LENGTH_IN_SECONDS = 10f; // Record last 'N' seconds.
        private const float MIN_UPDATE_INTERVAL_S = 0.1f; // 10 times per second.

        public event Action<NetWorldPlayerSnapshot> PlayerAdded = delegate { };
        public event Action<NetWorldPlayerSnapshot> PlayerRemoved = delegate { };

        public event Action<NetWorldPlayerSnapshot> PlayerBecomesReady = delegate { }; // Player loads game scene;
        public event DataSendRequestDelegate DataSendRequested = delegate { };

        public IReadOnlyCollection<byte[]> IncomingStateChangeRequests => _incomingStateChangeRequests;
        private readonly Queue<byte[]> _incomingStateChangeRequests = new();

        public IReadOnlyDictionary<NetObjectUid, NetObjectState> TemporaryUnregisteredObjects => _temporaryUnregisteredObjects;
        private readonly Dictionary<NetObjectUid, NetObjectState> _temporaryUnregisteredObjects = new();

        public readonly NetWorldHistoryBuffer HistoryBuffer = new();

        private readonly HashSet<ClientUid> _localPlayerRegisterQueue = new();
        private readonly HashSet<ClientUid> _playerUnregisterQueue = new();

        private float _lastUpdateTime;

        private readonly NetWriter _writer = new();
        private readonly NetReader _reader = new();

        public NetWorldRuntime(string worldName = "NetworkWorldRoot") : base(NetWorldId.Allocate(), worldName)
        {
            NetObjectUid.Reset();
            _lastUpdateTime = UnityEngine.Time.fixedUnscaledTime;
        }

        protected override void UpdateImplementation(float deltaTime)
        {
            var worldDelta = UnityEngine.Time.fixedUnscaledTime - _lastUpdateTime;
            if (worldDelta < MIN_UPDATE_INTERVAL_S)
                return;
            
            _lastUpdateTime = UnityEngine.Time.fixedUnscaledTime;
            UpdateWorldState(worldDelta);
        }

        #region Update

        protected override void OnUpdateWorldState(float worldDelta)
        {
            base.OnUpdateWorldState(worldDelta);

            var remotePlayersUpdateRequests = new Dictionary<ClientUid, NetWorldPlayerSnapshot>();
            var remoteObjectUpdateRequests = new Dictionary<NetObjectUid, NetObjectStateSnapshot>();
            var remoteWorldDataUpdateRequests = new Queue<byte[]>();
            var removedPlayers = new Queue<ClientUid>();

            TrySendWorldData();

            ProcessIncomingData();

            UpdateRemoteMasterWorldData();
            UpdatePlayerStates();
            UpdateObjectStateBasedOnRemovedPlayers();
            UpdateObjectStateBasedOnRemoteData();
            UpdateRpcRequestsWithoutAuthority();
            UpdateLocalObjectState();

            #region Internal

            void ProcessIncomingData()
            {
                while (_incomingStateChangeRequests.TryDequeue(out var stateChangeData))
                {
                    _reader.Replace(stateChangeData);
                    var worldChangeOpCode = (NetWorldRuntimeChangeOp)_reader.ReadByte();

                    switch (worldChangeOpCode)
                    {
                        // Other client wants to tell us about its player state.
                        case NetWorldRuntimeChangeOp.PlayerStateUpdate:
                        {
                            // Add player, if not exists.
                            // Deserialize other player.
                            // Adding player id duplicated logic.
                            var snapshot = new NetWorldPlayerSnapshot(_reader);

                            // TODO: As we may process multiple packets per update, we should be a bit more smart about it, and use the latest one.
                            if (!remotePlayersUpdateRequests.TryAdd(snapshot.Uid, snapshot))
                                Debug.LogWarning($"Unable to schedule update for remote player '{snapshot.Uid}': already scheduled.");

                            break;
                        }
                        case NetWorldRuntimeChangeOp.ObjectStateUpdate:
                        {
                            var snapshot = new NetObjectStateSnapshot(_reader);
                            if (!snapshot.ObjectUid.IsValid)
                            {
                                Debug.LogError($"Unable to schedule update for remote object '{snapshot.ObjectUid}' '{snapshot.PrefabId}', invalid Uid.");
                                break;
                            }

                            if (remoteObjectUpdateRequests.TryAdd(snapshot.ObjectUid, snapshot))
                                break;

                            var currentSnapshot = remoteObjectUpdateRequests[snapshot.ObjectUid];

                            var isSnapshotReliable = currentSnapshot.StateChangeType >= NetObjectStateChangeRequestType.ReliableUpdate;
                            var isRequestReliable = snapshot.StateChangeType >= NetObjectStateChangeRequestType.ReliableUpdate;

                            if (isSnapshotReliable && isRequestReliable)
                                Debug.LogError($"Snapshot for object '{snapshot.PrefabId}' must be merged in order to not loose reliable data.");
                            
                            // Overwrite snapshot with the new one.
                            // TODO: BOTH STATES COULD BE RELIABLE! We could skip reliable data here!!
                            if (currentSnapshot.StateChangeType < snapshot.StateChangeType)
                                remoteObjectUpdateRequests[snapshot.ObjectUid] = snapshot;

                            break;
                        }

                        case NetWorldRuntimeChangeOp.WorldCommunication:
                        {
                            remoteWorldDataUpdateRequests.Enqueue(stateChangeData);
                            break;
                        }

                        case NetWorldRuntimeChangeOp.Rpc:
                        {
                            var state = new RpcWithoutAuthorityState(_reader);
                            if (!state.IsValid)
                                throw new ArgumentException();

                            var objectIdentity = FindObjectView(state.ObjectUid);

                            // We need to handle rpc receive for objects, which are doesn't exist.
                            // It is probably already destroyed.
                            // No exceptions here, just warning.
                            if (objectIdentity == null)
                            {
#if NETWORK_DEBUG
                                var rpcHandlerMethod = NetObjectRpcUtils.TryFindMethodInfoByHashSlow(state.RpcState.Hash);
                                var rpcHandlerMethodName = rpcHandlerMethod == null
                                    ? "UNKNOWN"
                                    : $"{rpcHandlerMethod.DeclaringType!.FullName}.{rpcHandlerMethod.Name}";

                                Debug.LogError($"Trying to receive rpc with code '{state.RpcState.Hash}' '{rpcHandlerMethodName}' for object '{state.ObjectUid}' that doesnt exists, maybe it is already destroyed.");
#endif
                                break;
                            }

                            switch (state.Type)
                            {
                                case NoAuthorityRpcType.ToOwner:
                                {
                                    // Check if we are this object's owner.
                                    if (objectIdentity.IsLocal)
                                        objectIdentity.ReceiveRpcLocally(state);

                                    break;
                                }
                                case NoAuthorityRpcType.ToMaster:
                                {
                                    if (objectIdentity.IsOwnedByMasterClient)
                                        objectIdentity.ReceiveRpcOnMasterClient(state);

                                    break;
                                }
                                case NoAuthorityRpcType.ToAll:
                                {
                                    objectIdentity.ReceiveRpc(state);
                                    break;
                                }
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }

                            break;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            void TrySendWorldData()
            {
                // Only master can send this data.
                if (!Players.LocalPlayerIsMaster)
                    return;

                _writer.SeekZero();
                _writer.WriteByte((byte)NetWorldRuntimeChangeOp.WorldCommunication);
                _writer.WriteSingle(Time);
                Properties.Serialize(_writer);
                DataSendRequested(data: _writer.ToArray(), DeliveryMethod.Unreliable);
            }

            void UpdateRemoteMasterWorldData()
            {
                // Only non-master clients could read this data.
                if (Players.LocalPlayerIsMaster)
                    return;

                while (remoteWorldDataUpdateRequests.Count > 0)
                {
                    _reader.Replace(buffer: remoteWorldDataUpdateRequests.Dequeue());
                    _reader.ReadByte(); // Offset - see ProcessInternalData var worldChangeOpCode = (NetWorldRuntimeChangeOp)_reader.ReadByte();

                    var newTime = _reader.ReadSingle();
                    if (newTime >= Time)
                    {
                        Time = newTime;
                        Properties.Deserialize(_reader);
                    }
                }
            }

            void UpdatePlayerStates()
            {
                // Add local.
                {
                    foreach (var clientUid in _localPlayerRegisterQueue)
                    {
                        // Add player and notify.
                        var player = Players.RegisterImmediate(clientUid, isLocal: true);
                        PlayerAdded(player.ToSnapshot());
                    }

                    _localPlayerRegisterQueue.Clear();
                }

                // Remove all.
                {
                    foreach (var clientUid in _playerUnregisterQueue)
                    {
                        var player = Players.Find(clientUid);
                        var playerSnapshot = player?.ToSnapshot() ?? default;
                        if (Players.UnregisterImmediate(clientUid))
                            removedPlayers.Enqueue(clientUid);

                        if (playerSnapshot.Uid.IsValid)
                            PlayerRemoved(playerSnapshot);
                    }

                    _playerUnregisterQueue.Clear();
                }

                // Update remotes.
                {
                    foreach (var clientUid in new List<ClientUid>(remotePlayersUpdateRequests.Keys))
                    {
                        var snapshot = remotePlayersUpdateRequests[clientUid];
                        var player = Players.Find(clientUid);

                        // Player not exists.
                        if (player == null)
                        {
                            // Create new.
                            player = Players.RegisterImmediate(snapshot.Uid, isLocal: false);

                            // Notify player registered.
                            PlayerAdded(player.ToSnapshot());

                            foreach (var objectUid in new List<NetObjectUid>(ObjectStates.Keys))
                            {
                                var state = ObjectStates[objectUid];
                                if (state.IsOwnedByLocalPlayer)
                                    state.ScheduleUpdate(NetObjectStateChangeRequestType.InitializationUpdate);
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
                    {
                        _writer.SeekZero();
                        _writer.WriteByte((byte)NetWorldRuntimeChangeOp.PlayerStateUpdate);
                        localPlayer.ToSnapshot().Serialize(_writer);
                        DataSendRequested(data: _writer.ToArray(), DeliveryMethod.Reliable);
                    }
                }
            }

            // Update states based on removed player, if player leave, remove object.
            void UpdateObjectStateBasedOnRemovedPlayers()
            {
                while (removedPlayers.TryDequeue(out var removePlayerUid))
                {
                    foreach (var objectUid in new List<NetObjectUid>(ObjectStates.Keys))
                    {
                        var state = ObjectStates[objectUid];

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
                foreach (var uid in new List<NetObjectUid>(remoteObjectUpdateRequests.Keys))
                {
                    var snapshot = remoteObjectUpdateRequests[uid];

                    if (!snapshot.ObjectUid.IsValid)
                        throw new InvalidOperationException($"Invalid Uid: '{snapshot.ObjectUid}'  Prefab: '{snapshot.PrefabId}'");

                    // Check if owner exists.
                    if (!Players.Contains(snapshot.OwnerUid))
                    {
                        remoteObjectUpdateRequests.Remove(uid);
                        continue;
                    }

                    // Object exists, just update.
                    if (ObjectStates.TryGetValue(snapshot.ObjectUid, out var objState))
                    {
                        objState.ChangeObjectRemotely(snapshot);
                        remoteObjectUpdateRequests.Remove(uid);

                        if (objState.StateChangeType == NetObjectStateChangeRequestType.RemoveUpdateHard)
                            RemoveObject(snapshot.ObjectUid);

                        continue;
                    }

                    // Object doesn't exists.
                    if (snapshot.StateChangeType is NetObjectStateChangeRequestType.RemoveUpdateHard or NetObjectStateChangeRequestType.RemoveUpdateSoft)
                    {
                        Debug.LogError($"Remote update snapshot form object with Uid: '{snapshot.ObjectUid}:{snapshot.PrefabId}' has state '{snapshot.StateChangeType}', but object doesn't exists.");

                        _temporaryUnregisteredObjects.Remove(snapshot.ObjectUid);
                        remoteObjectUpdateRequests.Remove(uid);
                        continue;
                    }

                    // Add new or get pending.
                    if (snapshot.StateChangeType != NetObjectStateChangeRequestType.InitializationUpdate)
                    {
                        _temporaryUnregisteredObjects.Remove(snapshot.ObjectUid);
                        continue;
                    }

                    if (_temporaryUnregisteredObjects.TryGetValue(snapshot.ObjectUid, out var newState))
                    {
                        if (newState.IsOwnedBy(snapshot.OwnerUid))
                        {
                            newState.ChangeObjectRemotely(snapshot);
                            _temporaryUnregisteredObjects.Remove(snapshot.ObjectUid);
                        }
                        else
                            Debug.LogError($"Unable to restore object '{newState}' form '{snapshot}'.");
                    }
                    else
                        newState = new NetObjectState(IsObjectValid, Players.Find, snapshot);

                    TryAddObject(newState);
                    remoteObjectUpdateRequests.Remove(uid);
                }
            }

            // Update RPC requests - calling rpc without authority.
            void UpdateRpcRequestsWithoutAuthority()
            {
                foreach (var uid in new List<NetObjectUid>(ObjectStates.Keys))
                {
                    var state = ObjectStates[uid];
                    foreach (var request in state.RpcWithoutAuthorityRequests)
                    {
                        _writer.SeekZero();
                        _writer.WriteByte((byte)NetWorldRuntimeChangeOp.Rpc);
                        request.State.Serialize(_writer);
                        DataSendRequested(data: _writer.ToArray(), request.DeliveryMethod);
                    }
                }
            }

            // Update state based on local object changes.
            void UpdateLocalObjectState()
            {
                // Register new local object, that was just created.
                foreach (var uid in new List<NetObjectUid>(_temporaryUnregisteredObjects.Keys))
                {
                    var state = _temporaryUnregisteredObjects[uid];

                    // Skip remote objects.
                    if (!state.IsOwnedByLocalPlayer)
                        continue;

                    if (ObjectExists(state.Uid))
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Debug.LogError($"Object with Uid '{state.Uid}' already exists");
#endif
                    }
                    else
                    {
                        state.ScheduleUpdate(NetObjectStateChangeRequestType.InitializationUpdate);
                        TryAddObject(state);
                    }

                    _temporaryUnregisteredObjects.Remove(uid);
                }

                // Update and send local objects.
                foreach (var uid in new List<NetObjectUid>(ObjectStates.Keys))
                {
                    var state = ObjectStates[uid];

                    // Skip remote objects.
                    if (!state.IsOwnedByLocalPlayer)
                        continue;

                    var deliveryMethod = DeliveryMethod.Unreliable;
                    if (state.StateChangeType >= NetObjectStateChangeRequestType.ReliableUpdate)
                        deliveryMethod = DeliveryMethod.Reliable;

                    state.PrepareForSerialization();

                    _writer.SeekZero();
                    _writer.WriteByte((byte)NetWorldRuntimeChangeOp.ObjectStateUpdate);
                    state.ToSnapshot().Serialize(_writer);
                    DataSendRequested(data: _writer.ToArray(), deliveryMethod);

                    switch (state.StateChangeType)
                    {
                        case NetObjectStateChangeRequestType.RemoveUpdateHard:
                            RemoveObject(uid);
                            break;

                        case NetObjectStateChangeRequestType.RemoveUpdateSoft:
                            state.ScheduleUpdate(NetObjectStateChangeRequestType.RemoveUpdateHard);
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

            _localPlayerRegisterQueue.Clear();
            _playerUnregisterQueue.Clear();
            HistoryBuffer.Clear();

            PlayerAdded = null;
        }

        /// <summary>
        /// Create another world that could evaluate and re-player this world's history buffer.
        /// </summary>
        /// <returns></returns>
        public NetWorldEvaluator Replicate()
        {
            EnsureValid();
            return new NetWorldEvaluator(runtime: this);
        }

        protected override void RemoveObject(NetObjectUid stateUid)
        {
            base.RemoveObject(stateUid);
            _temporaryUnregisteredObjects.Remove(stateUid);
        }

        internal void RegisterLocalPlayer(ClientUid clientUid) =>
            _localPlayerRegisterQueue.Add(clientUid);

        internal void OnDisconnect(ClientUid clientUid)
        {
            _localPlayerRegisterQueue.Remove(clientUid);
            _playerUnregisterQueue.Add(clientUid);
        }

        internal void ScheduleRemoteUpdate(byte[] data) =>
            _incomingStateChangeRequests.Enqueue(data);

        private bool TryGetExistedOrPendingState(NetObjectUid instanceId, out NetObjectState state) =>
            TryGetObject(instanceId, out state) || _temporaryUnregisteredObjects.TryGetValue(instanceId, out state);

        /// <summary>
        /// Create object with position and rotation input.
        /// Should be called on local client to create object across network.
        /// </summary>
        public NetObjectIdentity CreateObject(string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation)
        {
            EnsureValid();

            // TODO: isSceneObject should be renamed to something else, like destroy with owner (when owner disconnected).
            var owner = Players.LocalPlayer;
            if (owner == null)
                throw new InvalidOperationException();

            var state = new NetObjectState(IsObjectValid, Players.Find, NetObjectUid.FromClientUid(owner.Uid), prefabId, isSceneObject, owner.Uid);
            _temporaryUnregisteredObjects.Add(state.Uid, state);
            return View.CreateImmediate(state, position, rotation);
        }

        /// <summary>
        /// Register object, which is not created via main object creation pipeline.
        /// See 'CreateObject' method.
        /// This is not an initialized object, so object will be invalid, until the very first update happens.
        /// </summary>
        private void TryRegisterOrInitializeSceneObjectIdentity(NetObjectSceneIdentity sceneIdentity)
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
            var newState = new NetObjectState(IsObjectValid, Players.Find, sceneIdentity.SceneObjectId, sceneIdentity.PrefabID, isSceneObject: true, Players.MasterPlayerUid);
            _temporaryUnregisteredObjects.Add(newState.Uid, newState);

            // Wait for view initialization.
            if (TryGetExistedOrPendingState(newState.Uid, out var state))
            {
                // Initialize immateriality on master client.
                if (Players.LocalPlayerIsMaster)
                    View.InitializeObjectView(sceneIdentity.Identity, state.Uid);

                // Add view to be used in the future.
                View.RegisterAsPending(newState.Uid, sceneIdentity.Identity);
            }
            else
            {
                UnityEngine.Object.Destroy(sceneIdentity.gameObject);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError($"Unable to initialize '{sceneIdentity.gameObject.name}'. Object or pending state with Uid '{newState.Uid}' not exist.");
#endif
            }
        }

        /// <summary>
        /// Returns an object state that corresponds to <param name="instanceId"></param>.
        /// </summary>
        public override NetObjectState FindObject(NetObjectUid instanceId)
        {
            var existed = base.FindObject(instanceId);
            if (existed == null)
                _temporaryUnregisteredObjects.TryGetValue(instanceId, out existed);

            return existed;
        }

        protected override bool IsObjectValid(NetObjectUid instanceId) =>
            base.IsObjectValid(instanceId) || _temporaryUnregisteredObjects.ContainsKey(instanceId);

        /// <summary>
        /// Get object before in was actually created by the world replication.
        /// Note that this object will not be in valid state, until update happens.
        /// Use in only if it's highly necessary.
        /// </summary>
        public NetObjectIdentity FindObjectViewOrCreateMock(NetWorldPlayer owner, NetObjectUid instanceId, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation)
        {
            var identity = FindObjectView(instanceId);
            if (identity != null)
                return identity;

            var state = new NetObjectState(IsObjectValid, Players.Find, instanceId, prefabId, isSceneObject, owner.Uid);
            _temporaryUnregisteredObjects.Add(instanceId, state);
            return View.CreateImmediate(state, position, rotation);
        }

        /// <inheritdoc cref="FindObjectViewOrCreateMock"/>
        public T FindObjectViewOrCreateMock<T>(NetWorldPlayer owner, NetObjectUid instanceId, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation) where T : NetObjectBehaviourBase
        {
            var networkObject = FindObjectViewOrCreateMock(owner, instanceId, prefabId, isSceneObject, position, rotation);
            return networkObject == null ? null : networkObject.GetComponent<T>();
        }

        public int GetHistoryBufferSize()
        {
            EnsureValid();
            return HistoryBuffer.Size;
        }

        /// <summary>
        /// World has to be notified, when game world is loaded.
        /// </summary>
        public void OnSceneLoaded()
        {
            EnsureValid();

            // Check for player exists.
            var player = Players.LocalPlayer;
            if (player != null)
            {
                player.IsReady = true;
                PlayerBecomesReady(player.ToSnapshot());
            }

            // Create view.
            InitializeView();

            // Init scene objects.
            for (var i = 0; i < SceneManager.loadedSceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                foreach (var rootObject in scene.GetRootGameObjects())
                {
                    var sceneIdentities = rootObject.GetComponentsInChildren<NetObjectSceneIdentity>(includeInactive: false);
                    foreach (var sceneIdentity in sceneIdentities)
                    {
                        if (!sceneIdentity.Identity.Initialized)
                            TryRegisterOrInitializeSceneObjectIdentity(sceneIdentity);
                    }
                }
            }
        }
    }
}