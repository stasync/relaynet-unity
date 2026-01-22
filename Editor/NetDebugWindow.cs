using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Core;
using Core.Networking;
using LROTech.Network.Debug.Runtime;
using LROTech.Network.GameObjects;
using LROTech.Network.StateManagement;
using UnityEditor;
using UnityEngine;

namespace LROTech.Network.Debug.Editor
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

            internal bool IsValid() => _state != null && _state.IsValid();
            internal void SetDrawingEnabled(bool value) => _draw = value;

            internal ObjectDrawer(NetObjectState objectState, NetWorld world)
            {
                _state = objectState;
                _world = world;
            }

            internal void Draw()
            {
                EditorGUILayout.BeginHorizontal();
                _draw = EditorGUILayout.Foldout(_draw, content: $"Uid: {_state.Uid} Prefab: {_state.PrefabId}");

                // Draw a colored box.
                var boxRect = GUILayoutUtility.GetRect(width: 20, EditorGUIUtility.singleLineHeight, GUILayout.Width(20));
                EditorGUI.DrawRect(boxRect, NetWorldViewDebug.GetColorForPlayer(_state.Owner));
                EditorGUILayout.EndHorizontal();

                if (!_draw)
                    return;

                if (!_state.IsValid())
                    return;

                var removeObjectFromWorld = false;
                EditorGUI.indentLevel++;
                {
                    EditorGUILayout.BeginHorizontal();

                    GUILayout.Space(pixels: EditorGUI.indentLevel * 15); // indent for buttons.
                    if (GUILayout.Button("Remove From World"))
                        removeObjectFromWorld = true;

                    if (_state.IsOwnedByLocalPlayer)
                    {
                        if (GUILayout.Button("Destroy Soft"))
                            _state.ScheduleUpdate(NetObjectStateChangeRequestType.ReliableRemoveUpdateSoft);
                    }

                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.LabelField($"Uid: {_state.Uid} Scene: {_state.IsSceneObject} Prefab: {_state.PrefabId}", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"Change Type: {_state.StateChangeType}", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"Owner: [{_state.Owner.Uid}] {_state.Owner.Name} (Local: {_state.IsOwnedByLocalPlayer}, Master: {_state.IsOwnedByMasterClient})", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"Update Time: {_state.WorldTime:f2}", EditorStyles.boldLabel);
                    if (_state.IsTagObjectForCurrentOwner)
                        EditorGUILayout.LabelField("Owner's tag object", EditorStyles.boldLabel);

                    if (_state.IsOwnedByLocalPlayer && _world is NetWorldRuntime runtimeWorld)
                    {
                        var lastReplicationState = runtimeWorld.IsStateReplicationEnabled(_state.Uid);
                        var newReplicationState = EditorGUILayout.Toggle("Replicate:", lastReplicationState);
                        if (newReplicationState != lastReplicationState)
                            runtimeWorld.SetStateReplicationEnabled(_state.Uid, newReplicationState);
                    }

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
                            DrawRpc(rpc);
                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.LabelField($"RPC: Persistent: {persistentBuffer.Count}", EditorStyles.boldLabel);
                    {
                        EditorGUI.indentLevel++;
                        foreach (var rpc in persistentBuffer)
                            DrawRpc(rpc);
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
                        EditorGUILayout.ObjectField(label: "View", _world.GetObjectView(_state.Uid), typeof(GameObject), allowSceneObjects: true);
                        EditorGUI.EndDisabledGroup();
                    }
                }
                EditorGUI.indentLevel--;

                if (removeObjectFromWorld)
                    _world.RemoveObject(_state.Uid);
            }

            private void DrawRpc(NetObjectRpcState rpc)
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

            private bool _draw;
            private readonly Dictionary<NetObjectUid, ObjectDrawer> _objectDrawers = new();
            private readonly List<NetObjectUid> _worldObjectsIterator = new();

            private bool _propertiesFoldoutState;
            private readonly Dictionary<ClientUid, bool> _playerFoldoutStates = new();
            private readonly Dictionary<ClientUid, bool> _propertiesFoldoutStatesPerPlayer = new();
            private readonly Dictionary<ClientUid, bool> _tagObjectsFoldoutStatesPerPlayer = new();

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

                _propertiesFoldoutState = EditorGUILayout.Foldout(_propertiesFoldoutState, content: "World Properties");
                if (_propertiesFoldoutState)
                {
                    EditorGUI.indentLevel++;
                    DrawProperties(World.Properties);
                    EditorGUI.indentLevel--;
                    EditorGUILayout.Space();
                }

                // Players.
                {
                    var ownedObjectsCountPerPlayer = new Dictionary<ClientUid, int>();
                    foreach (var state in World.ObjectStates.Values)
                    {
                        if (state.Owner == null)
                            continue;

                        ownedObjectsCountPerPlayer.TryAdd(state.Owner.Uid, 0);
                        ownedObjectsCountPerPlayer[state.Owner.Uid]++;
                    }

                    EditorGUILayout.LabelField($"Players: {World.Players.All.Count}", EditorStyles.boldLabel);
                    foreach (var worldPlayer in World.Players.All)
                    {
                        // Default value just in case.
                        ownedObjectsCountPerPlayer.TryAdd(worldPlayer.Uid, 0);

                        var prefix = $"[{worldPlayer.Uid}] ";
                        if (worldPlayer.IsLocal())
                            prefix += "[Local] ";
                        if (worldPlayer.IsMaster())
                            prefix += "[Master] ";

                        var label = $"{prefix}{worldPlayer.Name} (Owns:{ownedObjectsCountPerPlayer[worldPlayer.Uid]})";
                        _playerFoldoutStates.TryAdd(worldPlayer.Uid, false);

                        EditorGUILayout.BeginHorizontal();
                        _playerFoldoutStates[worldPlayer.Uid] = EditorGUILayout.Foldout(_playerFoldoutStates[worldPlayer.Uid], content: label);
                        // Draw a colored box.
                        var boxRect = GUILayoutUtility.GetRect(width: 20, EditorGUIUtility.singleLineHeight, GUILayout.Width(20));
                        EditorGUI.DrawRect(boxRect, NetWorldViewDebug.GetColorForPlayer(worldPlayer));
                        EditorGUILayout.EndHorizontal();

                        if (!_playerFoldoutStates[worldPlayer.Uid])
                            continue;

                        EditorGUI.indentLevel++;
                        DrawPlayer(worldPlayer);
                        EditorGUI.indentLevel--;
                    }
                }

                // Objects.
                {
                    foreach (var uid in new List<NetObjectUid>(_objectDrawers.Keys))
                    {
                        var drawer = _objectDrawers[uid];
                        if (!World.ObjectExists(uid) || !drawer.IsValid())
                            _objectDrawers.Remove(uid);
                    }

                    // Expand/Collapse all objects;
                    {
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Space(pixels: EditorGUI.indentLevel * 15); // indent for buttons.

                        if (GUILayout.Button("Collapse all"))
                        {
                            foreach (var drawer in _objectDrawers.Values)
                                drawer.SetDrawingEnabled(false);
                        }

                        if (GUILayout.Button("Expand all"))
                        {
                            foreach (var drawer in _objectDrawers.Values)
                                drawer.SetDrawingEnabled(true);
                        }

                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField($"Objects: {World.ObjectStates.Count}", EditorStyles.boldLabel);

                    _worldObjectsIterator.Clear();
                    _worldObjectsIterator.AddRange(World.ObjectStates.Keys);
                    foreach (var objectUid in _worldObjectsIterator)
                    {
                        var state = World.ObjectStates[objectUid];
                        if (!_objectDrawers.TryGetValue(objectUid, out var drawer))
                        {
                            drawer = new ObjectDrawer(state, World);
                            _objectDrawers.Add(objectUid, drawer);
                        }

                        drawer.Draw();
                    }
                }
            }

            private void DrawPlayer(NetWorldPlayer player)
            {
                EditorGUILayout.LabelField($"Uid: {player.Uid}");
                EditorGUILayout.LabelField($"Name: {player.Name}");
                EditorGUILayout.LabelField($"WorldTime: {player.WorldTime}");
                EditorGUILayout.LabelField($"IsReady: {player.IsReady}");
                EditorGUILayout.LabelField($"Platform: {player.Platform}");

                _tagObjectsFoldoutStatesPerPlayer.TryAdd(player.Uid, false);
                _tagObjectsFoldoutStatesPerPlayer[player.Uid] = EditorGUILayout.Foldout(_tagObjectsFoldoutStatesPerPlayer[player.Uid], content: $"Tag Objects: {player.TagObjectUidCollection.Count}");
                if (_tagObjectsFoldoutStatesPerPlayer[player.Uid])
                {
                    EditorGUI.indentLevel++;
                    foreach (var tagObjectUid in player.TagObjectUidCollection)
                    {
                        var label = tagObjectUid.ToString();
                        if (tagObjectUid == player.MainTagObjectUid)
                            label = $"[MAIN] {label}";

                        var tagObjectState = World.GetObject(tagObjectUid);
                        label = tagObjectState != null ? $"{label} {tagObjectState.PrefabId}" : $"[INVALID] {label}";

                        EditorGUILayout.LabelField(label);
                    }

                    EditorGUI.indentLevel--;
                }

                _propertiesFoldoutStatesPerPlayer.TryAdd(player.Uid, false);
                _propertiesFoldoutStatesPerPlayer[player.Uid] = EditorGUILayout.Foldout(_propertiesFoldoutStatesPerPlayer[player.Uid], content: "Properties");
                if (_propertiesFoldoutStatesPerPlayer[player.Uid])
                {
                    EditorGUI.indentLevel++;
                    DrawProperties(player.Properties);
                    EditorGUI.indentLevel--;
                }
            }

            private void DrawProperties(NetProperties properties)
            {
                foreach (var fieldInfo in properties.GetType().GetFields(bindingAttr: BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!typeof(IDictionary).IsAssignableFrom(fieldInfo.FieldType))
                        continue;

                    var dictionary = (IDictionary)fieldInfo.GetValue(properties);

                    EditorGUILayout.LabelField($"{ObjectNames.NicifyVariableName(fieldInfo.Name)}: {dictionary.Keys.Count}");
                    EditorGUI.indentLevel++;
                    foreach (var key in dictionary.Keys)
                        EditorGUILayout.LabelField($"{key}: {dictionary[key]}");
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
                            var registerRequests = World.IncomingStateChangeRequests;
                            EditorGUILayout.LabelField($"Remote Update Requests: {registerRequests.Count}", EditorStyles.boldLabel);
                        }

                        // Temporary Unregistered Scene Objects.
                        {
                            var unregisteredSceneObjects = World.UnregisteredSceneObjects;
                            EditorGUILayout.LabelField($"Unregistered Scene Objects: {unregisteredSceneObjects.Count}", EditorStyles.boldLabel);

                            EditorGUI.indentLevel++;
                            foreach (var objectState in unregisteredSceneObjects.Values)
                                EditorGUILayout.LabelField($"[{objectState.SceneObjectId}] [{objectState.name}]");
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