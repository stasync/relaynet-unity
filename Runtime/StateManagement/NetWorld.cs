using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class WorldPlayersCollection
{
    ClientUID m_LocalPlayerUID;
    ClientUID m_MasterClientUID;

    public IEnumerable<ClientUID> PlayerUIDs => m_PlayersCollection.Keys;
    Dictionary<ClientUID, WorldPlayer> m_PlayersCollection;

    Queue<Action> m_UpdateQueue;

    public WorldPlayer LocalPlayer => Find(m_LocalPlayerUID);
    public WorldPlayer MasterPlayer => Find(m_MasterClientUID);
    public WorldPlayer Find(ClientUID uid) => m_PlayersCollection.TryGetValue(uid, out var player) ? player : null;
    public bool Contains(ClientUID uid) => m_PlayersCollection.ContainsKey(uid);

    public WorldPlayersCollection()
    {
        m_LocalPlayerUID = ClientUID.Invalid;
        m_PlayersCollection = new Dictionary<ClientUID, WorldPlayer>();
        m_UpdateQueue = new Queue<Action>();
    }

    public void Register(ClientUID uid, string name, bool isLocal) =>
        ScheduleOnUpdate(() =>
        {
            if (isLocal)
                m_LocalPlayerUID = uid;

            if (m_PlayersCollection.ContainsKey(uid))
                throw new InvalidOperationException();

            m_PlayersCollection.Add(uid, new WorldPlayer(uid, name, () => m_LocalPlayerUID, () => m_MasterClientUID));
        });

    public void Unregister(ClientUID uid) =>
        ScheduleOnUpdate(() =>
        {
            if (m_LocalPlayerUID == uid)
                m_LocalPlayerUID = ClientUID.Invalid;

            m_PlayersCollection.Remove(uid);
        });

    void ScheduleOnUpdate(Action a) => m_UpdateQueue.Enqueue(a);

    public void Udpate()
    {
        while (m_UpdateQueue.Count > 0)
            m_UpdateQueue.Dequeue()();

        m_MasterClientUID = PlayerUIDs.Any() ? PlayerUIDs.Min() : ClientUID.Invalid;
    }

    public void Clear()
    {
        m_LocalPlayerUID = ClientUID.Invalid;
        m_MasterClientUID = ClientUID.Invalid;

        m_PlayersCollection.Clear();
        m_PlayersCollection = null;

        m_UpdateQueue.Clear();
        m_UpdateQueue = null;
    }
}

public class WorldPlayer
{
    public readonly ClientUID UID;
    public readonly string Name;
    public bool IsLocal => UID == m_LocalPlayer();
    public bool IsMaster => UID == m_MasterPlayer();

    readonly Func<ClientUID> m_LocalPlayer;
    readonly Func<ClientUID> m_MasterPlayer;

    public WorldPlayer(ClientUID uid, string name, Func<ClientUID> localPlayer, Func<ClientUID> masterPlayer)
    {
        UID = uid;
        Name = name;
        m_LocalPlayer = localPlayer;
        m_MasterPlayer = masterPlayer;
    }

    public override string ToString() => $"{UID}:{Name} l:{IsLocal} m:{IsMaster}";
}

public struct WorldSnapshot 
{
    public IReadOnlyDictionary<NetObjectUID, ObjectUpdateSnapshot> Objects => m_ObjectStates;
    readonly Dictionary<NetObjectUID, ObjectUpdateSnapshot> m_ObjectStates;

    // TODO: World players;

    public WorldSnapshot(Dictionary<NetObjectUID, ObjectUpdateSnapshot> objects)
    {
        m_ObjectStates = objects;
    }

    public static WorldSnapshot FromWorld(NetWorld world)
    {
        var objects = new Dictionary<NetObjectUID, ObjectUpdateSnapshot>();
        foreach (var kvp in world.ObjectStates)
            objects.Add(kvp.Key, kvp.Value.ToSnapshot());

        return new WorldSnapshot(objects);
    }
}

public class NetWorld : IJSONSerializable
{
    public NetWorldUID UID { get; private set; }

    public event Action<NetObjectIdentity> NewObjectInitialized = delegate { };

    public WorldPlayersCollection Players { get; private set; }

