using System;
using System.Collections.Generic;
using UnityEngine;
using Keel.Networking;
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
        private const float AUTO_REMOVE_REMOTE_OBJECT_TIME_S = 6f; // Remove remote objects if now data was received for 'N' seconds.
        private const float OWNERSHIP_DISTRIBUTION_UPDATE_RATE = 5f;
        private const float OWNERSHIP_DISTRIBUTION_REQUEST_TIMEOUT = 2f;

        /// <summary>
        /// This id's for network objects that located in a scene by default.
        /// If you create a scene object in runtime, they use the default peer-based ID space.
        /// 1000 means scene object in must be from 0 to 1000 (1000 inclusive).
        /// </summary>
        internal const uint SCENE_OBJECT_RESERVED_UID_SPACE = 1000;

        internal const uint RESERVED_UID_SPACE_PER_CLIENT = 10000;

        public event Action<NetWorldPlayerSnapshot> PlayerAdded = delegate { };
        public event Action<NetWorldPlayerSnapshot> PlayerRemoved = delegate { };

        public event Action<NetWorldPlayerSnapshot> PlayerBecomesReady = delegate { }; // Player loads game scene;
        public event DataSendRequestDelegate DataSendRequested = delegate { };

        public IReadOnlyCollection<byte[]> IncomingStateChangeRequests => _incomingStateChangeRequests;
        private readonly Queue<byte[]> _incomingStateChangeRequests = new();

        public IReadOnlyDictionary<NetObjectUid, NetObjectSceneIdentity> UnregisteredSceneObjects => _unregisteredSceneObjects;
        private readonly Dictionary<NetObjectUid, NetObjectSceneIdentity> _unregisteredSceneObjects = new();

        private readonly Dictionary<NetObjectUid, float> _lastObjectOwnershipValidationTimeLookup = new();

        public readonly NetWorldHistoryBuffer HistoryBuffer = new();
        private readonly Func<ClientUid> _masterClientUidProvider;
        private readonly NetWriter _writer = new();
        private readonly NetReader _reader = new();
        private readonly HashSet<NetObjectUid> _objectSendBlacklist = new();

        private float _lastUpdateTime;
        private float _localUpdateTime;
        private float _nextOwnershipDistributionUpdateTime;

        private uint _netObjectUidCounter;

        private readonly List<NetObjectOwnershipDistributionRule> _ownershipDistributionRules = new();

        public NetWorldRuntime(Func<ClientUid> masterClientUidProvider, string worldName = "NetworkWorldRoot") : base(NetWorldId.Allocate(), worldName)
        {
            _masterClientUidProvider = masterClientUidProvider ?? throw new ArgumentNullException(nameof(masterClientUidProvider));
            foreach (var ruleType in Keel.Utils.Types.Lookup<NetObjectOwnershipDistributionRule>.Value)
            {
                if (Activator.CreateInstance(ruleType) is NetObjectOwnershipDistributionRule instance)
                    _ownershipDistributionRules.Add(instance);
            }
        }

        protected override ClientUid GetMasterClientUid() =>
            _masterClientUidProvider();

        /// <summary>
        /// Whether the automatic distribution pass may move this object. Consulted both when asking for an
        /// object and when answering somebody else's request, so the two sides cannot disagree about who is
        /// allowed to hold it. An explicit <see cref="NetObjectIdentity.OwnObject"/> bypasses the rules.
        /// </summary>
        private bool IsOwnershipDistributionAllowed(NetObjectState state)
        {
            foreach (var rule in _ownershipDistributionRules)
            {
                if (!rule.CanRequestOwnership(state))
                    return false;
            }

            return true;
        }

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
                        // Another client wants to tell us about its player state.
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
                                Debug.LogError($"Unable to schedule update for remote object '{snapshot.ObjectUid}' '{snapshot.PrefabUid}', invalid Uid.");
                                break;
                            }

                            if (remoteObjectUpdateRequests.TryAdd(snapshot.ObjectUid, snapshot))
                                break;

                            // If snapshot is already registered, we should check if it's older that the one we're trying to register.
                            var currentSnapshot = remoteObjectUpdateRequests[snapshot.ObjectUid];

                            // Packets could arrive in the wrong order.
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

                            var objectIdentity = View == null ? null : View.GetObjectView(state.ObjectUid);

                            // We need to handle rpc receive for objects, which are doesn't exist.
                            // It is probably already destroyed.
                            // No exceptions here, just warning.
                            if (objectIdentity == null)
                            {
#if NETWORK_DEBUG
                                var (delegateTargetType, methodInfo) = NetObjectRpcUtils.FindDelegateInfoByHashSlow(state.RpcState.Hash);
                                var rpcHandlerMethodName = delegateTargetType == null
                                    ? "UNKNOWN"
                                    : $"{delegateTargetType.FullName}.{methodInfo.Name}";

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

                // We need to peek the latest data here - the 'maxTime' value to filter out irrelevant data.
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
                        DataSendRequested(data: _writer.ToArray(), DeliveryMethod.Unreliable);
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
                        throw new InvalidOperationException($"Invalid Uid: '{snapshot.ObjectUid}'  Prefab: '{snapshot.PrefabUid}'");

                    // Check if the owner exists.
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

                        if (objState.StateChangeType == NetObjectStateChangeRequestType.RemoveHard)
                            RemoveObject(snapshot.ObjectUid);

                        continue;
                    }

                    // Object doesn't exist - check the state change type (should be at least unreliable update).
                    if (snapshot.StateChangeType < NetObjectStateChangeRequestType.UnreliableUpdate)
                        continue;

                    // Object was removed remotely - no need to proceed, just clear all the queues.
                    if (snapshot.StateChangeType is NetObjectStateChangeRequestType.RemoveHard or NetObjectStateChangeRequestType.RemoveSoft)
                    {
                        Debug.LogError($"Remote update snapshot form object with Uid: '{snapshot.ObjectUid}:{snapshot.PrefabUid}' has state '{snapshot.StateChangeType}', but object doesn't exists.");

                        _unregisteredSceneObjects.Remove(snapshot.ObjectUid);

                        remoteObjectUpdateRequests.Remove(uid);
                        continue;
                    }

                    // Create a new state.
                    var newState = new NetObjectState(Players.Get, snapshot);
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

                        var newState = new NetObjectState(Players.Get, sceneIdentity.SceneObjectId, sceneIdentity.PrefabUid, isSceneObject: true, owner: Players.LocalPlayer.Uid);
                        AddObject(newState);
                        newState.ScheduleUpdate(NetObjectStateChangeRequestType.UnreliableUpdate);

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
                    if (state.StateChangeType is NetObjectStateChangeRequestType.ReliableUpdate or NetObjectStateChangeRequestType.ReliableUpdateOwnerChange)
                        deliveryMethod = DeliveryMethod.Reliable;

                    state.PrepareForSerialization(Time);

                    _writer.SeekZero();
                    _writer.WriteByte((byte)NetWorldRuntimeChangeOp.ObjectStateUpdate);
                    state.ToSnapshot().Serialize(_writer);
                    DataSendRequested(data: _writer.ToArray(), deliveryMethod);

                    switch (state.StateChangeType)
                    {
                        case NetObjectStateChangeRequestType.RemoveHard:
                            RemoveObject(uid);
                            break;

                        case NetObjectStateChangeRequestType.RemoveSoft:
                            var view = View.GetObjectView(uid);
                            if (view != null)
                                view.OnSoftDestroyRequestedLocallyInternal();

                            state.ScheduleUpdate(NetObjectStateChangeRequestType.RemoveHard);
                            break;
                    }
                }
            }

            // Logic that handles automatic ownership distribution.
            void UpdateObjectOwnershipDistribution()
            {
                // Cleanup validation lookup times.
                if (_lastObjectOwnershipValidationTimeLookup.Count > 0)
                {
                    foreach (var uid in new List<NetObjectUid>(_lastObjectOwnershipValidationTimeLookup.Keys))
                    {
                        if (Time >= _lastObjectOwnershipValidationTimeLookup[uid])
                            _lastObjectOwnershipValidationTimeLookup.Remove(uid);
                    }
                }

                if (Players.LocalPlayer == null)
                    return;

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

                        if (!IsOwnershipDistributionAllowed(targetObject))
                            continue;

                        // Object could be in the dictionary only if it was recently requested to validate ownership.
                        // We don't allow multiple sequential or concurrent requests - 2-second timeout.
                        if (!_lastObjectOwnershipValidationTimeLookup.TryAdd(objectUid, Time + OWNERSHIP_DISTRIBUTION_REQUEST_TIMEOUT))
                            continue;

                        _writer.SeekZero();
                        _writer.WriteByte((byte)NetWorldRuntimeChangeOp.WorldObjectOwnerValidationRequest);
                        _writer.WriteBoolean(true); // is response?
                        _writer.WriteClientUid(newPotentialOwner);
                        _writer.WriteNetObjectUid(objectUid);
                        DataSendRequested(data: _writer.ToArray(), DeliveryMethod.Unreliable);
                    }
                }

                // Local objects.
                if (Time >= _nextOwnershipDistributionUpdateTime)
                {
                    _nextOwnershipDistributionUpdateTime = Time + OWNERSHIP_DISTRIBUTION_UPDATE_RATE;

                    // Iterate over the objects to request ownership.
                    foreach (var uid in new List<NetObjectUid>(ObjectStates.Keys))
                    {
                        var state = ObjectStates[uid];

                        // Skip local or tag objects.
                        if (state.IsOwnedByLocalPlayer || state.IsTagObjectForCurrentOwner)
                            continue;

                        if (!IsOwnershipDistributionAllowed(state))
                            continue;

                        var potentialOwner = GetPotentialOwner(state.Uid);
                        if (potentialOwner != Players.LocalPlayer.Uid)
                            continue;

                        _writer.SeekZero();
                        _writer.WriteByte((byte)NetWorldRuntimeChangeOp.WorldObjectOwnerValidationRequest);
                        _writer.WriteBoolean(false); // is response?
                        _writer.WriteClientUid(state.OwnerUid);
                        _writer.WriteClientUid(potentialOwner);
                        _writer.WriteNetObjectUid(uid);
                        DataSendRequested(data: _writer.ToArray(), DeliveryMethod.Unreliable);
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

        private ClientUid GetPotentialOwner(NetObjectUid objectUid)
        {
            if (!objectUid.IsValid)
                return ClientUid.Invalid;

            var bestClient = ClientUid.Invalid;
            var highestHash = int.MinValue;

            foreach (var player in Players.All)
            {
                // Combine object ID and client ID, then hash
                var hash = HashCombine(objectUid, player.Uid);
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
            _objectSendBlacklist.Remove(uid);
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
                    // Change owner if an object is a scene object.
                    if (state.IsSceneObject)
                    {
                        // Is local client becomes the master client, set the object's owner and sync it.
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
        /// Create an object with position and rotation input.
        /// Should be called on a local client to create the object across network.
        /// </summary>
        public NetObjectIdentity CreateObject(uint prefabUid, bool isSceneObject, Vector3 position, Quaternion rotation)
        {
            EnsureValid();

            // TODO: isSceneObject should be renamed to something else, like destroy with owner (when owner disconnected).
            var owner = Players.LocalPlayer;
            if (owner == null)
                throw new InvalidOperationException("Local player is not registered.");

            // Increment internal id.
            _netObjectUidCounter++;

            // Calculate the source id value, applying client offsets.
            var newObjectUid = NetObjectUid.FIRST_VALID_UID + SCENE_OBJECT_RESERVED_UID_SPACE + (uint)owner.Uid * RESERVED_UID_SPACE_PER_CLIENT + _netObjectUidCounter;

            // Create and register a new state.
            var newState = new NetObjectState(Players.Get, uid: new NetObjectUid(newObjectUid), prefabUid, isSceneObject, owner.Uid);
            AddObject(newState);
            newState.ScheduleUpdate(NetObjectStateChangeRequestType.UnreliableUpdate);

            return View.CreateObjectViewForState(newState, position, rotation);
        }

        public NetObjectState CreateObjectAsync(uint prefabUid, bool isSceneObject, Vector3 position, Quaternion rotation, Action<NetObjectIdentity> callback)
        {
            EnsureValid();
            var owner = Players.LocalPlayer;
            if (owner == null)
                throw new InvalidOperationException("Local player is not registered.");

            // Increment internal id.
            _netObjectUidCounter++;

            // Calculate the source id value, applying client offsets.
            var newObjectUid = NetObjectUid.FIRST_VALID_UID + SCENE_OBJECT_RESERVED_UID_SPACE + (uint)owner.Uid * RESERVED_UID_SPACE_PER_CLIENT + _netObjectUidCounter;

            // Create and register a new state.
            var newState = new NetObjectState(Players.Get, uid: new NetObjectUid(newObjectUid), prefabUid, isSceneObject, owner.Uid);
            AddObject(newState);
            newState.ScheduleUpdate(NetObjectStateChangeRequestType.UnreliableUpdate);

            View.CreateObjectViewForStateAsync(newState, position, rotation, callback);
            return newState;
        }

        /// <summary>
        /// Register an object which is not created via a main object creation pipeline.
        /// See the 'CreateObject' method.
        /// This is not an initialized object, so an object will be invalid until the very first update happens.
        /// </summary>
        private void TryRegisterOrInitializeSceneObjectIdentity(NetObjectSceneIdentity sceneIdentity)
        {
            if (sceneIdentity.Identity.Initialized)
                throw new InvalidOperationException($"'{sceneIdentity.gameObject.name}' already initialized.");

            if (ObjectExists(sceneIdentity.SceneObjectId))
            {
#if NETWORK_DEBUG
                Debug.LogWarning($"Trying to register scene identity '{sceneIdentity.gameObject.name}' '{sceneIdentity.PrefabUid}': but object with id '{sceneIdentity.SceneObjectId}' already registered.");
#endif
                UnityEngine.Object.Destroy(sceneIdentity.gameObject);
                return;
            }

            // At this point we don't know who's going to be the owner, client it will be invalid just for now.
            if (!_unregisteredSceneObjects.TryAdd(sceneIdentity.SceneObjectId, sceneIdentity))
                throw new InvalidOperationException($"Unable to register object '{sceneIdentity.gameObject.name}'. Object with the same uid '{sceneIdentity.SceneObjectId}' was already registered.");
        }

        public int GetHistoryBufferSize()
        {
            EnsureValid();
            return HistoryBuffer.Size;
        }

        /// <summary>
        /// World has to be notified when the game world is loaded.
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