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

        public event Action<NetWorldPlayerSnapshot> PlayerAdded = delegate { };
        public event Action<NetWorldPlayerSnapshot> PlayerRemoved = delegate { };

        public event Action<NetWorldPlayerSnapshot> PlayerBecomesReady = delegate { }; // Player loads game scene;
        public event DataSendRequestDelegate DataSendRequested = delegate { };

        public IReadOnlyCollection<byte[]> IncomingStateChangeRequests => _incomingStateChangeRequests;
        private readonly Queue<byte[]> _incomingStateChangeRequests = new();

        public IReadOnlyDictionary<NetObjectUid, NetObjectState> TemporaryUnregisteredObjects => _temporaryUnregisteredObjects;
        private readonly Dictionary<NetObjectUid, NetObjectState> _temporaryUnregisteredObjects = new();

        public IReadOnlyDictionary<NetObjectUid, NetObjectSceneIdentity> UnregisteredSceneObjects => _unregisteredSceneObjects;
        private readonly Dictionary<NetObjectUid, NetObjectSceneIdentity> _unregisteredSceneObjects = new();

        public readonly NetWorldHistoryBuffer HistoryBuffer = new();
        private readonly Func<ClientUid> _sessionOwnerProvider;
        private readonly NetWriter _writer = new();
        private readonly NetReader _reader = new();

        private float _lastUpdateTime;
        private float _localUpdateTime;

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

            TrySendWorldData();

            ProcessIncomingData();

            UpdateRemoteMasterWorldData();
            UpdateRemotePlayerStates();
            UpdateRemoteObjectsStates();
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
                if (Players.LocalPlayer == null || Players.LocalPlayer.IsMaster())
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
                while (remoteWorldDataUpdateRequests.Count > 0)
                {
                    _reader.Replace(buffer: remoteWorldDataUpdateRequests.Dequeue());
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

                        // Player not exists.
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

                    // Object doesn't exist - we need to wait for next update to proceed.
                    if (snapshot.StateChangeType < NetObjectStateChangeRequestType.UnreliableUpdate)
                        continue;

                    // Object was removed remotely - no need to proceed, just clear all the queues.
                    if (snapshot.StateChangeType is NetObjectStateChangeRequestType.ReliableRemoveUpdateHard or NetObjectStateChangeRequestType.ReliableRemoveUpdateSoft)
                    {
                        Debug.LogError($"Remote update snapshot form object with Uid: '{snapshot.ObjectUid}:{snapshot.PrefabId}' has state '{snapshot.StateChangeType}', but object doesn't exists.");

                        _temporaryUnregisteredObjects.Remove(snapshot.ObjectUid);
                        _unregisteredSceneObjects.Remove(snapshot.ObjectUid);

                        remoteObjectUpdateRequests.Remove(uid);
                        continue;
                    }

                    // Trying to fetch the object from temporary registry.
                    if (_temporaryUnregisteredObjects.TryGetValue(snapshot.ObjectUid, out var newState))
                        newState.ChangeObjectRemotely(snapshot);
                    else
                        newState = new NetObjectState(IsObjectValid, Players.Get, snapshot);

                    TryAddObject(newState);
                    remoteObjectUpdateRequests.Remove(uid);

                    _temporaryUnregisteredObjects.Remove(snapshot.ObjectUid);

                    if (_unregisteredSceneObjects.TryGetValue(snapshot.ObjectUid, out var unregisteredSceneObject))
                    {
                        View.InitializeObjectView(newState.Uid, unregisteredSceneObject.Identity);
                        View.RegisterAsPending(newState.Uid, unregisteredSceneObject.Identity);
                    }

                    _unregisteredSceneObjects.Remove(snapshot.ObjectUid);
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
                        _temporaryUnregisteredObjects.Add(newState.Uid, newState);

                        View.InitializeObjectView(newState.Uid, sceneIdentity.Identity);
                        View.RegisterAsPending(newState.Uid, sceneIdentity.Identity);
                    }

                    _unregisteredSceneObjects.Clear();
                }

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
                        state.ScheduleUpdate(NetObjectStateChangeRequestType.ReliableUpdate);
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
                            state.ScheduleUpdate(NetObjectStateChangeRequestType.ReliableRemoveUpdateHard);
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
            EnsureValid();

            _temporaryUnregisteredObjects.Clear();
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

        protected override void RemoveObject(NetObjectUid stateUid)
        {
            base.RemoveObject(stateUid);
            _temporaryUnregisteredObjects.Remove(stateUid);
            _unregisteredSceneObjects.Remove(stateUid);
        }

        internal void RegisterLocalPlayer(ClientUid clientUid)
        {
            // Add player and notify.
            var player = Players.Register(clientUid, isLocal: true);
            PlayerAdded(player.ToSnapshot());
        }

        internal void OnDisconnect(ClientUid clientUid)
        {
            if (!Players.Unregister(clientUid, out var removePlayerSnapshot)) 
                return;
            
            PlayerRemoved(removePlayerSnapshot);

            foreach (var objectUid in new List<NetObjectUid>(ObjectStates.Keys))
            {
                var state = ObjectStates[objectUid];

                // Object owned by disconnected player.
                if (state.IsOwnedBy(clientUid))
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
                            view.OnDisconnected(clientUid);
                    }
                    // Destroy object.
                    else
                        RemoveObject(objectUid);
                }
                // Notify view.
                else
                {
                    if (View != null && View.CurrentObjectViews.TryGetValue(objectUid, out var view))
                        view.OnDisconnected(clientUid);
                }
            }
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

            var state = new NetObjectState(IsObjectValid, Players.Get, NetObjectUid.FromClientUid(owner.Uid), prefabId, isSceneObject, owner.Uid);
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

            // Object or its temporary version already exist.
            if (TryGetExistedOrPendingState(sceneIdentity.SceneObjectId, out var state))
            {
                // TODO: CHECK IF WE REALLY NEED THIS BLOCK.
                // Initialize immateriality on master client.
                if (Players.LocalPlayer != null && Players.LocalPlayer.IsMaster())
                    View.InitializeObjectView(state.Uid, sceneIdentity.Identity);

                // Add view to be used in the future.
                View.RegisterAsPending(sceneIdentity.SceneObjectId, sceneIdentity.Identity);
            }
            // Object doesnt exist,
            else
            {
                // At this point we don't know who's going to be the owner, client it will be invalid just for now.
                _unregisteredSceneObjects.Add(sceneIdentity.SceneObjectId, sceneIdentity);
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