    public IReadOnlyDictionary<NetObjectUID, NetObjectIdentity> ObjectViews => m_CurrentObjectViews;
    public IReadOnlyDictionary<NetObjectUID, NetObjectState> ObjectStates => m_CurrentObjectStates;
    public IReadOnlyDictionary<NetObjectUID, NetObjectState> ObjectsRegisterRequests => m_ObjectsRegisterRequests;
    public IReadOnlyDictionary<NetObjectUID, ObjectUpdateSnapshot> RemoteObjectUpdateRequests => m_RemoteObjectUpdateRequests;

    //===
    Dictionary<NetObjectUID, NetObjectState> m_CurrentObjectStates;
    Dictionary<NetObjectUID, NetObjectIdentity> m_CurrentObjectViews;

    Dictionary<NetObjectUID, NetObjectState> m_ObjectsRegisterRequests;
    Dictionary<NetObjectUID, NetObjectIdentity> m_PendingObjectViews;

    Dictionary<NetObjectUID, ObjectUpdateSnapshot> m_RemoteObjectUpdateRequests;
    //===

    NetWorldView m_WorldView;
    INetwork m_InternalNetwork;
    NetObjectFactory m_ObjectFactory;
    float m_LastUpdateTime;

    const float m_MinUpdateInteraval = 0.1f; //10 times per second;

    public NetWorld(INetwork network, string worldName = "NetworkWorldRoot")
    {
        if (m_WorldView != null)
            throw new InvalidOperationException($"{nameof(m_WorldView)} alreay created");

        m_InternalNetwork = network ?? throw new ArgumentNullException(nameof(network));

        UID = NetWorldUID.Allocate();

        m_WorldView = new GameObject($"{UID}:{worldName}")
            .AddComponent<NetWorldView>();

        UnityEngine.Object.DontDestroyOnLoad(m_WorldView.gameObject);

        Players = new WorldPlayersCollection();

        m_ObjectFactory = new NetObjectFactory();

        /// Must be cleaned, when player disconnects;
        {
            m_CurrentObjectViews = new Dictionary<NetObjectUID, NetObjectIdentity>();
            m_CurrentObjectStates = new Dictionary<NetObjectUID, NetObjectState>();

            m_RemoteObjectUpdateRequests = new Dictionary<NetObjectUID, ObjectUpdateSnapshot>();

            m_ObjectsRegisterRequests = new Dictionary<NetObjectUID, NetObjectState>();
            m_PendingObjectViews = new Dictionary<NetObjectUID, NetObjectIdentity>();
        }
    }

    void RemoveFromDictionary<T>(NetObjectUID uid, Dictionary<NetObjectUID, T> dictionary, Action<T> valueProcessor = null)
    {
        if (!dictionary.TryGetValue(uid, out var val))
            return;

        dictionary.Remove(uid);
        valueProcessor?.Invoke(val);
    }

    public WorldSnapshot ToSnapshot() => WorldSnapshot.FromWorld(this);

    public void ScheduleUpdateForRemoteObject(NetObjectUID UID, ObjectUpdateSnapshot snapshot)
    {
        if (m_RemoteObjectUpdateRequests.ContainsKey(UID))
        {
            var current = m_RemoteObjectUpdateRequests[UID];
            if (current.StateChangeType < snapshot.StateChangeType)
                m_RemoteObjectUpdateRequests[UID] = snapshot;
            else
                Debug.LogError($"Unable to schedule update for remote object {snapshot.ObjectUID} {snapshot.PrefabId} with {snapshot.StateChangeType}, already scheduled update witht high priority");
        }
        else
            m_RemoteObjectUpdateRequests.Add(UID, snapshot);
    }

    void ForEachState(Action<NetObjectUID, NetObjectState> processor)
    {
        foreach (var UID in m_CurrentObjectStates.Keys.ToArray())
            processor(UID, m_CurrentObjectStates[UID]);
    }

