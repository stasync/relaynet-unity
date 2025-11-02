using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Core;
using Core.Networking;
using LROTech.Network.GameObjects;
using LROTech.Network.StateManagement;
using UnityEditor;
using UnityEngine;

namespace LROTech.Network.Editor
{
    internal sealed class NetDebugWindow : EditorWindow
    {
        #region Drawers

        private class ObjectDrawer
        {
            private static readonly NetWriter s_networkStateSizeWriter = new();

            private readonly Dictionary<int, bool> _dataFoldoutStates = new();
            private readonly NetObjectState _state;
            private readonly NetWorld _world;

            private bool _draw;

            public void SetDrawingEnabled(bool value) => _draw = value;

            public ObjectDrawer(NetObjectState objectState, NetWorld world)
            {
                _state = objectState;
                _world = world;
            }

            public void Draw()
            {
                _draw = EditorGUILayout.Foldout(_draw, content: $"Uid: {_state.Uid} Prefab: {_state.PrefabId}");
                if (!_draw)
                    return;

                EditorGUI.indentLevel++;
                {
                    EditorGUILayout.LabelField($"Uid: {_state.Uid} Scene: {_state.IsSceneObject} Prefab: {_state.PrefabId}", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"Change Type: {_state.StateChangeType}", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"Owner: {_state.Owner} (Local: {_state.IsOwnedByLocalPlayer}, Master: {_state.IsOwnedByMasterClient})");

                    var stateSnapshot = _state.ToSnapshot();
                    EditorGUILayout.LabelField($"Total Size: {stateSnapshot.GetSize()}");

                    s_networkStateSizeWriter.SeekZero();
                    stateSnapshot.Serialize(s_networkStateSizeWriter);
                    EditorGUILayout.LabelField($"Network Size: {s_networkStateSizeWriter.Position}");

                    var temporalBuffer = _state.RpcBuffer.TemporalBuffer;
                    var persistentBuffer = _state.RpcBuffer.PersistentBuffer;

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

                    var dataCollection = _state.DataCollection;
                    EditorGUILayout.LabelField($"Data: {dataCollection.Count}", EditorStyles.boldLabel);
                    {
                        EditorGUI.indentLevel++;
                        foreach (var data in dataCollection)
                        {
                            if (!_dataFoldoutStates.TryGetValue(data.TypeHashCode, out var drawData))
                                _dataFoldoutStates.Add(data.TypeHashCode, drawData = false);

                            var dataSize = data.GetSerializedSize_Editor();

                            var color = GUI.color;
                            GUI.color = Color.Lerp(Color.green, Color.red, Mathf.Clamp01(dataSize / 200f));
                            drawData = EditorGUILayout.Foldout(drawData, content: $"{data.GetType().FullName}  Size: {dataSize}  Type: {data.TypeHashCode}");
                            _dataFoldoutStates[data.TypeHashCode] = drawData;
                            GUI.color = color;

                            if (drawData)
                            {
                                EditorGUI.indentLevel++;
                                foreach (var (fieldName, fieldValueString) in data.GetValuesInfo_Editor())
                                    EditorGUILayout.LabelField($"{ObjectNames.NicifyVariableName(fieldName)}: {fieldValueString}");
                                EditorGUI.indentLevel--;
                            }
                        }

                        EditorGUI.indentLevel--;
                    }

                    if (_world.View != null)
                    {
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.ObjectField(label: "View", _world.FindObjectView(_state.Uid), typeof(GameObject), allowSceneObjects: true);
                        EditorGUI.EndDisabledGroup();
                    }
                }
                EditorGUI.indentLevel--;
            }

            private void DrawRPC(NetObjectRpcState rpc)
            {
                var handlerMethod = NetObjectRpcUtils.TryFindMethodInfoByHashSlow(rpc.Hash);
                var handlerInfo = handlerMethod == null
                    ? "UNKNOWN"
                    : $"{handlerMethod.DeclaringType!.FullName}.{handlerMethod.Name}";

                EditorGUILayout.LabelField($"{rpc.Uid} Hash: {rpc.Hash} '{handlerInfo}' Payload Size: {rpc.Payload.Length}");
            }
        }

