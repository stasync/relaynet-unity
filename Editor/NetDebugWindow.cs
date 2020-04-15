using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class NetWorldDebugWindow : EditorWindow
{
    class ObjectViewDrawer
    {
        readonly NetObjectState m_State;
        bool m_Draw;

        public void SetDrawingEnabled(bool value) => m_Draw = value;

        public ObjectViewDrawer(NetObjectState objectState)
        {
            m_State = objectState;
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
            }
            EditorGUI.indentLevel--;
        }

        void DrawRPC(RPCState rpc) =>
            EditorGUILayout.LabelField($"{rpc.UID} Hash: {rpc.Hash} '{RPCUtils.TryFindStringByHash(rpc.Hash)}' Payload size: {rpc.Payload.Length}");
    }

    bool m_ShowHistoryBuffer;
    ServiceProvider<GSNetwork> m_NetworkProvider;
    Dictionary<NetObjectUID, ObjectViewDrawer> m_ObjectViews;
    Vector2 m_GlobalScrollPosition;
    string m_LastJSONState;


    [MenuItem("Network/World debug")]
#pragma warning disable IDE0051 // Удалите неиспользуемые закрытые члены
    static void Init()
#pragma warning restore IDE0051 // Удалите неиспользуемые закрытые члены
    {
        var window = GetWindow<NetWorldDebugWindow>("World info");
        window.Show();
    }

    void Update()
    {
        Repaint();
    }

    void OnGUI()
    {
        if(!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox("Avaliable only in play mode", MessageType.Error);
            return;
        }

        if (m_NetworkProvider == null)
            m_NetworkProvider = new ServiceProvider<GSNetwork>();

        if (m_NetworkProvider.Instance == null)
            return;

        if (m_ObjectViews == null)
            m_ObjectViews = new Dictionary<NetObjectUID, ObjectViewDrawer>();

        var world = m_NetworkProvider.Instance.Realtime.MainWorld;
        if(world == null)
        {
            EditorGUILayout.HelpBox("World not represented yet", MessageType.Error);
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"World hash: {world.GetHashCode()}", EditorStyles.boldLabel);
        //JSON State
        {
            EditorGUILayout.Space();
            if (GUILayout.Button("Aggregate JSON state"))
                m_LastJSONState = world.ToJSONProperty();
            EditorGUILayout.TextField(m_LastJSONState);
        }

        EditorGUILayout.Space();
        m_GlobalScrollPosition = EditorGUILayout.BeginScrollView(m_GlobalScrollPosition);
        //Hitory
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"History size: {world.GetHistoryBufferSize()} bytes, {world.HistroyBuffer.Count} frames", EditorStyles.boldLabel);

            if (GUILayout.Button("Playback"))
            {
                var replica = m_NetworkProvider.Instance.Realtime.MainWorld.Replicate();
                var unityService = new ServiceProvider<IUnityService>();
                unityService.Instance.UnityFixedUpdate += OnFixedUpdate;

                void OnFixedUpdate()
                {
                    replica.FixedUpdate();
                    if (!replica.IsValid)
                        unityService.Instance.UnityFixedUpdate -= OnFixedUpdate;
                }
            }

            m_ShowHistoryBuffer = EditorGUILayout.Foldout(m_ShowHistoryBuffer, "Show buffer");
            if(m_ShowHistoryBuffer)
            {
                EditorGUI.indentLevel++;
                foreach (var s in world.HistroyBuffer)
                    EditorGUILayout.LabelField($"{s.DeltaTime} size: {s.GetSize()} obj: {s.Objects.Count} pl: {s.Players.PlayersCollection.Count}", EditorStyles.boldLabel);
                EditorGUI.indentLevel--;
            }
        }

        //Players
        {
            EditorGUILayout.LabelField("Players: ", EditorStyles.boldLabel);
            var players = world.Players;
            DrawPlayer("Local: ", players.LocalPlayer);
            DrawPlayer("Master: ", players.MasterPlayer);

            EditorGUILayout.Space();
            foreach (var uid in players.AllUIDs)
                DrawPlayer(string.Empty, players.Find(uid));
        }

        //Obejcts
        {
            foreach (var UID in m_ObjectViews.Keys.ToList())
                if (!world.ObjectExists(UID))
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
            var objStates = world.ObjectStates;
            EditorGUILayout.LabelField($"Objects: {objStates.Count}", EditorStyles.boldLabel);
            foreach(var kvp in objStates)
            {
                if(!m_ObjectViews.TryGetValue(kvp.Key, out var drawer))
                {
                    drawer = new ObjectViewDrawer(kvp.Value);
                    m_ObjectViews.Add(kvp.Key, drawer);
                }
                drawer.Draw();
            }
        }

        //Register equests
        {
            EditorGUILayout.Space();
            var registerRequests = world.ObjectsRegisterRequests;
            EditorGUILayout.LabelField($"Objects register request: {registerRequests.Count}", EditorStyles.boldLabel);
            foreach (var obj in registerRequests)
                EditorGUILayout.LabelField(obj.Value.ToString());
        }

        //Remote udpate requests
        {
            EditorGUILayout.Separator();
            EditorGUILayout.Space();
            var remoteRequests = world.RemoteObjectUpdateRequests;
            EditorGUILayout.LabelField($"Objects remote request: {remoteRequests.Count}", EditorStyles.boldLabel);
            foreach (var obj in remoteRequests)
                EditorGUILayout.LabelField(obj.Value.ToString());
        }

        EditorGUILayout.EndScrollView();
    }

    void DrawPlayer(string prefix, NetWorldPlayer p) => EditorGUILayout.LabelField($"{prefix}{(p == null ? "null" : p.ToString())}");
}