    public void TryUpdate(float deltaTime)
    {
        var worldDelta = Time.fixedUnscaledTime - m_LastUpdateTime;
        if (worldDelta < m_MinUpdateInteraval)
            return;

        m_LastUpdateTime = Time.fixedUnscaledTime;

        /// Loop
        {
            /// Update player collection;
            Players.Udpate();
            /// Remove objects, if player disconnected;
            UpdateRemovedPlyers();
            /// Update remote object state data;
            UpdateRemoteObjects();
            /// Update local object data;
            UpdateLocalObjects();
            /// Save current;
            RecordState(worldDelta);
            /// Execulte;
            ExecuteState();
        }
        //This is not used at now;
        m_WorldView.OnUpdate(worldDelta);

        #region INTERNAL
        void UpdateRemovedPlyers()
        {
            foreach (var UID in m_CurrentObjectStates.Keys.ToArray())
            {
                var state = m_CurrentObjectStates[UID];

                if (!Players.Contains(state.OwnerUID) && !state.IsSceneObject)
                {
                    m_CurrentObjectStates.Remove(UID);

                    RemoveFromDictionary(UID, m_CurrentObjectViews, v => UnityEngine.Object.Destroy(v.gameObject));
                    RemoveFromDictionary(UID, m_PendingObjectViews, v => UnityEngine.Object.Destroy(v.gameObject));
                    RemoveFromDictionary(UID, m_ObjectsRegisterRequests);
                    RemoveFromDictionary(UID, m_RemoteObjectUpdateRequests);
                }
                else
                {
                    if (m_CurrentObjectViews.TryGetValue(UID, out var view))
                        view.OnDisconnected(state.OwnerUID);
                }
            }
        }

        void UpdateRemoteObjects()
        {
            foreach (var UID in m_RemoteObjectUpdateRequests.Keys.ToArray())
            {
                var snapshot = m_RemoteObjectUpdateRequests[UID];

                if (!snapshot.ObjectUID.IsValid || string.IsNullOrWhiteSpace(snapshot.PrefabId) || snapshot.Data == null)
                    throw new InvalidOperationException();

                /// Update existed;
                if (m_CurrentObjectStates.TryGetValue(snapshot.ObjectUID, out var objState))
                {
                    objState.ChangeObjectRemotely(snapshot);
                    m_RemoteObjectUpdateRequests.Remove(UID);
                }
                /// Add new or get pending;
                else
                {
                    if (snapshot.StateChangeType != ObjectStateChangeRequestType.InitializationUpdate)
                        continue;

                    if (m_ObjectsRegisterRequests.TryGetValue(snapshot.ObjectUID, out var newState) && newState.IsOwnerBy(snapshot.OwnerUID))
                    {
                        newState.ChangeObjectRemotely(snapshot);
                        m_CurrentObjectStates.Add(snapshot.ObjectUID, newState);

                        m_ObjectsRegisterRequests.Remove(snapshot.ObjectUID);
                    }
                    else
                        newState = new NetObjectState(snapshot, Players.Find);

                    m_CurrentObjectStates.Add(snapshot.ObjectUID, newState);
                    m_RemoteObjectUpdateRequests.Remove(UID);
                }
            }
        }

        void UpdateLocalObjects()
        {
            /// Register new objects, that was created localy;
            {
                //Register new local object, that was just created;
                foreach (var UID in m_ObjectsRegisterRequests.Keys.ToArray())
                {
                    var state = m_ObjectsRegisterRequests[UID];
                    //Skip remote objects;
                    if (!state.IsOwnerByLocalPlayer)
                        continue;

                    if (m_CurrentObjectStates.ContainsKey(state.UID))
                        Debug.LogError($"Object with UID {state.UID} already exists");
                    else
                    {
                        state.ScheduleUpdate(ObjectStateChangeRequestType.InitializationUpdate);
                        m_CurrentObjectStates.Add(state.UID, state);
                    }

                    m_ObjectsRegisterRequests.Remove(UID);
                }
            }

            //Update local objects;
            foreach (var netObject in m_CurrentObjectStates.Values)
            {
                if (!netObject.IsOwnerByLocalPlayer)
                    continue;

                if (!m_CurrentObjectViews.TryGetValue(netObject.UID, out var netObjectView))
                {
                    //Try add pending view;
                    if (m_PendingObjectViews.TryGetValue(netObject.UID, out netObjectView))
                        m_CurrentObjectViews.Add(netObject.UID, netObjectView);
                    else
                    {
                        Debug.LogError($"Unable to find view for object with UID {netObject.UID}");
                        continue;
                    }
                }

                var deliveryIntent = DeliveryIntent.Unreliable;
                if (netObjectView.State.StateChangeType >= ObjectStateChangeRequestType.ReliableUpdate)
                {
                    deliveryIntent = DeliveryIntent.Reliable;
                    if (netObjectView.State.StateChangeType == ObjectStateChangeRequestType.InitializationUpdate)
                        netObjectView.SetDirtyIfLocal();
                }
                netObjectView.OnUpdate(worldDelta);

                if (netObject.DataChanged || deliveryIntent == DeliveryIntent.Reliable)
                    m_InternalNetwork.SendGameObjectStateMessage(netObject.Serialize(), deliveryIntent);
            }
        }

        void RecordState(float delta)
        {
        }

        void ExecuteState()
        {
            //Update objects;
            foreach (var UID in m_CurrentObjectStates.Keys.ToArray())
            {
                var state = m_CurrentObjectStates[UID];

                //Try get or create new state view;
                if (!m_CurrentObjectViews.TryGetValue(UID, out var netObjectView))
                {
                    if (m_PendingObjectViews.TryGetValue(UID, out netObjectView))
                    {
                        m_PendingObjectViews.Remove(UID);
                        m_CurrentObjectViews.Add(UID, netObjectView);
                    }
                    else
                    {
                        netObjectView = m_ObjectFactory.Create(state.PrefabId, Vector3.zero, Quaternion.identity);
                        m_CurrentObjectViews.Add(UID, netObjectView);
                    }
                }

                //Remove state if need;
                if (state.StateChangeType == ObjectStateChangeRequestType.RemoveUpdate)
                {
                    m_CurrentObjectStates.Remove(UID);
                    m_CurrentObjectViews.Remove(UID);

                    if (netObjectView != null)
                        UnityEngine.Object.Destroy(netObjectView.gameObject);
                }
                else
                {
                    //Initialize new view;
                    if (netObjectView.Initialized)
                    {
                        if (!state.IsOwnerByLocalPlayer)
                            netObjectView.Deserialize(state.Data, false, false);
                    }
                    else
                        InitializeIdentity(netObjectView, state, m_InternalNetwork, null);

                    //Executed;
                    state.OnExecuted();

                    //Report to player;
                    var senderPlayer = m_InternalNetwork.FindPlayer(state.OwnerUID);
                    if (senderPlayer == null)
                    {
                        Debug.LogError($"Unable to update object state for player with UID {state.OwnerUID}. Player doesn't exists");
                        continue;
                    }

                    if(!senderPlayer.IsLocal)
                        senderPlayer.OnObjectCreateUpdate(netObjectView);
                }
            }
        }
        #endregion
    }