        private abstract class WorldDrawer<T> where T : NetWorld
        {
            protected readonly T World;

            public bool IsValid => World is { IsValid: true };

            bool _draw;
            Dictionary<NetObjectUid, ObjectDrawer> _objectViews;

            protected WorldDrawer(T world)
            {
                World = world;
                _draw = true;
            }

            public void Draw()
            {
                if (!IsValid)
                {
                    EditorGUILayout.HelpBox(message: "World is invalid", MessageType.Error);
                    return;
                }

                _draw = EditorGUILayout.Foldout(_draw, World.Name);
                if (!_draw)
                    return;

                _objectViews ??= new Dictionary<NetObjectUid, ObjectDrawer>();

                EditorGUI.indentLevel++;
                OnDrawWorld();
                EditorGUI.indentLevel--;
            }

            protected virtual void OnDrawWorld()
            {
                EditorGUILayout.LabelField($"Uid: {World.Uid}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Time: {World.Time:f1}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Local Time: {World.LocalTime:f1}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Hash: {World.GetVersionHash()}", EditorStyles.boldLabel);
                EditorGUILayout.Space();

                // Players.
                {
                    EditorGUILayout.LabelField("Players: ", EditorStyles.boldLabel);
                    var players = World.Players;
                    DrawPlayer(prefix: "Local: ", players.LocalPlayer);
                    DrawPlayer(prefix: "Master: ", players.MasterPlayer);

                    EditorGUILayout.Space();
                    foreach (var worldPlayer in players.All)
                        DrawPlayer(prefix: string.Empty, worldPlayer);
                }

                // Objects.
                {
                    foreach (var uid in _objectViews.Keys.ToList())
                    {
                        if (!World.ObjectExists(uid))
                            _objectViews.Remove(uid);
                    }

                    // Expand/Collapse all objects;
                    {
                        EditorGUILayout.BeginHorizontal();

                        if (GUILayout.Button("Collapse all"))
                            foreach (var drawer in _objectViews.Values)
                                drawer.SetDrawingEnabled(false);

                        if (GUILayout.Button("Expand all"))
                            foreach (var drawer in _objectViews.Values)
                                drawer.SetDrawingEnabled(true);

                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.Space();
                    var objStates = World.ObjectStates;
                    EditorGUILayout.LabelField($"Objects: {objStates.Count}", EditorStyles.boldLabel);
                    foreach (var (uid, netObject) in objStates)
                    {
                        if (!_objectViews.TryGetValue(uid, out var drawer))
                        {
                            drawer = new ObjectDrawer(netObject, World);
                            _objectViews.Add(uid, drawer);
                        }

                        drawer.Draw();
                    }
                }
            }

            private void DrawPlayer(string prefix, NetWorldPlayer player, bool drawProperties = false)
            {
                EditorGUILayout.LabelField($"{prefix}{(player == null ? "null" : player.ToString())}");
                if (player == null)
                    return;

                if (drawProperties)
                {
                    EditorGUI.indentLevel++;
                    foreach (var fieldInfo in player.Properties.GetType().GetFields(bindingAttr: BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (!typeof(IDictionary).IsAssignableFrom(fieldInfo.FieldType))
                            continue;

                        var dictionary = (IDictionary)fieldInfo.GetValue(player!.Properties);

                        EditorGUILayout.LabelField($"{ObjectNames.NicifyVariableName(fieldInfo.Name)}: {dictionary.Keys.Count}");
                        EditorGUI.indentLevel++;
                        foreach (var key in dictionary.Keys)
                            EditorGUILayout.LabelField($"{key}: {dictionary[key]}");
                        EditorGUI.indentLevel--;
                    }

                    EditorGUI.indentLevel--;
                }
            }
        }

        private class MainWorldDrawer : WorldDrawer<NetWorldRuntime>
        {
            private bool _drawCommon;
            private bool _drawPendingViews;

            public MainWorldDrawer(NetWorldRuntime runtimeWorld) : base(runtimeWorld)
            {
            }

            protected override void OnDrawWorld()
            {
                base.OnDrawWorld();

                EditorGUILayout.Space();
                _drawCommon = EditorGUILayout.Foldout(_drawCommon, content: "Common");
                if (_drawCommon)
                {
                    EditorGUI.indentLevel++;
                    {
                        // Register requests.
                        {
                            EditorGUILayout.Space();
                            var registerRequests = World.IncomingStateChangeRequests;
                            EditorGUILayout.LabelField($"Remote Update Requests: {registerRequests.Count}", EditorStyles.boldLabel);
                        }

                        // Temporary Unregistered Objects.
                        {
                            EditorGUILayout.Space();
                            var temporaryUnregisteredObjects = World.TemporaryUnregisteredObjects;
                            EditorGUILayout.LabelField($"Unregistered Objects: {temporaryUnregisteredObjects.Count}", EditorStyles.boldLabel);

                            EditorGUI.indentLevel++;
                            foreach (var objectState in temporaryUnregisteredObjects.Values)
                                EditorGUILayout.LabelField($"[{objectState.Uid}] [{objectState.PrefabId}] Owner: {objectState.Owner} Scene: {objectState.IsSceneObject}");
                            EditorGUI.indentLevel--;
                        }
                    }
                    EditorGUI.indentLevel--;
                }

                if (World.View != null)
                {
                    EditorGUILayout.Space();
                    _drawPendingViews = EditorGUILayout.Foldout(_drawPendingViews, $"Pending Views: {World.View.TemporaryUnregisteredViews.Count}") && World.View.TemporaryUnregisteredViews.Count > 0;
                    if (_drawPendingViews)
                    {
                        EditorGUI.indentLevel++;
                        {
                            foreach (var kvp in World.View.TemporaryUnregisteredViews)
                                EditorGUILayout.ObjectField($"Uid: {kvp.Key}", kvp.Value, typeof(NetObjectIdentity), allowSceneObjects: true);
                        }
                        EditorGUI.indentLevel--;
                    }
                }
            }
        }

        private class ReplicaWorldDrawer : WorldDrawer<NetWorldEvaluator>
        {
            public ReplicaWorldDrawer(NetWorldEvaluator replicaWorld) : base(replicaWorld)
            {
            }

            protected override void OnDrawWorld()
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(World.Paused ? "Play >" : "Pause ||"))
                    World.SetPaused(!World.Paused);

                EditorGUI.BeginDisabledGroup(!World.Paused);

                if (GUILayout.Button("<< Previous"))
                    World.MoveToPreviousSnapshot(true);

                if (GUILayout.Button("Next >>"))
                    World.MoveToNextSnapshot(true);

                EditorGUILayout.EndHorizontal();
                EditorGUI.BeginChangeCheck();
                var targetSnapshot = EditorGUILayout.IntSlider(World.CurrentSnapshotIndex, 0, World.BufferCount - 1);
                if (EditorGUI.EndChangeCheck() && targetSnapshot != World.CurrentSnapshotIndex)
                    World.MoveToSnapshotSafe(targetSnapshot, true);

                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space();

                base.OnDrawWorld();
            }
        }

