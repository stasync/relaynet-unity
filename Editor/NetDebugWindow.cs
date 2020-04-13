using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class NetWorldDebugWindow : EditorWindow
{
    class ObjectViewDrawer
    {
        readonly NetObjectState m_State;
        bool m_Show;

        public ObjectViewDrawer(NetObjectState objectState)
        {
            m_State = objectState;
        }
        public void Draw()
        {
            m_Show = EditorGUILayout.Foldout(m_Show, m_State.ToString());
            if(m_Show)
                EditorGUILayout.HelpBox("TODO: Some info here", MessageType.None);
        }
    }

    bool m_ShowHistoryBuffer;
    ServiceProvider<GSNetwork> m_NetworkProvider;
    Dictionary<NetObjectUID, ObjectViewDrawer> m_ObjectViews;

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
                foreach(var s in world.HistroyBuffer)
                    EditorGUILayout.LabelField($"{s.DeltaTime} size: {s.GetSize()} obj: {s.Objects.Count} pl: {s.Players.PlayersCollection.Count}", EditorStyles.boldLabel);

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
            foreach (var UID in m_ObjectViews.Keys)
                if (!world.ObjectExists(UID))
                    m_ObjectViews.Remove(UID);

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
    }

    void DrawPlayer(string prefix, NetWorldPlayer p) => EditorGUILayout.LabelField($"{prefix}{(p == null ? "null" : p.ToString())}");
}