    /// <summary>
    /// Destroy all network objects and cleanup scene.
    /// </summary>
    public void Destroy()
    {
        Debug.LogError($"Destroy world {m_WorldView.gameObject}");
        NewObjectInitialized = null;

        Players.Clear();
        Players = null;

        m_CurrentObjectViews.Clear();
        m_CurrentObjectViews = null;

        m_CurrentObjectStates.Clear();
        m_CurrentObjectStates = null;

        m_RemoteObjectUpdateRequests.Clear();
        m_RemoteObjectUpdateRequests = null;

        m_ObjectsRegisterRequests.Clear();
        m_ObjectsRegisterRequests = null;

        m_PendingObjectViews.Clear();
        m_PendingObjectViews = null;

        UnityEngine.Object.Destroy(m_WorldView.gameObject); //Destroy all net object;
        m_WorldView = null;

        m_InternalNetwork = null;
        m_ObjectFactory = null;
    }

    public void OnPlayerBecomesReady(INetRTPlayer player, bool isLocal) =>
        Players.Register(player.UID, player.Name, isLocal);

    /// <summary>
    /// Someone connected and loaded game scene, schedule local objects for reliable update update;
    /// </summary>
    /// <param name="peerId"></param>
    public void OnRemotePlayerLoadedGameScene(ClientUID peerId)
    {
        foreach (var obj in m_CurrentObjectViews.Values)
            obj.OnBecomesReady(peerId);
    }

