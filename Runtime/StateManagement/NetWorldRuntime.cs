using System;
using System.Collections.Generic;
using UnityEngine;
using Core.Networking;
using LROTech.Network.GameObjects;
using LROTech.Network.StateManagement.Snapshots;
using UnityEngine.SceneManagement;

namespace LROTech.Network.StateManagement
{
    public sealed class NetWorldRuntime : NetWorld
    {
        public delegate void DataSendRequestDelegate(byte[] data, DeliveryMethod deliveryMethod);

        private const float HISTORY_BUFFER_MAX_LENGTH_IN_SECONDS = 10f; // Record last 'N' seconds.
        private const float MIN_UPDATE_INTERVAL_S = 0.1f; // 10 times per second.
        private const float AUTO_REMOVE_REMOTE_OBJECT_TIME_S = 6f; // Remove remote objects, if now data was received for 'N' seconds.
        private const float OWNERSHIP_DISTRIBUTION_UPDATE_RATE = 5f;
        private const float OWNERSHIP_DISTRIBUTION_REQUEST_TIMOUT = 2f;

        public event Action<NetWorldPlayerSnapshot> PlayerAdded = delegate { };
        public event Action<NetWorldPlayerSnapshot> PlayerRemoved = delegate { };

        public event Action<NetWorldPlayerSnapshot> PlayerBecomesReady = delegate { }; // Player loads game scene;
        public event DataSendRequestDelegate DataSendRequested = delegate { };

        public IReadOnlyCollection<byte[]> IncomingStateChangeRequests => _incomingStateChangeRequests;
        private readonly Queue<byte[]> _incomingStateChangeRequests = new();

        public IReadOnlyDictionary<NetObjectUid, NetObjectSceneIdentity> UnregisteredSceneObjects => _unregisteredSceneObjects;
        private readonly Dictionary<NetObjectUid, NetObjectSceneIdentity> _unregisteredSceneObjects = new();

        private readonly Dictionary<NetObjectUid, float> _lastObjectValidationTimeLookup = new();

        public readonly NetWorldHistoryBuffer HistoryBuffer = new();
        private readonly Func<ClientUid> _sessionOwnerProvider;
        private readonly NetWriter _writer = new();
        private readonly NetReader _reader = new();
        private readonly HashSet<NetObjectUid> _objectSendBlacklist = new();

        private float _lastUpdateTime;
        private float _localUpdateTime;
        private float _nextOwnershipDistributionUpdateTime;

        public NetWorldRuntime(Func<ClientUid> sessionOwnerProvider, string worldName = "NetworkWorldRoot") : base(NetWorldId.Allocate(), worldName)
        {
            _sessionOwnerProvider = sessionOwnerProvider ?? throw new ArgumentNullException(nameof(sessionOwnerProvider));
            NetObjectUid.Reset();
        }

        protected override ClientUid GetSessionOwnerUid() =>
            _sessionOwnerProvider();

        protected override void UpdateImplementation(float deltaTime)
        {
            _localUpdateTime += deltaTime;

            var worldDelta = _localUpdateTime - _lastUpdateTime;
            if (worldDelta < MIN_UPDATE_INTERVAL_S)
                return;

            _lastUpdateTime = _localUpdateTime;
            UpdateWorldState(worldDelta);
        }

        #region Update

