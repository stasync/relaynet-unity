using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class NetDebugWindow : EditorWindow
{
    #region Drawers
    class ObjectDrawer
    {
        readonly NetObjectState m_State;
        readonly NetWorld m_World;

        bool m_Draw;

        public void SetDrawingEnabled(bool value) => m_Draw = value;

        public ObjectDrawer(NetObjectState objectState, NetWorld world)
        {
            m_State = objectState;
            m_World = world;
        }

        public void Draw()
        {
            m_Draw = EditorGUILayout.Foldout(m_Draw, $"UID: {m_State.UID} Prefab: {m_State.PrefabId}");
            if (!m_Draw)
                return;

            EditorGUI.indentLevel++;
            {
                EditorGUILayout.LabelField($"UID: {m_State.UID} Scene: {m_State.IsSceneObject} Prefab: {m_State.PrefabId}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Chanage type: {m_State.StateChangeType}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Owner: {m_State.Owner.ToString()}");
                EditorGUILayout.LabelField($"Local: {m_State.IsOwnedByLocalPlayer}");
                EditorGUILayout.LabelField($"Master: {m_State.IsOwnedByMasterClient}");
                EditorGUILayout.LabelField($"Data size: {m_State.ViewData.Length}");

                var temporalBuffer = m_State.RPCBuffer.TemporalBuffer;
                var persistentBuffer = m_State.RPCBuffer.PersistentBuffer;

                EditorGUILayout.LabelField($"RPC: Temporal: {temporalBuffer.Count}", EditorStyles.boldLabel);
                {
                    EditorGUI.indentLevel++;
                    foreach (var rpc in temporalBuffer)
                        DrawRPC(rpc);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.LabelField($"RPC: Pesistent: {persistentBuffer.Count}", EditorStyles.boldLabel);
                {
                    EditorGUI.indentLevel++;
                    foreach (var rpc in persistentBuffer)
                        DrawRPC(rpc);
                    EditorGUI.indentLevel--;
                }

                var properties = m_State.Properties.Data;
                EditorGUILayout.LabelField($"Properties: {properties.Count}", EditorStyles.boldLabel);
                {
                    EditorGUI.indentLevel++;
                    foreach (var kvp in properties)
                        EditorGUILayout.LabelField($"{kvp.Key} Size: {kvp.Value.Length}");
                    EditorGUI.indentLevel--;
                }

                if (m_World.View != null)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.ObjectField("View", m_World.FindObjectView(m_State.UID), typeof(GameObject), true);
                    EditorGUI.EndDisabledGroup();
                }
            }
            EditorGUI.indentLevel--;
        }

        void DrawRPC(RPCState rpc) =>
            EditorGUILayout.LabelField($"{rpc.UID} Hash: {rpc.Hash} '{RPCUtils.TryFindStringByHash(rpc.Hash)}' Payload size: {rpc.Payload.Length}");
    }

    abstract class WorldDrawer<T> where T : NetWorld
    {
        protected readonly T m_World;

        public bool IsValid => m_World != null && m_World.IsValid;

        bool m_Draw;
        Dictionary<NetObjectUID, ObjectDrawer> m_ObjectViews;
        string m_LastJSONState;

        public WorldDrawer(T world)
        {
            m_World = world;
            m_Draw = true;
        }

        public void Draw()
        {
            if(!IsValid)
            {
                EditorGUILayout.HelpBox("World is invalid", MessageType.Error);
                return;
            }

            m_Draw = EditorGUILayout.Foldout(m_Draw, m_World.Name);
            if (!m_Draw)
                return;

            if (m_ObjectViews == null)
                m_ObjectViews = new Dictionary<NetObjectUID, ObjectDrawer>();

            EditorGUI.indentLevel++;
            OnDrawWorld();
            EditorGUI.indentLevel--;
        }

        protected virtual void OnDrawWorld()
        {
            EditorGUILayout.LabelField($"Time: {m_World.Time.ToString("f1")}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Local Time: {m_World.LocalTime.ToString("f1")}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Hash: {m_World.GetHashCode()}", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            //JSON State
            {
                EditorGUILayout.Space();
                if (GUILayout.Button("Aggregate JSON state"))
                    m_LastJSONState = m_World.ToJSONProperty();
                EditorGUILayout.TextField(m_LastJSONState);
            }

            EditorGUILayout.Space();

            //Players
            {
                EditorGUILayout.LabelField("Players: ", EditorStyles.boldLabel);
                var players = m_World.Players;
                DrawPlayer("Local: ", players.LocalPlayer);
                DrawPlayer("Master: ", players.MasterPlayer);

                EditorGUILayout.Space();
                foreach (var uid in players.AllUIDs)
                    DrawPlayer(string.Empty, players.Find(uid));
            }

            //Obejcts
            {
                foreach (var UID in m_ObjectViews.Keys.ToList())
                    if (!m_World.ObjectExists(UID))
                        m_ObjectViews.Remove(UID);

                // Expand/Collapse all objects;
                {
                    EditorGUILayout.BeginHorizontal();

                    if (GUILayout.Button("Collapse all"))
                        foreach (var d in m_ObjectViews.Values)
                            d.SetDrawingEnabled(false);

                    if (GUILayout.Button("Expand all"))
                        foreach (var d in m_ObjectViews.Values)
                            d.SetDrawingEnabled(true);

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.Space();
                var objStates = m_World.ObjectStates;
                EditorGUILayout.LabelField($"Objects: {objStates.Count}", EditorStyles.boldLabel);
                foreach (var kvp in objStates)
                {
                    if (!m_ObjectViews.TryGetValue(kvp.Key, out var drawer))
                    {
                        drawer = new ObjectDrawer(kvp.Value, m_World);
                        m_ObjectViews.Add(kvp.Key, drawer);
                    }
                    drawer.Draw();
                }
            }
        }
        void DrawPlayer(string prefix, NetWorldPlayer p) => EditorGUILayout.LabelField($"{prefix}{(p == null ? "null" : p.ToString())}");
    }

    class BaseWorldDrawer : WorldDrawer<NetWorld>
    {
        public BaseWorldDrawer(NetWorld world) : base(world) { }
    }

    class MainWorldDrawer : WorldDrawer<NetWorldRuntime>
    {
        bool m_DrawCommon;
        bool m_DrawPendingViews;

        public MainWorldDrawer(NetWorldRuntime runtimeWorld) : base(runtimeWorld) { }

        protected override void OnDrawWorld()
        {
            base.OnDrawWorld();

            EditorGUILayout.Space();
            m_DrawCommon = EditorGUILayout.Foldout(m_DrawCommon, "Common");
            if (m_DrawCommon)
            {
                EditorGUI.indentLevel++;
                {
                    //Register requests
                    {
                        EditorGUILayout.Space();
                        var registerRequests = m_World.ObjectsRegisterRequests;
                        EditorGUILayout.LabelField($"Objects register request: {registerRequests.Count}", EditorStyles.boldLabel);
                        foreach (var obj in registerRequests)
                            EditorGUILayout.LabelField(obj.Value.ToString());
                    }

                    //Remote udpate requests
                    {
                        EditorGUILayout.Space();
                        var remoteRequests = m_World.RemoteObjectUpdateRequests;
                        EditorGUILayout.LabelField($"Objects remote request: {remoteRequests.Count}", EditorStyles.boldLabel);
                        foreach (var obj in remoteRequests)
                            EditorGUILayout.LabelField(obj.Value.ToString());
                    }

                    //Outgoing messages
                    {
                        EditorGUILayout.Space();
                        EditorGUILayout.LabelField($"Outgoing world message count: {m_World.OutgoingMessageQueueCount}", EditorStyles.boldLabel);
                    }
                }
                EditorGUI.indentLevel--;
            }

            if(m_World.View != null)
            {
                EditorGUILayout.Space();
                m_DrawPendingViews = EditorGUILayout.Foldout(m_DrawPendingViews, $"Pending Views: {m_World.View.PendingObjectViews.Count}") && m_World.View.PendingObjectViews.Count > 0;
                if (m_DrawPendingViews)
                {
                    EditorGUI.indentLevel++;
                    {
                        foreach (var kvp in m_World.View.PendingObjectViews)
                            EditorGUILayout.ObjectField($"UID: {kvp.Key}", kvp.Value, typeof(NetObjectIdentity), true);
                    }
                    EditorGUI.indentLevel--;
                }
            }
        }
    }

    class ReplicaWorldDrawer : WorldDrawer<NetWorldEvaluator>
    {
        public ReplicaWorldDrawer(NetWorldEvaluator replicaWorld) : base(replicaWorld) { }

        protected override void OnDrawWorld()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(m_World.Paused ? "Play >" : "Pause ||"))
                m_World.SetPaused(!m_World.Paused);

            EditorGUI.BeginDisabledGroup(!m_World.Paused);

            if (GUILayout.Button("<< Previous"))
                m_World.MoveToPreviousSnapshot(true);

            if (GUILayout.Button("Next >>"))
                m_World.MoveToNextSnapshot(true);

            EditorGUILayout.EndHorizontal();
            EditorGUI.BeginChangeCheck();
            var targetSnapshot = EditorGUILayout.IntSlider(m_World.CurrentSnapshotIndex, 0, m_World.BufferCount - 1);
            if (EditorGUI.EndChangeCheck() && targetSnapshot != m_World.CurrentSnapshotIndex)
                m_World.MoveToSnapshotSafe(targetSnapshot, true);

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();

            base.OnDrawWorld();
        }
    }
    #endregion

    bool m_ShowNetworkStatistics;
    bool m_ShowMainWorldHistoryInfo;
    bool m_ShowMainWorldBuffer;
    ServiceProvider<GSNetwork> m_NetworkProvider;
    Vector2 m_GlobalScrollPosition;

    MainWorldDrawer m_MainWorldDrawer;
    ReplicaWorldDrawer m_CurrentWorldReplicaDrawer;

    [MenuItem("System/Network/Debug Window")]
#pragma warning disable IDE0051 // Удалите неиспользуемые закрытые члены
    static void Init()
#pragma warning restore IDE0051 // Удалите неиспользуемые закрытые члены
    {
        var window = GetWindow<NetDebugWindow>("Network Debug Info");
        window.Show();
        window.m_ShowMainWorldHistoryInfo = true;
    }

    void Update()
    {
        Repaint();
    }

    void OnGUI()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox("Avaliable only in play mode", MessageType.Error);
            return;
        }

        if (m_NetworkProvider == null)
            m_NetworkProvider = new ServiceProvider<GSNetwork>();

        if (m_NetworkProvider.Instance == null)
            return;

        var realtime = m_NetworkProvider.Instance.Realtime;

        m_ShowNetworkStatistics = EditorGUILayout.Foldout(m_ShowNetworkStatistics, "Network Statistics");
        if(m_ShowNetworkStatistics)
        {
            var statistics = realtime.CurrentClientStatistics;
            if (statistics != null)
                EditorGUILayout.HelpBox(statistics.ToString(), MessageType.None);
            else
                EditorGUILayout.HelpBox("Statistics is not avaliable", MessageType.Error);
        }
        EditorGUILayout.Space();

        var mainWorld = realtime.MainWorld;
        if (mainWorld == null)
        {
            EditorGUILayout.HelpBox("World not represented yet", MessageType.Error);
            return;
        }

        if (m_MainWorldDrawer == null || !m_MainWorldDrawer.IsValid)
            m_MainWorldDrawer = new MainWorldDrawer(mainWorld);

        EditorGUILayout.Space();
        m_GlobalScrollPosition = EditorGUILayout.BeginScrollView(m_GlobalScrollPosition);

        //Hitory
        m_ShowMainWorldHistoryInfo = EditorGUILayout.Foldout(m_ShowMainWorldHistoryInfo, "Main World History Info");
        if (m_ShowMainWorldHistoryInfo)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"History size: {mainWorld.GetHistoryBufferSize()} bytes, {mainWorld.HistroyBuffer.Count} frames", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(m_CurrentWorldReplicaDrawer != null);
            if (GUILayout.Button("Playback"))
            {
                var currentReplica = mainWorld.Replicate();
                m_CurrentWorldReplicaDrawer = new ReplicaWorldDrawer(currentReplica);
                var unityService = new ServiceProvider<IUnityService>();
                unityService.Instance.UnityFixedUpdate += OnFixedUpdate;

                void OnFixedUpdate()
                {
                    currentReplica.Update(Time.fixedDeltaTime);
                    if (!currentReplica.IsValid)
                    {
                        m_CurrentWorldReplicaDrawer = null;
                        unityService.Instance.UnityFixedUpdate -= OnFixedUpdate;
                    }
                }
            }
            EditorGUI.EndDisabledGroup();

            m_ShowMainWorldBuffer = EditorGUILayout.Foldout(m_ShowMainWorldBuffer, "Show buffer");
            if (m_ShowMainWorldBuffer)
            {
                EditorGUI.indentLevel++;
                foreach (var s in mainWorld.HistroyBuffer)
                    EditorGUILayout.LabelField($"t:{s.Time.ToString("f2")} dt:{s.DeltaTime.ToString("f2")} size: {s.GetSize()} obj: {s.Objects.Count} pl: {s.Players.PlayersCollection.Count}", EditorStyles.boldLabel);
                EditorGUI.indentLevel--;
            }
        }

        EditorGUILayout.Space();
        m_MainWorldDrawer.Draw();

        if(m_CurrentWorldReplicaDrawer != null)
        {
            if(m_CurrentWorldReplicaDrawer.IsValid)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Replica info:", EditorStyles.boldLabel);
                m_CurrentWorldReplicaDrawer.Draw();
            }
            else
                m_CurrentWorldReplicaDrawer = null;
        }

        EditorGUILayout.EndScrollView();
    }
}