        #endregion

        private bool _showNetworkStatistics;
        private bool _showMainWorldHistoryInfo;

        private bool _showMainWorldBuffer;

        //private ServiceProvider<GSNetwork> _networkProvider;
        private Vector2 _globalScrollPosition;

        private MainWorldDrawer _mainWorldDrawer;
        private ReplicaWorldDrawer _currentWorldReplicaDrawer;

        [MenuItem("System/Network/Debug Window")]
        private static void Init()
        {
            var window = GetWindow<NetDebugWindow>("Network Debug Info");
            window.Show();
            window._showMainWorldHistoryInfo = true;
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

            // _networkProvider ??= new ServiceProvider<GSNetwork>();
            //if (_networkProvider.Instance == null)
            //    return;

            //var realtime = _networkProvider.Instance.Realtime;
            //EditorGUILayout.LabelField($"RT status: {realtime.IsSessionRunningAndReady}", EditorStyles.boldLabel);
            //EditorGUILayout.LabelField($"RT conn: {realtime.LocalConnectionUID}", EditorStyles.boldLabel);

            EditorGUILayout.Space();

            _showNetworkStatistics = EditorGUILayout.Foldout(_showNetworkStatistics, "Network Statistics");
            if (_showNetworkStatistics)
                EditorGUILayout.HelpBox("Statistics is not available", MessageType.Error);

            EditorGUILayout.Space();

            EditorGUILayout.LabelField($"World Count: {NetWorld.All.Count}", EditorStyles.boldLabel);

            var mainWorld = default(NetWorldRuntime);
            foreach (var world in NetWorld.All.Values)
            {
                mainWorld = world as NetWorldRuntime;
                if (mainWorld != null)
                    break;
            }

            if (mainWorld == null)
            {
                EditorGUILayout.HelpBox("World not represented yet", MessageType.Error);
                return;
            }

            if (_mainWorldDrawer is not { IsValid: true })
                _mainWorldDrawer = new MainWorldDrawer(mainWorld);

            EditorGUILayout.Space();
            _globalScrollPosition = EditorGUILayout.BeginScrollView(_globalScrollPosition);

            // History.
            _showMainWorldHistoryInfo = EditorGUILayout.Foldout(_showMainWorldHistoryInfo, "Main World History Info");
            if (_showMainWorldHistoryInfo)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"History size: {mainWorld.HistoryBuffer.Size} ({mainWorld.HistoryBuffer.SizeMb:f2}mb) [{mainWorld.HistoryBuffer.AverageFrameSize}] bytes, cnt: {mainWorld.HistoryBuffer.Count} frames, time: {mainWorld.HistoryBuffer.LengthInSeconds:f2} s", EditorStyles.boldLabel);