        protected override void OnUpdateWorldState(float worldDelta)
        {
            base.OnUpdateWorldState(worldDelta);

            // TODO: Avoid allocations.
            var remotePlayersUpdateRequests = new Dictionary<ClientUid, NetWorldPlayerSnapshot>();
            var remoteObjectUpdateRequests = new Dictionary<NetObjectUid, NetObjectStateSnapshot>();
            var remoteWorldDataUpdateRequests = new Queue<byte[]>();
            var remoteObjectOwnershipValidationsUpdateRequests = new Queue<byte[]>();

            TrySendWorldData();

            ProcessIncomingData();

            UpdateRemoteMasterWorldData();
            UpdateRemotePlayerStates();
            UpdateRemoteObjectsStates();
            UpdateRpcRequestsWithoutAuthority();
            UpdateLocalObjectState();
            UpdateObjectOwnershipDistribution();

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
                            var snapshot = new NetWorldPlayerSnapshot(_reader);
                            if (!snapshot.Uid.IsValid)
                            {
                                Debug.LogError($"Unable to schedule update for remote player '{snapshot.Uid}', invalid Uid.");
                                break;
                            }

                            if (!remotePlayersUpdateRequests.TryAdd(snapshot.Uid, snapshot))
                            {
                                var currentsSnapshot = remotePlayersUpdateRequests[snapshot.Uid];
                                if (currentsSnapshot.WorldTime < snapshot.WorldTime)
                                    remotePlayersUpdateRequests[snapshot.Uid] = snapshot;
                            }

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

                            // If snapshot is already registered, we should check if it's older that the one we're trying to register.
                            var currentSnapshot = remoteObjectUpdateRequests[snapshot.ObjectUid];

                            // Packets could arrive in a wrong order.
                            // NOTE: 'StateChangeType' is a priority here - we can't replace the data with lower 'StateChangeType', event if the new data is newer.
                            // So, the data will be replaced if the following rule applies.
                            // 1. 'StateChangeType' of the new data is bigger that 'StateChangeType' of the current one.
                            // OR
                            // 2. 'StateChangeType' is the same, and incoming data is newer.
                            var shouldBeUpdated =
                                currentSnapshot.StateChangeType < snapshot.StateChangeType ||
                                currentSnapshot.StateChangeType == snapshot.StateChangeType &&
                                currentSnapshot.WorldTime < snapshot.WorldTime;

                            // Overwrite snapshot with the new one.
                            if (shouldBeUpdated)
                                remoteObjectUpdateRequests[snapshot.ObjectUid] = snapshot;

                            break;
                        }

                        case NetWorldRuntimeChangeOp.WorldCommunication:
                        {
                            remoteWorldDataUpdateRequests.Enqueue(stateChangeData);
                            break;
                        }

                        case NetWorldRuntimeChangeOp.WorldObjectOwnerValidationRequest:
                        {
                            remoteObjectOwnershipValidationsUpdateRequests.Enqueue(stateChangeData);
                            break;
                        }

                        case NetWorldRuntimeChangeOp.Rpc:
                        {
                            var state = new RpcWithoutAuthorityState(_reader);
                            if (!state.IsValid)
                                throw new ArgumentException();

                            var objectIdentity = GetObjectView(state.ObjectUid);

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
                if (Players.LocalPlayer == null || !Players.LocalPlayer.IsMaster())
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
                if (Players.LocalPlayer != null && Players.LocalPlayer.IsMaster())
                    return;

                // We need to peek latest data here - the 'maxTime' value to filter out irrelevant data.
                var maxTime = float.MinValue;
                while (remoteWorldDataUpdateRequests.TryDequeue(out var data))
                {
                    _reader.Replace(buffer: data);
                    _reader.ReadByte(); // Offset - see ProcessInternalData var worldChangeOpCode = (NetWorldRuntimeChangeOp)_reader.ReadByte();

                    var newTime = _reader.ReadSingle();
                    if (newTime < maxTime)
                        continue;

                    maxTime = newTime;
                    Time = newTime + 0.1f; // TODO: we have to take in account round trip time the relay here, to estimate more accurate.
                    Properties.Deserialize(_reader);
                }
            }

