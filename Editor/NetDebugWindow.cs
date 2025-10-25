using System.Collections.Generic;
using System.Linq;
using Core;
using Core.DependencyInjection.Services;
using LROTech.Network.State;
using UnityEditor;
using UnityEngine;

public class NetDebugWindow : EditorWindow
{
    #region Drawers
    private class ObjectDrawer
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
            m_Draw = EditorGUILayout.Foldout(m_Draw, $"Uid: {m_State.UID} Prefab: {m_State.PrefabId}");
            if (!m_Draw)
                return;

            EditorGUI.indentLevel++;
            {
                EditorGUILayout.LabelField($"Uid: {m_State.UID} Scene: {m_State.IsSceneObject} Prefab: {m_State.PrefabId}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Change type: {m_State.StateChangeType}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Owner: {m_State.Owner}");
                EditorGUILayout.LabelField($"Local: {m_State.IsOwnedByLocalPlayer}");
                EditorGUILayout.LabelField($"Master: {m_State.IsOwnedByMasterClient}");

                EditorGUILayout.LabelField($"Total size: {m_State.Serialize().Length}");

                if (m_State.DataCollection.TryGetData<NetObjectViewStateData>(out var viewData))
                    EditorGUILayout.LabelField($"View data size: {viewData.UnstructuredData.Length}");

                var temporalBuffer = m_State.RPCBuffer.TemporalBuffer;
                var persistentBuffer = m_State.RPCBuffer.PersistentBuffer;

                EditorGUILayout.LabelField($"RPC: Temporal: {temporalBuffer.Count}", EditorStyles.boldLabel);
                {
                    EditorGUI.indentLevel++;
                    foreach (var rpc in temporalBuffer)
                        DrawRPC(rpc);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.LabelField($"RPC: Persistent: {persistentBuffer.Count}", EditorStyles.boldLabel);
                {
                    EditorGUI.indentLevel++;
                    foreach (var rpc in persistentBuffer)
                        DrawRPC(rpc);
                    EditorGUI.indentLevel--;
                }

                var dataCollection = m_State.DataCollection;
                EditorGUILayout.LabelField($"Data: {dataCollection.Count}", EditorStyles.boldLabel);
                {
                    EditorGUI.indentLevel++;
                    foreach (var data in dataCollection)
                    {
                        EditorGUILayout.LabelField($"{data.GetType().FullName}  Size: {data.GetSerializedSize_Editor()}  Type: {data.TypeHashCode}:", EditorStyles.boldLabel);
                        EditorGUI.indentLevel++;
                        foreach(var dataValueDefinition in data.GetValuesInfo_Editor())
                            EditorGUILayout.LabelField(dataValueDefinition);
                        EditorGUI.indentLevel--;
                    }
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

        private void DrawRPC(RPCState rpc)
        {
            var handlerMethod = RPCUtils.TryFindMethodInfoByHashSlow(rpc.Hash);
            var handlerInfo = handlerMethod == null
                ? "UNKNOWN"
                : $"{handlerMethod.DeclaringType!.FullName}.{handlerMethod.Name}";
            
            EditorGUILayout.LabelField($"{rpc.UID} Hash: {rpc.Hash} '{handlerInfo}' Payload size: {rpc.Payload.Length}");
        }
    }

    private abstract class WorldDrawer<T> where T : NetWorld
    {
        protected readonly T m_World;

        public bool IsValid => m_World != null && m_World.IsValid;

        bool m_Draw;
        Dictionary<NetObjectUID, ObjectDrawer> m_ObjectViews;

        protected WorldDrawer(T world)
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

            m_ObjectViews ??= new Dictionary<NetObjectUID, ObjectDrawer>();

            EditorGUI.indentLevel++;
            OnDrawWorld();
            EditorGUI.indentLevel--;
        }

        protected virtual void OnDrawWorld()
        {
            EditorGUILayout.LabelField($"Time: {m_World.Time:f1}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Local Time: {m_World.LocalTime:f1}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Hash: {m_World.GetHashCode()}", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            //Players
            {
                EditorGUILayout.LabelField("Players: ", EditorStyles.boldLabel);
                var players = m_World.Players;
                DrawPlayer("Local: ", players.LocalPlayer);
                DrawPlayer("Master: ", players.MasterPlayer);

                EditorGUILayout.Space();
                foreach (var worldPlayer in players.All)
                    DrawPlayer(string.Empty, worldPlayer);
            }

            //Obejcts
            {
                foreach (var UID in m_ObjectViews.Keys.ToList())
                {
                    if (!m_World.ObjectExists(UID))
                        m_ObjectViews.Remove(UID);
                }

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
        
        private void DrawPlayer(string prefix, NetWorldPlayer p) => 
            EditorGUILayout.LabelField($"{prefix}{(p == null ? "null" : p.ToString())}");
    }

    private class BaseWorldDrawer : WorldDrawer<NetWorld>
    {
        public BaseWorldDrawer(NetWorld world) : base(world) { }
    }

    private class MainWorldDrawer : WorldDrawer<NetWorldRuntime>
    {
        private bool m_DrawCommon;
        private bool m_DrawPendingViews;

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
                    // Register requests.
                    {
                        EditorGUILayout.Space();
                        var registerRequests = m_World.IncomingStateChangeRequests;
                        EditorGUILayout.LabelField($"Remote Update Requests: {registerRequests.Count}", EditorStyles.boldLabel);
                        
                        EditorGUI.indentLevel++;
                        foreach (var request in registerRequests)
                            EditorGUILayout.LabelField($"{request.OpCode} {request.Data}");
                        EditorGUI.indentLevel--;
                    }
                    
                    // Temporary Unregistered Objects.
                    {
                        EditorGUILayout.Space();
                        var temporaryUnregisteredObjects = m_World.TemporaryUnregisteredObjects;
                        EditorGUILayout.LabelField($"Unregistered Objects: {temporaryUnregisteredObjects.Count}", EditorStyles.boldLabel);
                        
                        EditorGUI.indentLevel++;
                        foreach (var objectState in temporaryUnregisteredObjects.Values)
                            EditorGUILayout.LabelField($"[{objectState.UID}] [{objectState.PrefabId}] Owner: {objectState.Owner} Scene: {objectState.IsSceneObject}");
                        EditorGUI.indentLevel--;
                    }
                }
                EditorGUI.indentLevel--;
            }

            if (m_World.View != null)
            {
                EditorGUILayout.Space();
                m_DrawPendingViews = EditorGUILayout.Foldout(m_DrawPendingViews, $"Pending Views: {m_World.View.TemporaryUnregisteredViews.Count}") && m_World.View.TemporaryUnregisteredViews.Count > 0;
                if (m_DrawPendingViews)
                {
                    EditorGUI.indentLevel++;
                    {
                        foreach (var kvp in m_World.View.TemporaryUnregisteredViews)
                            EditorGUILayout.ObjectField($"Uid: {kvp.Key}", kvp.Value, typeof(NetObjectIdentity), allowSceneObjects: true);
                    }
                    EditorGUI.indentLevel--;
                }
            }
        }
    }

    private class ReplicaWorldDrawer : WorldDrawer<NetWorldEvaluator>
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

    private bool m_ShowNetworkStatistics;
    private bool m_ShowMainWorldHistoryInfo;
    private bool m_ShowMainWorldBuffer;
    private ServiceProvider<GSNetwork> m_NetworkProvider;
    private Vector2 m_GlobalScrollPosition;

    private MainWorldDrawer m_MainWorldDrawer;
    private ReplicaWorldDrawer m_CurrentWorldReplicaDrawer;

    [MenuItem("System/Network/Debug Window")]
    private static void Init()
    {
        var window = GetWindow<NetDebugWindow>("Network Debug Info");
        window.Show();
        window.m_ShowMainWorldHistoryInfo = true;
    }
    
    private void Update()
    {
        Repaint();
    }

    private void OnGUI()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox("Available only in play mode", MessageType.Error);
            return;
        }

