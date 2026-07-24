using System.Collections.Generic;
using Core.Networking;
using LROTech.Network.GameObjects;
using LROTech.Network.StateManagement;
using UnityEditor;
using UnityEngine;

namespace LROTech.Network.Debug.Editor
{
    internal sealed class NetObjectStateDrawer
    {
        private static readonly NetWriter s_networkStateSizeWriter = new();

        private readonly Dictionary<int, bool> _dataFoldoutStates = new();
        private readonly NetObjectState _state;
        private readonly NetWorld _world;

        private bool _draw;

        internal bool IsValid() => _state != null && _state.IsValid();
        internal void SetDrawingEnabled(bool value) => _draw = value;

        internal NetObjectStateDrawer(NetObjectState objectState, NetWorld world)
        {
            _state = objectState;
            _world = world;
        }

        internal void Draw()
        {
            EditorGUILayout.BeginHorizontal();
            var prefabLabel = $"{_state.PrefabUid}";
            if (NetPrefabDatabase.TryGetPrefab(_state.PrefabUid, out var prefab))
                prefabLabel = $"{prefab.name} ({_state.PrefabUid})";

            _draw = EditorGUILayout.Foldout(_draw, content: $"Uid: {_state.Uid} Prefab: {prefabLabel}");

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

                EditorGUILayout.LabelField($"Uid: {_state.Uid} Scene: {_state.IsSceneObject} Prefab: {prefabLabel}", EditorStyles.boldLabel);
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
                    var view = _world.GetObjectView(_state.Uid);
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.ObjectField(label: "View", view, typeof(GameObject), allowSceneObjects: true);
                    EditorGUI.EndDisabledGroup();
                }
            }
            EditorGUI.indentLevel--;

            if (removeObjectFromWorld)
                _world.RemoveObject(_state.Uid);
        }

        private static void DrawRpc(NetObjectRpcState rpc)
        {
            var (delegateTargetType, methodInfo) = NetObjectRpcUtils.FindDelegateInfoByHashSlow(rpc.Hash);
            var handlerInfo = delegateTargetType == null
                ? "UNKNOWN"
                : $"{delegateTargetType.FullName}.{methodInfo.Name}";

            EditorGUILayout.LabelField($"{rpc.Uid} Hash: {rpc.Hash} '{handlerInfo}' Payload Size: {rpc.Payload.Length}");
        }
    }
}