    /// <summary>
    /// Someone disconnected.
    /// </summary>
    /// <param name="uid"></param>
    public void OnDisconnect(ClientUID uid)
    {
        Players.Unregister(uid);
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
    public NetObjectIdentity CreateObject(INetRTPlayer owner, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation, object[] additionalData)
    {
        var uid = new NetObjectUID(owner.UID);
        var state = new NetObjectState(uid, prefabId, isSceneObject, owner.UID, Players.Find);
        var netObjectView = m_ObjectFactory.Create(state.PrefabId, position, rotation);

        InitializeIdentity(netObjectView, state, m_InternalNetwork, additionalData);

        m_ObjectsRegisterRequests.Add(uid, state);
        m_PendingObjectViews.Add(uid, netObjectView);

        return netObjectView;
    }

    /// <summary>
    /// Register object, which is not created via main object creation pipline.
    /// See 'CreateObject' method.
    /// This is not initialize object, so object will be invalid, untill first update happens.
    /// </summary>
    /// <param name="sceneIdentity"></param>
    public void TryRegisterOrInitializeSceneObjectIdentity(GSSceneObjectIdentity sceneIdentity)
    {
        if (m_CurrentObjectStates.ContainsKey(sceneIdentity.SceneObjectId))
        {
#if NETWORK_DEBUG
            Debug.LogErrorFormat("Trying to register scene identity {0}: but object with id {1} already registered",
                sceneIdentity.gameObject.name,
                sceneIdentity.SceneObjectId);
#endif
            return;
        }

        if (sceneIdentity.Identity.Initialized)
            throw new InvalidOperationException($"{sceneIdentity.gameObject.name} alredy initialized");

        //Create state from scen object identity;
        //We don't need to know scene object prefab id, because it's already in scene, but prefab id can't be null.
        var newState = new NetObjectState(sceneIdentity.SceneObjectId, "SceneObject", true, m_InternalNetwork.MasterClientConnection, Players.Find);
        
        //Initialize immidiatlty on master client;
        if (m_InternalNetwork.IsMasterClient)
            InitializeIdentity(sceneIdentity.Identity, newState, m_InternalNetwork, null);

        //Schedule state to be registerd;
        m_ObjectsRegisterRequests.Add(newState.UID, newState);

        //Add view to be used in future;
        m_PendingObjectViews.Add(newState.UID, sceneIdentity.Identity);
    }

    NetObjectIdentity InitializeIdentity(NetObjectIdentity identity, NetObjectState state, INetwork transport, object[] additionalData)
    {
        if(identity.Initialized)
            throw new InvalidOperationException($"Indentity {identity.gameObject.name}:{state.UID} already initialized");

        //Put to world view;
        UnityEngine.Object.DontDestroyOnLoad(identity.gameObject);
        identity.transform.SetParent(m_WorldView.transform);

        //Init;
        identity.Initialze(state, transport, this, additionalData);
        NewObjectInitialized(identity);
        return identity;
    }

    /// <summary>
    /// Find game object reference by id.
    /// Can be NULL.
    /// </summary>
    /// <param name="instanceId"></param>
    /// <returns></returns>
    public NetObjectIdentity FindObject(NetObjectUID instanceId) =>
        m_CurrentObjectViews.TryGetValue(instanceId, out NetObjectIdentity identity) ? identity : null;

    /// <summary>
    /// Get object before in was actually created by transport system.
    /// Note that this object will not be in valid state, until update happens.
    /// Use in only if its highly nessesery.
    public NetObjectIdentity FindObjectOrCreateMock(INetRTPlayer owner, NetObjectUID instanceId, string prefabId, bool isSceneObject, Vector3 position, Quaternion rotation)
    {
        var identity = FindObject(instanceId);
        return 
            identity == null ?
            CC() : 
            identity;

        NetObjectIdentity CC()
        {
            var state = new NetObjectState(instanceId, prefabId, isSceneObject, owner.UID, Players.Find);
            var netObjectView = m_ObjectFactory.Create(state.PrefabId, position, rotation);

            InitializeIdentity(netObjectView, state, m_InternalNetwork, null);

            m_ObjectsRegisterRequests.Add(instanceId, state);
            m_PendingObjectViews.Add(instanceId, netObjectView);

            return netObjectView;
        }
    }

    /// <summary>
    /// If some object doesn't exist yet, we can wait for it;
    /// Similar to: <see cref="FindObjectOrCreateMock(INetRTPlayer, NetObjectUID, string, bool, Vector3, Quaternion)"/>
    /// But it doesn't create mock objects;
    /// </summary>
    /// <param name="instanceId"></param>
    /// <param name="result"></param>
    public void FindObjectOrWait(NetObjectUID instanceId, Action<NetObjectIdentity> result)
    {
        var identity = FindObject(instanceId);
        if(identity != null)
        {
            result(identity);
            return;
        }
        NewObjectInitialized += OnNewObjectInitialized;

        void OnNewObjectInitialized(NetObjectIdentity o)
        {
            if (o == null || o.InstanceId != instanceId)
                return;

            result(o);
            NewObjectInitialized -= OnNewObjectInitialized;
        }
    }

    public string ToJSONProperty() => RealtimeJSONUtils.ToJSONCollection(m_CurrentObjectViews.Values.Select(o => o.ToJSONProperty()).ToArray());
}