            void UpdateRemotePlayerStates()
            {
                // Update remotes.
                {
                    foreach (var clientUid in new List<ClientUid>(remotePlayersUpdateRequests.Keys))
                    {
                        var snapshot = remotePlayersUpdateRequests[clientUid];
                        var player = Players.Get(clientUid);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        if (player != null && player.IsLocal())
                            throw new InvalidOperationException();
#endif

                        // Player doesnt exist.
                        if (player == null)
                        {
                            // Create new.
                            player = Players.Register(snapshot.Uid, isLocal: false);

                            // Notify player registered.
                            PlayerAdded(player.ToSnapshot());
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
                    if (localPlayer != null)
                    {
                        _writer.SeekZero();
                        _writer.WriteByte((byte)NetWorldRuntimeChangeOp.PlayerStateUpdate);
                        localPlayer.PrepareForSerialization(Time);
                        localPlayer.ToSnapshot().Serialize(_writer);
                        DataSendRequested(data: _writer.ToArray(), DeliveryMethod.Reliable);
                    }
                }
            }

            // Update state based on remote incoming data.
            void UpdateRemoteObjectsStates()
            {
                // Process incoming update requests.
                foreach (var uid in new List<NetObjectUid>(remoteObjectUpdateRequests.Keys))
                {
                    var snapshot = remoteObjectUpdateRequests[uid];

                    if (!snapshot.ObjectUid.IsValid)
                        throw new InvalidOperationException($"Invalid Uid: '{snapshot.ObjectUid}'  Prefab: '{snapshot.PrefabId}'");

                    // Check if owner exists.
                    // NOTE: it's equally "unsafe" to either drop and apply this packet. However, i think it's better to drop it.
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

                        if (objState.StateChangeType == NetObjectStateChangeRequestType.ReliableRemoveUpdateHard)
                            RemoveObject(snapshot.ObjectUid);

                        continue;
                    }

                    // Object doesn't exist - check the state change type (should be at least unreliable update).
                    if (snapshot.StateChangeType < NetObjectStateChangeRequestType.UnreliableUpdate)
                        continue;

                    // Object was removed remotely - no need to proceed, just clear all the queues.
                    if (snapshot.StateChangeType is NetObjectStateChangeRequestType.ReliableRemoveUpdateHard or NetObjectStateChangeRequestType.ReliableRemoveUpdateSoft)
                    {
                        Debug.LogError($"Remote update snapshot form object with Uid: '{snapshot.ObjectUid}:{snapshot.PrefabId}' has state '{snapshot.StateChangeType}', but object doesn't exists.");

                        _unregisteredSceneObjects.Remove(snapshot.ObjectUid);

                        remoteObjectUpdateRequests.Remove(uid);
                        continue;
                    }

                    // Create new state.
                    var newState = new NetObjectState(IsObjectValid, Players.Get, snapshot);
                    AddObject(newState);

                    if (_unregisteredSceneObjects.TryGetValue(snapshot.ObjectUid, out var unregisteredSceneObject))
                    {
                        View.RegisterExistedObjectView(newState, unregisteredSceneObject.Identity);
                        _unregisteredSceneObjects.Remove(snapshot.ObjectUid);
                    }

                    remoteObjectUpdateRequests.Remove(uid);
                }

                // Validate objects lifetime.
                foreach (var uid in new List<NetObjectUid>(ObjectStates.Keys))
                {
                    var state = ObjectStates[uid];

                    // Skip local objects.
                    if (state.IsOwnedByLocalPlayer)
                        continue;

                    var timeDelta = Time - state.WorldTime;
                    if (timeDelta < AUTO_REMOVE_REMOTE_OBJECT_TIME_S)
                        continue;

                    remoteObjectUpdateRequests.Remove(uid);
                    RemoveObject(uid);
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
                // Process unregistered scene objects.
                if (Players.LocalPlayer != null && Players.LocalPlayer.IsMaster() && _unregisteredSceneObjects.Count > 0)
                {
                    foreach (var uid in new List<NetObjectUid>(_unregisteredSceneObjects.Keys))
                    {
                        var sceneIdentity = _unregisteredSceneObjects[uid];

                        var newState = new NetObjectState(IsObjectValid, Players.Get, sceneIdentity.SceneObjectId, sceneIdentity.PrefabID, isSceneObject: true, owner: Players.LocalPlayer.Uid);
                        AddObject(newState);
                        newState.ScheduleUpdate(NetObjectStateChangeRequestType.ReliableUpdate);

                        View.RegisterExistedObjectView(newState, sceneIdentity.Identity);
                    }

                    _unregisteredSceneObjects.Clear();
                }

                // Update and send local objects.
                foreach (var uid in new List<NetObjectUid>(ObjectStates.Keys))
                {
                    var state = ObjectStates[uid];

                    // Skip remote objects.
                    if (!state.IsOwnedByLocalPlayer)
                        continue;

                    // Check if state replication is not allowed for the state.
                    if (_objectSendBlacklist.Contains(uid))
                        continue;

                    var deliveryMethod = DeliveryMethod.Unreliable;
                    if (state.StateChangeType >= NetObjectStateChangeRequestType.ReliableUpdate)
                        deliveryMethod = DeliveryMethod.Reliable;

                    state.PrepareForSerialization(Time);

                    _writer.SeekZero();
                    _writer.WriteByte((byte)NetWorldRuntimeChangeOp.ObjectStateUpdate);
                    state.ToSnapshot().Serialize(_writer);
                    DataSendRequested(data: _writer.ToArray(), deliveryMethod);

                    switch (state.StateChangeType)
                    {
                        case NetObjectStateChangeRequestType.ReliableRemoveUpdateHard:
                            RemoveObject(uid);
                            break;

                        case NetObjectStateChangeRequestType.ReliableRemoveUpdateSoft:
                            var view = View.GetObjectView(uid);
                            if (view != null)
                                view.OnSoftDestroyRequestedLocallyInternal();

                            state.ScheduleUpdate(NetObjectStateChangeRequestType.ReliableRemoveUpdateHard);
                            break;
                    }
                }
            }

            // Logic that handles automatic ownership distribution.
            void UpdateObjectOwnershipDistribution()
            {
                // Cleanup validation lookup times.
                if (_lastObjectValidationTimeLookup.Count > 0)
                {
                    foreach (var uid in new List<NetObjectUid>(_lastObjectValidationTimeLookup.Keys))
                    {
                        if (Time >= _lastObjectValidationTimeLookup[uid])
                            _lastObjectValidationTimeLookup.Remove(uid);
                    }
                }

                if (Players.LocalPlayer == null)
                    return;

                if (Time < _nextOwnershipDistributionUpdateTime)
                    return;

                _nextOwnershipDistributionUpdateTime = Time + OWNERSHIP_DISTRIBUTION_UPDATE_RATE;

                // Remote requests.
                while (remoteObjectOwnershipValidationsUpdateRequests.TryDequeue(out var data))
                {
                    _reader.Replace(buffer: data);
                    _reader.ReadByte(); // Offset - see ProcessInternalData var worldChangeOpCode = (NetWorldRuntimeChangeOp)_reader.ReadByte();

                    var isResponse = _reader.ReadBoolean();
                    if (isResponse)
                    {
                        var newOwnerId = _reader.ReadClientUid();
                        var objectUid = _reader.ReadNetObjectUid();

                        var player = Players.Get(newOwnerId);
                        if (player != null && player.IsLocal())
                        {
                            var targetObject = GetObject(objectUid);
                            targetObject?.ChangedOwner(newOwnerId);
                        }
                    }
                    else
                    {
                        var currentOwner = _reader.ReadClientUid();
                        var newPotentialOwner = _reader.ReadClientUid();
                        var objectUid = _reader.ReadNetObjectUid();

                        if (currentOwner == newPotentialOwner)
                            continue;

                        var targetObject = GetObject(objectUid);
                        if (targetObject == null || !targetObject.IsOwnedBy(currentOwner) || !Players.Contains(newPotentialOwner))
                            continue;

                        if (targetObject.IsTagObjectForCurrentOwner)
                            continue;

                        // Object could be in dictionary only if it was recently requested to validate ownership.
                        // We not allow multiple sequential requests - 2 seconds timeout.
                        if (!_lastObjectValidationTimeLookup.TryAdd(objectUid, Time + OWNERSHIP_DISTRIBUTION_REQUEST_TIMOUT))
                            continue;

                        _writer.SeekZero();
                        _writer.WriteByte((byte)NetWorldRuntimeChangeOp.WorldObjectOwnerValidationRequest);
                        _writer.WriteBoolean(true); // is response.
                        _writer.WriteClientUid(newPotentialOwner);
                        _writer.WriteNetObjectUid(objectUid);
                        DataSendRequested(data: _writer.ToArray(), DeliveryMethod.Unreliable);
                    }
                }

                // Local objects.
                foreach (var uid in new List<NetObjectUid>(ObjectStates.Keys))
                {
                    var state = ObjectStates[uid];

                    // Skip local objects.
                    if (state.IsOwnedByLocalPlayer)
                        continue;

                    var potentialOwner = GetPotentialOwner(state.Uid);
                    if (potentialOwner != Players.LocalPlayer.Uid)
                        continue;

                    if (state.IsTagObjectForCurrentOwner)
                        continue;

                    _writer.SeekZero();
                    _writer.WriteByte((byte)NetWorldRuntimeChangeOp.WorldObjectOwnerValidationRequest);
                    _writer.WriteBoolean(false); // is response.
                    _writer.WriteClientUid(state.OwnerUid);
                    _writer.WriteClientUid(potentialOwner);
                    _writer.WriteNetObjectUid(uid);
                    DataSendRequested(data: _writer.ToArray(), DeliveryMethod.Unreliable);
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

        private ClientUid GetPotentialOwner(NetObjectUid objectNetId)
        {
            if (!objectNetId.IsValid)
                return ClientUid.Invalid;

            var bestClient = ClientUid.Invalid;
            var highestHash = int.MinValue;

            foreach (var player in Players.All)
            {
                // Combine object ID and client ID, then hash
                var hash = HashCombine(objectNetId, player.Uid);
                if (hash <= highestHash)
                    continue;

                highestHash = hash;
                bestClient = player.Uid;
            }

            return bestClient;

            static int HashCombine(NetObjectUid a, ClientUid b)
            {
                unchecked
                {
                    var h1 = (uint)a;
                    var h2 = (uint)b;

                    h1 ^= h2;
                    h1 *= 0x5bd1e995;
                    h1 ^= h1 >> 15;
                    h1 *= 0x5bd1e995;
                    h1 ^= h1 >> 15;

                    return (int)h1;
                }
            }
        }

        /// <summary>
        /// Destroy all network objects and cleanup scene.
        /// </summary>
        public override void Dispose()
        {
            EnsureValid();

            _incomingStateChangeRequests.Clear();
            _unregisteredSceneObjects.Clear();

            HistoryBuffer.Clear();

            PlayerAdded = null;

            // Base dispose call.
            base.Dispose();
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

        internal override void RemoveObject(NetObjectUid uid)
        {
            base.RemoveObject(uid);
            _unregisteredSceneObjects.Remove(uid);
        }

        internal void RegisterLocalPlayer(ClientUid uid)
        {
            // Add player and notify.
            var player = Players.Register(uid, isLocal: true);
            PlayerAdded(player.ToSnapshot());
        }

        internal void OnDisconnect(ClientUid uid)
        {
            if (!Players.Unregister(uid, out var removePlayerSnapshot))
                return;

            PlayerRemoved(removePlayerSnapshot);

            foreach (var objectUid in new List<NetObjectUid>(ObjectStates.Keys))
            {
                var state = ObjectStates[objectUid];

                // Object owned by disconnected player.
                if (state.IsOwnedBy(uid))
                {
                    // Change owner, if object is scene object.
                    if (state.IsSceneObject)
                    {
                        // Is local client becomes master client, set object's owner and sync it.
                        if (Players.LocalPlayer != null && Players.LocalPlayer.IsMaster())
                            state.ChangedOwner(Players.LocalPlayer.Uid);
                        else
                            state.InvalidateOwner();

                        if (View != null && View.CurrentObjectViews.TryGetValue(objectUid, out var view))
                            view.OnDisconnected(uid);
                    }
                    // Destroy object.
                    else
                        RemoveObject(objectUid);
                }
                // Notify view.
                else
                {
                    if (View != null && View.CurrentObjectViews.TryGetValue(objectUid, out var view))
                        view.OnDisconnected(uid);
                }
            }
        }

        internal void ScheduleRemoteUpdate(byte[] data) =>
            _incomingStateChangeRequests.Enqueue(data);

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

            var newState = new NetObjectState(IsObjectValid, Players.Get, NetObjectUid.FromClientUid(owner.Uid), prefabId, isSceneObject, owner.Uid);
            AddObject(newState);
            newState.ScheduleUpdate(NetObjectStateChangeRequestType.ReliableUpdate);

            return View.CreateObjectViewImmediate(newState, position, rotation);
        }

        /// <summary>
        /// Register object, which is not created via main object creation pipeline.
        /// See 'CreateObject' method.
        /// This is not an initialized object, so object will be invalid, until the very first update happens.
        /// </summary>
        private void TryRegisterOrInitializeSceneObjectIdentity(NetObjectSceneIdentity sceneIdentity)
        {
            if (sceneIdentity.Identity.Initialized)
                throw new InvalidOperationException($"'{sceneIdentity.gameObject.name}' already initialized.");

            if (ObjectExists(sceneIdentity.SceneObjectId))
            {
#if NETWORK_DEBUG
                Debug.LogWarning($"Trying to register scene identity '{sceneIdentity.gameObject.name}' '{sceneIdentity.PrefabID}': but object with id '{sceneIdentity.SceneObjectId}' already registered.");
#endif
                UnityEngine.Object.Destroy(sceneIdentity.gameObject);
                return;
            }

            // At this point we don't know who's going to be the owner, client it will be invalid just for now.
            _unregisteredSceneObjects.Add(sceneIdentity.SceneObjectId, sceneIdentity);
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

        internal bool IsStateReplicationEnabled(NetObjectUid uid) =>
            !_objectSendBlacklist.Contains(uid);

        internal void SetStateReplicationEnabled(NetObjectUid netObjectUid, bool value)
        {
            var state = GetObject(netObjectUid);
            if (state == null)
                return;

            if (value)
                _objectSendBlacklist.Remove(netObjectUid);
            else
                _objectSendBlacklist.Add(netObjectUid);
        }
    }
}