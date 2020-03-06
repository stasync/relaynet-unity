using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class NetWorldRuntime : NetWorld, IJSONSerializable
{
    public IReadOnlyDictionary<NetObjectUID, NetObjectState> ObjectsRegisterRequests => m_ObjectsRegisterRequests;
    Dictionary<NetObjectUID, NetObjectState> m_ObjectsRegisterRequests;

    public IReadOnlyDictionary<NetObjectUID, ObjectUpdateSnapshot> RemoteObjectUpdateRequests => m_RemoteObjectUpdateRequests;    
    Dictionary<NetObjectUID, ObjectUpdateSnapshot> m_RemoteObjectUpdateRequests;

    public IReadOnlyCollection<NetWorldSnapshot> HistroyBuffer => m_HistoryBuffer;
    LinkedList<NetWorldSnapshot> m_HistoryBuffer;

    INetwork m_InternalNetwork;
    Action<byte[], DeliveryIntent> m_ObjectSender;

    const float c_HistoryBufferSecondsLength = 30f; //Record last 30 seconds;
    const float c_MinUpdateInteraval = 0.1f; //10 times per second;
    float m_LastUpdateTime;

    public NetWorldRuntime(INetwork network, Action<byte[], DeliveryIntent> objectSender, string worldName = "NetworkWorldRoot") : base(NetWorldUID.Allocate(), worldName)
    {
        m_InternalNetwork = network ?? throw new ArgumentNullException(nameof(network));
        m_ObjectSender = objectSender ?? throw new ArgumentNullException(nameof(objectSender));

        NetObjectUID.Reset();

        /// Must be cleaned, when player disconnects;
        {
            m_RemoteObjectUpdateRequests = new Dictionary<NetObjectUID, ObjectUpdateSnapshot>();
            m_ObjectsRegisterRequests = new Dictionary<NetObjectUID, NetObjectState>();
        }

        m_HistoryBuffer = new LinkedList<NetWorldSnapshot>();
    }

    public NetWorldEvaluator Replicate() => new NetWorldEvaluator(this);

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

    protected override void UpdateImplementation()
    {
        var worldDelta = Time.fixedUnscaledTime - m_LastUpdateTime;
        if (worldDelta < c_MinUpdateInteraval)
            return;

        m_LastUpdateTime = Time.fixedUnscaledTime;
        Evaluate(worldDelta);
    }

    #region Loop
    protected override void OnUpdateWorldState(float deltaTime)
    {
        Players.Udpate();

        UpdateStateBasedOnRemovedPlayers();
        UpdateStateBasedOnRemoteData();
        UpdateLocalObjectState();

        #region Internal
        /// Update states based on removed player, if player leave, remove object;
        void UpdateStateBasedOnRemovedPlayers()
        {
            ForEachStates((stateUID, state) =>
            {
                foreach (var playerUID in Players.RemovedPlayers.ToArray())
                {
                    //Object owned by disconnected player;
                    if (state.IsOwnedBy(playerUID))
                    {
                        //Change owner, if object is scene obeject;
                        if (state.IsSceneObject)
                        {
                            //Is local client becomes master client, set object's owner and sync it.
                            if (Players.LocalPlayer != null && Players.LocalPlayer.IsMaster)
                                state.ScheduleOwnerChanged(Players.LocalPlayer.UID);
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
                if (WorldView.CurrentObjectViews.TryGetValue(objectUID, out var view))
                    view.OnDisconnected(clientUID);
            }
        }

        /// Update state based on remote incoming data;
        void UpdateStateBasedOnRemoteData()
        {
            ForEachRemoteRequests((UID, snapshot) =>
            {
                if (!snapshot.ObjectUID.IsValid || string.IsNullOrWhiteSpace(snapshot.PrefabId) || snapshot.Data == null)
                    throw new InvalidOperationException();

                /// Object exists, just update;
                if (ObjectStates.TryGetValue(snapshot.ObjectUID, out var objState))
                {
                    objState.ChangeObjectRemotely(snapshot);
                    m_RemoteObjectUpdateRequests.Remove(UID);

                    if (objState.RequireRemoving)
                        RemoveObject(snapshot.ObjectUID);

                    return;
                }

                /// Add new or get pending;
                if (snapshot.StateChangeType != ObjectStateChangeRequestType.InitializationUpdate)
                    return;

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
                    newState = new NetObjectState(snapshot, Players.Find);

                AddObject(snapshot.ObjectUID, newState);
                m_RemoteObjectUpdateRequests.Remove(UID);
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
                /// We need view to send and serialize objects;
                var view = WorldView.GetView(UID);
                if(view == null)
                {
                    Debug.LogError($"Unable to find view for object with UID {state.UID}");
                    return;
                }

                var deliveryIntent = DeliveryIntent.Unreliable;
                if (state.StateChangeType >= ObjectStateChangeRequestType.ReliableUpdate)
                    deliveryIntent = DeliveryIntent.Reliable;

                state.RequestUpdate();
                if (state.ChangedLocaly || deliveryIntent == DeliveryIntent.Reliable)
                    m_ObjectSender(state.Serialize(), deliveryIntent);

                if (state.RequireRemoving)
                    RemoveObject(UID);
            });
        }
        #endregion
    }

    protected override void OnBeforeExecuteView()
    {
        base.OnBeforeExecuteView();

        var elementsCount = c_HistoryBufferSecondsLength / c_MinUpdateInteraval;
        m_HistoryBuffer.AddLast(ToSnapshot());
        if (m_HistoryBuffer.Count > elementsCount)
            m_HistoryBuffer.RemoveFirst();
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

        Debug.LogError($"Destroy world {WorldView.gameObject}");

        m_RemoteObjectUpdateRequests.Clear();
        m_RemoteObjectUpdateRequests = null;

        m_ObjectsRegisterRequests.Clear();
        m_ObjectsRegisterRequests = null;

        m_HistoryBuffer.Clear();
        m_HistoryBuffer = null;

        m_InternalNetwork = null;
    }

    /// Someone connected and loaded game scene, schedule local objects for reliable update update;
    public void OnRemotePlayerLoadedGameScene(ClientUID peerId) =>
        WorldView.OnRemotePlayerBecomesReady(peerId);

    public void OnPlayerBecomesReady(INetRTPlayer player, bool isLocal) =>
        Players.Register(player.UID, player.Name, isLocal);

    /// Someone disconnected.
    public void OnDisconnect(ClientUID uid) =>
        Players.Unregister(uid);

    protected void ForEachRemoteRequests(Action<NetObjectUID, ObjectUpdateSnapshot> processor)
    {
        foreach (var UID in RemoteObjectUpdateRequests.Keys.ToArray())
            processor(UID, RemoteObjectUpdateRequests[UID]);
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
        var state = new NetObjectState(new NetObjectUID(owner.UID), prefabId, isSceneObject, owner.UID, Players.Find);
        m_ObjectsRegisterRequests.Add(state.UID, state);
        return WorldView.CreateImmidiate(state, position, rotation, additionalData);      
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
        m_ObjectsRegisterRequests.Add(newState.UID, newState);

        //Initialize immidiatlty on master client;
        if (m_InternalNetwork.IsMasterClient)
            WorldView.InitializeView(sceneIdentity.Identity, newState, m_InternalNetwork, null);

        //Add view to be used in future;
        WorldView.RegisterAsPending(newState.UID, sceneIdentity.Identity);
    }

    /// <summary>
    /// Find game object reference by id.
    /// Can be NULL.
    /// </summary>
    /// <param name="instanceId"></param>
    /// <returns></returns>
    public NetObjectIdentity FindObject(NetObjectUID instanceId) =>
        WorldView.CurrentObjectViews.TryGetValue(instanceId, out NetObjectIdentity identity) ? identity : null;

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
            m_ObjectsRegisterRequests.Add(instanceId, state);
            return WorldView.CreateImmidiate(state, m_InternalNetwork, position, rotation, null);
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
        WorldView.NewObjectInitialized += OnNewObjectInitialized;

        void OnNewObjectInitialized(NetObjectIdentity o)
        {
            if (o == null || o.InstanceId != instanceId)
                return;

            result(o);
            WorldView.NewObjectInitialized -= OnNewObjectInitialized;
        }
    }

    public string ToJSONProperty() => RealtimeJSONUtils.ToJSONCollection(WorldView.CurrentObjectViews.Values.Select(o => o.ToJSONProperty()).ToArray());
}