        m_NetworkProvider ??= new ServiceProvider<GSNetwork>();
        if (m_NetworkProvider.Instance == null)
            return;

        var realtime = m_NetworkProvider.Instance.Realtime;
        EditorGUILayout.LabelField($"RT status: {realtime.IsSessionRunningAndReady}", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"RT conn: {realtime.LocalConnectionUID}", EditorStyles.boldLabel);

        EditorGUILayout.Space();

        m_ShowNetworkStatistics = EditorGUILayout.Foldout(m_ShowNetworkStatistics, "Network Statistics");
        if (m_ShowNetworkStatistics)
        {
            var statistics = realtime.CurrentClientStatistics;
            if (statistics != null)
                EditorGUILayout.HelpBox(statistics.ToString(), MessageType.None);
            else
                EditorGUILayout.HelpBox("Statistics is not available", MessageType.Error);
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

        // History.
        m_ShowMainWorldHistoryInfo = EditorGUILayout.Foldout(m_ShowMainWorldHistoryInfo, "Main World History Info");
        if (m_ShowMainWorldHistoryInfo)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"History size: {mainWorld.HistoryBuffer.Size} ({mainWorld.HistoryBuffer.SizeMB:f2}mb) [{mainWorld.HistoryBuffer.AverageFrameSize}] bytes, cnt: {mainWorld.HistoryBuffer.Count} frames, time: {mainWorld.HistoryBuffer.LengthInSeconds:f2} s", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(m_CurrentWorldReplicaDrawer != null);
            if (GUILayout.Button("Playback"))
            {
                var currentReplica = mainWorld.Replicate();
                m_CurrentWorldReplicaDrawer = new ReplicaWorldDrawer(currentReplica);
                Bootstrap.UnityFixedUpdate += OnFixedUpdate;
                
                void OnFixedUpdate()
                {
                    currentReplica.Update(Time.fixedDeltaTime);
                    if (!currentReplica.IsValid)
                    {
                        m_CurrentWorldReplicaDrawer = null;
                        Bootstrap.UnityFixedUpdate -= OnFixedUpdate;
                    }
                }
            }
            EditorGUI.EndDisabledGroup();

            m_ShowMainWorldBuffer = EditorGUILayout.Foldout(m_ShowMainWorldBuffer, "Show buffer");
            if (m_ShowMainWorldBuffer)
            {
                EditorGUI.indentLevel++;
                foreach (var netWorldSnapshot in mainWorld.HistoryBuffer)
                    EditorGUILayout.LabelField($"t:{netWorldSnapshot.Time:f2} dt:{netWorldSnapshot.DeltaTime:f2} size: {netWorldSnapshot.GetSize()} obj: {netWorldSnapshot.Objects.Count} pl: {netWorldSnapshot.Players.PlayersCollection.Count}", EditorStyles.boldLabel);
                EditorGUI.indentLevel--;
            }
        }

        EditorGUILayout.Space();
        m_MainWorldDrawer.Draw();

        if (m_CurrentWorldReplicaDrawer != null)
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