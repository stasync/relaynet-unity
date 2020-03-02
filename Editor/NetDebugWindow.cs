using UnityEngine;
using UnityEditor;

public class NetWorldDebugWindow : EditorWindow
{
    ServiceProvider<GSNetwork> m_NetworkProvider;

    [MenuItem("Network/World debug")]
    static void Init()
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

        var world = m_NetworkProvider.Instance.Realtime.CurrentWorld;
        if(world == null)
        {
            EditorGUILayout.HelpBox("World not represented yet", MessageType.Error);
            return;
        }

        //Players
        {
            EditorGUILayout.LabelField("Players: ", EditorStyles.boldLabel);
            var players = world.Players;
            DrawPlayer("Local: ", players.LocalPlayer);
            DrawPlayer("Master: ", players.MasterPlayer);

            EditorGUILayout.Space();
            foreach (var uid in players.PlayerUIDs)
                DrawPlayer(string.Empty, players.Find(uid));
        }

        //Obejcts
        {
            EditorGUILayout.Space();
            var objStates = world.ObjectStates;
            EditorGUILayout.LabelField($"Objects: {objStates.Count}", EditorStyles.boldLabel);
            foreach(var obj in objStates)
                EditorGUILayout.LabelField(obj.Value.ToString());
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
            EditorGUILayout.Space();
            var remoteRequests = world.RemoteObjectUpdateRequests;
            EditorGUILayout.LabelField($"Objects remote request: {remoteRequests.Count}", EditorStyles.boldLabel);
            foreach (var obj in remoteRequests)
                EditorGUILayout.LabelField(obj.Value.ToString());
        }
    }

    void DrawPlayer(string prefix, NetWorldPlayer p) => EditorGUILayout.LabelField($"{prefix}{(p == null ? "null" : p.ToString())}");
}