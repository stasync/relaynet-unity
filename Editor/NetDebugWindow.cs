using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class NetWorldDebugWindow : EditorWindow
{
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
                EditorGUILayout.LabelField($"Data size: {m_State.Data.Length}");

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

                if(m_World.View != null)
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

    class WorldDrawer
    {
        readonly NetWorld m_World;

        bool m_Draw;
        Dictionary<NetObjectUID, ObjectDrawer> m_ObjectViews;
        string m_LastJSONState;

        public WorldDrawer(NetWorld world)
        {
            m_World = world;
            m_Draw = true;
        }

        public void Draw()
        {
            m_Draw = EditorGUILayout.Foldout(m_Draw, m_World.Name);
            if (!m_Draw)
                return;

            if (m_ObjectViews == null)
                m_ObjectViews = new Dictionary<NetObjectUID, ObjectDrawer>();

            EditorGUI.indentLevel++;
            DrawWorld();
            EditorGUI.indentLevel--;
        }

        void DrawWorld()
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

            if (m_World is NetWorldRuntime realtimeWorld)
            {
                //Register equests
                {
                    EditorGUILayout.Space();
                    var registerRequests = realtimeWorld.ObjectsRegisterRequests;
                    EditorGUILayout.LabelField($"Objects register request: {registerRequests.Count}", EditorStyles.boldLabel);
                    foreach (var obj in registerRequests)
                        EditorGUILayout.LabelField(obj.Value.ToString());
                }

                //Remote udpate requests
                {
                    EditorGUILayout.Separator();
                    EditorGUILayout.Space();
                    var remoteRequests = realtimeWorld.RemoteObjectUpdateRequests;
                    EditorGUILayout.LabelField($"Objects remote request: {remoteRequests.Count}", EditorStyles.boldLabel);
                    foreach (var obj in remoteRequests)
                        EditorGUILayout.LabelField(obj.Value.ToString());
                }
            }
        }
        void DrawPlayer(string prefix, NetWorldPlayer p) => EditorGUILayout.LabelField($"{prefix}{(p == null ? "null" : p.ToString())}");
    }

    bool m_ShowMainWorldHistoryInfo;
    bool m_ShowMainWorldBuffer;
    ServiceProvider<GSNetwork> m_NetworkProvider;
    Vector2 m_GlobalScrollPosition;

    WorldDrawer m_MainWorldDrawer;
    WorldDrawer m_CurrentWorldReplicaDrawer;

    [MenuItem("Network/World debug")]
#pragma warning disable IDE0051 // Удалите неиспользуемые закрытые члены
    static void Init()
#pragma warning restore IDE0051 // Удалите неиспользуемые закрытые члены
    {
        var window = GetWindow<NetWorldDebugWindow>("World info");
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

        var mainWorld = m_NetworkProvider.Instance.Realtime.MainWorld;
        if (mainWorld == null)
        {
            EditorGUILayout.HelpBox("World not represented yet", MessageType.Error);
            return;
        }

        if (m_MainWorldDrawer == null)
            m_MainWorldDrawer = new WorldDrawer(mainWorld);

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
                var currentReplica = m_NetworkProvider.Instance.Realtime.MainWorld.Replicate();
                m_CurrentWorldReplicaDrawer = new WorldDrawer(currentReplica);

                var unityService = new ServiceProvider<IUnityService>();
                unityService.Instance.UnityFixedUpdate += OnFixedUpdate;

                void OnFixedUpdate()
                {
                    currentReplica.FixedUpdate();
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
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Replica info:", EditorStyles.boldLabel);
            m_CurrentWorldReplicaDrawer.Draw();
        }

        EditorGUILayout.EndScrollView();
    }
}