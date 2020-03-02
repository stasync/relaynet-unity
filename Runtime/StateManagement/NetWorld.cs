using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class NetWorld : IJSONSerializable, INetWoldOperand
{
    #region INetWoldOperand
    Dictionary<NetObjectUID, NetObjectState> INetWoldOperand.ObjectStates => m_CurrentObjectStates;
    Dictionary<NetObjectUID, NetObjectIdentity> INetWoldOperand.ObjectViews => m_CurrentObjectViews;
    Dictionary<NetObjectUID, NetObjectState> INetWoldOperand.ObjectsRegisterRequests => m_ObjectsRegisterRequests;
    Dictionary<NetObjectUID, NetObjectIdentity> INetWoldOperand.PendingObjectViews => m_PendingObjectViews;
    Dictionary<NetObjectUID, ObjectUpdateSnapshot> INetWoldOperand.RemoteObjectUpdateRequests => m_RemoteObjectUpdateRequests;
    #endregion

    public NetWorldUID UID { get; private set; }

    public event Action<NetObjectIdentity> NewObjectInitialized = delegate { };
    public NetWorldPlayersCollection Players { get; private set; }

    public IReadOnlyDictionary<NetObjectUID, NetObjectState> ObjectStates => m_CurrentObjectStates;
    public IReadOnlyDictionary<NetObjectUID, NetObjectIdentity> ObjectViews => m_CurrentObjectViews;
    public IReadOnlyDictionary<NetObjectUID, NetObjectState> ObjectsRegisterRequests => m_ObjectsRegisterRequests;
    public IReadOnlyDictionary<NetObjectUID, ObjectUpdateSnapshot> RemoteObjectUpdateRequests => m_RemoteObjectUpdateRequests;

    //===
    Dictionary<NetObjectUID, NetObjectState> m_CurrentObjectStates;
    Dictionary<NetObjectUID, NetObjectIdentity> m_CurrentObjectViews;

    Dictionary<NetObjectUID, NetObjectState> m_ObjectsRegisterRequests;
    Dictionary<NetObjectUID, NetObjectIdentity> m_PendingObjectViews;

    Dictionary<NetObjectUID, ObjectUpdateSnapshot> m_RemoteObjectUpdateRequests;
    //===

    public IReadOnlyCollection<NetWorldSnapshot> HistroyBuffer => m_HistoryBuffer;
    LinkedList<NetWorldSnapshot> m_HistoryBuffer;

    NetWorldView m_WorldView;
    INetwork m_InternalNetwork;
    NetObjectFactory m_ObjectFactory;
    float m_LastUpdateTime;

    const float c_MinUpdateInteraval = 0.1f; //10 times per second;
    const float c_HistoryBufferSecondsLength = 30f; //Record last 30 seconds;

    List<NetWorldOperator> m_Operators;

    public NetWorld(NetWorldUID uid, string worldName)
    {
        UID = uid;
        m_WorldView = new GameObject($"{UID}:{worldName}").AddComponent<NetWorldView>();
        UnityEngine.Object.DontDestroyOnLoad(m_WorldView.gameObject);
    }

    public NetWorld(INetwork network, string worldName = "NetworkWorldRoot") : this(NetWorldUID.Allocate(), worldName)
    {
        if (m_WorldView != null)
            throw new InvalidOperationException($"{nameof(m_WorldView)} alreay created");

        m_InternalNetwork = network ?? throw new ArgumentNullException(nameof(network));

        Players = new NetWorldPlayersCollection();
        m_ObjectFactory = new NetObjectFactory();

        /// Must be cleaned, when player disconnects;
        {
            m_CurrentObjectViews = new Dictionary<NetObjectUID, NetObjectIdentity>();
            m_CurrentObjectStates = new Dictionary<NetObjectUID, NetObjectState>();

            m_RemoteObjectUpdateRequests = new Dictionary<NetObjectUID, ObjectUpdateSnapshot>();

            m_ObjectsRegisterRequests = new Dictionary<NetObjectUID, NetObjectState>();
            m_PendingObjectViews = new Dictionary<NetObjectUID, NetObjectIdentity>();
        }

        m_HistoryBuffer = new LinkedList<NetWorldSnapshot>();

        m_Operators = new List<NetWorldOperator>()
        {
            new NetWorldRemovedPlayersOperator(this),
            new NetWorldUpdateRemoteObjectsOperator(this),
            new NetWorldUpdateLocalObjectsOperator(m_InternalNetwork, this),
            new NetWorldGenericOperator(this, p => 
                {
                    var elementsCount = c_HistoryBufferSecondsLength / c_MinUpdateInteraval;
                    m_HistoryBuffer.AddLast(NetWorldSnapshot.FromWorld(p.DeltaTime, this));
                    if (m_HistoryBuffer.Count > elementsCount)
                        m_HistoryBuffer.RemoveFirst();
                }),
            new NetWorldExecutorOperator(m_InternalNetwork, m_ObjectFactory, this),
            new NetWorldGenericOperator(this, p =>
                {
                    Players.FinalizeIteration();
                    p.ForEachStates((UID, state) => state.FinalizeIteration());
                }),
        };
    }

    public NetWorldSnapshot ToSnapshot(float deltaTime = 0f) => NetWorldSnapshot.FromWorld(deltaTime, this);

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

    public void TryUpdate(float deltaTime)
    {
        var worldDelta = Time.fixedUnscaledTime - m_LastUpdateTime;
        if (worldDelta < c_MinUpdateInteraval)
            return;

        m_LastUpdateTime = Time.fixedUnscaledTime;
        Players.Udpate();

        /// Execute opertors
        foreach (var op in m_Operators)
            op.Execute(deltaTime);

        //This is not used at now;
        m_WorldView.OnUpdate(worldDelta);
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

        m_HistoryBuffer.Clear();
        m_HistoryBuffer = null;

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

    NetObjectIdentity INetWoldOperand.InitializeIdentity(NetObjectIdentity identity, NetObjectState state, INetwork transport, object[] additionalData) =>
        InitializeIdentity(identity, state, transport, additionalData);

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
