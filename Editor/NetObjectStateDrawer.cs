using System.Collections.Generic;
using Keel.Networking;
using Keel.RelayNet.Editor;
using Keel.RelayNet.GameObjects;
using Keel.RelayNet.StateManagement;
using UnityEditor;
using UnityEngine;

namespace Keel.RelayNet.Debug.Editor
{
    internal sealed class NetObjectStateDrawer
    {
        private static readonly NetWriter s_networkStateSizeWriter = new();

        private readonly Dictionary<int, bool> _dataFoldoutStates = new();
        private readonly NetObjectState _state;
        private readonly NetWorld _world;

        private readonly string _prefabName;
        private readonly string _prefabLabel;

        private bool _draw;

        internal bool IsValid() => _state != null && _state.IsValid();
        internal void SetDrawingEnabled(bool value) => _draw = value;

        internal NetObjectStateDrawer(NetObjectState objectState, NetWorld world)
        {
            _state = objectState;
            _world = world;

            // The prefab of an object never changes.
            _prefabName = NetPrefabDatabase.TryGetPrefab(_state.PrefabUid, out var prefab) ? prefab.name : null;
            _prefabLabel = _prefabName == null ? $"{_state.PrefabUid}" : $"{_prefabName} ({_state.PrefabUid})";
        }

        /// <summary>
        /// What a search in the Objects tab matches against: the uid, the prefab and the owner.
        /// </summary>
        internal string GetSearchText() =>
            $"{_state.Uid} {_prefabLabel} {_state.Owner?.Name}";

        internal void Draw()
        {
            var owner = _state.Owner;
            var rect = EditorGUILayout.GetControlRect();
            NetDebugEditorGUI.Swatch(rect, NetDebugColors.ForPlayer(owner));

            // A remote object is only as fresh as its owner's last update.
            var age = _state.IsOwnedByLocalPlayer ? "" : $" · {_world.Time - _state.WorldTime:0.0} s ago";
            var title = $"{_state.Uid}  {_prefabName ?? _prefabLabel}  ·  {(owner == null ? "nobody" : owner.Name)}{age}";

            var view = _world.View == null ? null : _world.GetObjectView(_state.Uid);
            var buttonWidth = view == null ? 0f : 52f;
            _draw = EditorGUI.Foldout(new Rect(rect.x + 16f, rect.y, rect.width - 16f - buttonWidth, rect.height), _draw, title, toggleOnLabelClick: true);

            if (view != null && GUI.Button(new Rect(rect.xMax - buttonWidth + 4f, rect.y + 1f, buttonWidth - 4f, rect.height - 2f), "Select", EditorStyles.miniButton))
            {
                Selection.activeGameObject = view.gameObject;
                EditorGUIUtility.PingObject(view.gameObject);
            }

            if (!_draw)
                return;

            if (!_state.IsValid())
                return;

            var removeObjectFromWorld = false;
            EditorGUI.indentLevel++;
            {
                EditorGUILayout.BeginHorizontal();

                GUILayout.Space(pixels: EditorGUI.indentLevel * 15); // indent for buttons.
                if (GUILayout.Button("Copy JSON"))
                    NetDebugEditorGUI.Copy(NetDebugJson.Object(_state), $"object {_state.Uid} as JSON");

                if (GUILayout.Button("Remove From World"))
                    removeObjectFromWorld = true;

                if (_state.IsOwnedByLocalPlayer)
                {
                    if (GUILayout.Button("Destroy Soft"))
                        _state.RequestRemoval();
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField($"Uid: {_state.Uid} Shared: {_state.IsShared} Prefab: {_prefabLabel}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Change Type: {_state.StateChangeType}", EditorStyles.boldLabel);

                // An object outlives the player that owned it, and a scene object starts with no owner at all.
                EditorGUILayout.LabelField(owner == null
                    ? $"Owner: [{_state.OwnerUid}] NONE"
                    : $"Owner: [{owner.Uid}] {owner.Name} (Local: {_state.IsOwnedByLocalPlayer}, Master: {_state.IsOwnedByMasterClient})", EditorStyles.boldLabel);
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

                var rpcBuffer = _state.RpcBuffer.TemporalBuffer;

                EditorGUILayout.LabelField($"RPC: {rpcBuffer.Count}", EditorStyles.boldLabel);
                {
                    EditorGUI.indentLevel++;
                    foreach (var rpc in rpcBuffer)
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