                EditorGUI.BeginDisabledGroup(_currentWorldReplicaDrawer != null);
                if (GUILayout.Button("Playback"))
                {
                    var currentReplica = mainWorld.Replicate();
                    _currentWorldReplicaDrawer = new ReplicaWorldDrawer(currentReplica);
                    Bootstrap.UnityUpdate += OnUpdate;

                    void OnUpdate()
                    {
                        currentReplica.Update(Time.deltaTime);
                        if (currentReplica.IsValid)
                            return;

                        _currentWorldReplicaDrawer = null;
                        Bootstrap.UnityUpdate -= OnUpdate;
                    }
                }

                EditorGUI.EndDisabledGroup();

                _showMainWorldBuffer = EditorGUILayout.Foldout(_showMainWorldBuffer, "Show buffer");
                if (_showMainWorldBuffer)
                {
                    EditorGUI.indentLevel++;
                    foreach (var netWorldSnapshot in mainWorld.HistoryBuffer)
                        EditorGUILayout.LabelField($"t:{netWorldSnapshot.Time:f2} dt:{netWorldSnapshot.DeltaTime:f2} size: {netWorldSnapshot.GetSize()} obj: {netWorldSnapshot.Objects.Count} pl: {netWorldSnapshot.Players.PlayersCollection.Count}", EditorStyles.boldLabel);
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.Space();
            _mainWorldDrawer.Draw();

            if (_currentWorldReplicaDrawer != null)
            {
                if (_currentWorldReplicaDrawer.IsValid)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Replica info:", EditorStyles.boldLabel);
                    _currentWorldReplicaDrawer.Draw();
                }
                else
                    _currentWorldReplicaDrawer = null;
            }

            EditorGUILayout.EndScrollView();
        }
    }
}