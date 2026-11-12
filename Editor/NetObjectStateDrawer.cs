using System.Collections.Generic;
using Keel.Networking;
using Keel.RelayNet.Editor;
using Keel.RelayNet.GameObjects;
using Keel.RelayNet.StateManagement;
using UnityEditor;
using UnityEngine;

namespace Keel.RelayNet.Debug.Editor
{
    /// <summary>
    /// One object of the debug window's Objects tab: its line in the list, and its details once it's selected.
    /// </summary>
    internal sealed class NetObjectStateDrawer
    {
        private static readonly NetWriter s_networkStateSizeWriter = new();

        private readonly Dictionary<int, bool> _dataFoldoutStates = new();
        private readonly NetObjectState _state;
        private readonly NetWorld _world;

        private readonly string _prefabName;
        private readonly string _prefabLabel;

        /// <summary>
        /// Kept apart from the state, which forgets its uid once the object is removed.
        /// </summary>
        internal NetObjectUid Uid { get; }

        /// <summary>
        /// The uid and the prefab.
        /// </summary>
        internal string Title { get; }

        internal bool IsValid() => _state != null && _state.IsValid();

        internal NetObjectStateDrawer(NetObjectState objectState, NetWorld world)
        {
            _state = objectState;
            _world = world;
            Uid = objectState.Uid;

            // The prefab of an object never changes.
            _prefabName = NetPrefabDatabase.TryGetPrefab(_state.PrefabUid, out var prefab) ? prefab.name : null;
            _prefabLabel = _prefabName == null ? $"{_state.PrefabUid}" : $"{_prefabName} ({_state.PrefabUid})";
            Title = $"{Uid}  {_prefabName ?? _prefabLabel}";
        }

        /// <summary>
        /// What a search in the Objects tab matches against: the uid, the prefab and the owner.
        /// </summary>
        internal string GetSearchText() =>
            $"{Uid} {_prefabLabel} {_state.Owner?.Name}";

        /// <summary>
        /// The object's line in the list, in its owner's color.
        /// </summary>
        internal void DrawRow(Rect rect)
        {
            var owner = _state.Owner;
            NetDebugEditorGUI.Swatch(new Rect(rect.x + 4f, rect.y, rect.width, rect.height), NetDebugColors.ForPlayer(owner));
            GUI.Label(new Rect(rect.x + 20f, rect.y, rect.width - 20f, rect.height), $"{Title}  ·  {(owner == null ? "nobody" : owner.Name)}");

            // A remote object is only as fresh as its owner's last update.
            if (!_state.IsOwnedByLocalPlayer)
                GUI.Label(new Rect(rect.x, rect.y, rect.width - 4f, rect.height), $"{_world.Time - _state.WorldTime:0.0} s ago", NetDebugEditorGUI.RightMiniLabel);
        }

        /// <summary>
        /// Selects the object's GameObject in the Hierarchy, if it has one.
        /// </summary>
        internal void SelectView()
        {
            var view = GetView();
            if (view == null)
                return;

            Selection.activeGameObject = view.gameObject;
            EditorGUIUtility.PingObject(view.gameObject);
        }

        private NetObjectIdentity GetView() =>
            _world.View == null || !IsValid() ? null : _world.GetObjectView(Uid);

        internal void DrawDetails()
        {
            var owner = _state.Owner;
            var view = GetView();

            var titleRect = EditorGUILayout.GetControlRect();
            NetDebugEditorGUI.Swatch(titleRect, NetDebugColors.ForPlayer(owner));
            GUI.Label(new Rect(titleRect.x + 16f, titleRect.y, titleRect.width - 16f, titleRect.height), $"{Title}  ·  {(owner == null ? "nobody" : owner.Name)}", EditorStyles.boldLabel);

            var removeObjectFromWorld = false;
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUI.BeginDisabledGroup(view == null);
                if (GUILayout.Button(new GUIContent("Select", "Select its GameObject in the Hierarchy. Double-clicking the object in the list does the same.")))
                    SelectView();
                EditorGUI.EndDisabledGroup();

                if (GUILayout.Button("Copy JSON"))
                    NetDebugEditorGUI.Copy(NetDebugJson.Object(_state), $"object {Uid} as JSON");

                if (GUILayout.Button("Remove From World"))
                    removeObjectFromWorld = true;

                if (_state.IsOwnedByLocalPlayer)
                {
                    if (GUILayout.Button("Destroy Soft"))
                        _state.RequestRemoval();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);
            NetDebugEditorGUI.Row("Prefab", _prefabLabel);
            NetDebugEditorGUI.Row("Shared", _state.IsShared ? "Yes" : "No");

            // An object outlives the player that owned it, and a scene object starts with no owner at all.
            NetDebugEditorGUI.Row("Owner", owner == null
                ? $"[{_state.OwnerUid}] nobody"
                : $"[{owner.Uid}] {owner.Name}{(_state.IsOwnedByLocalPlayer ? " (you)" : "")}{(_state.IsOwnedByMasterClient ? " (master)" : "")}");
            if (_state.IsTagObjectForCurrentOwner)
                NetDebugEditorGUI.Row("Tag object", "The owner's");

            NetDebugEditorGUI.Row("Change type", _state.StateChangeType.ToString());
            NetDebugEditorGUI.Row("Updated", $"{_state.WorldTime:0.00} s");

            if (_state.IsOwnedByLocalPlayer && _world is NetWorldRuntime runtimeWorld)
            {
                var lastReplicationState = runtimeWorld.IsStateReplicationEnabled(Uid);
                var newReplicationState = EditorGUILayout.Toggle("Replicate", lastReplicationState);
                if (newReplicationState != lastReplicationState)
                    runtimeWorld.SetStateReplicationEnabled(Uid, newReplicationState);
            }

            var stateSnapshot = _state.ToSnapshot();
            s_networkStateSizeWriter.SeekZero();
            stateSnapshot.Serialize(s_networkStateSizeWriter);
            NetDebugEditorGUI.Row("Size", $"{stateSnapshot.GetSize()} B, {s_networkStateSizeWriter.Position} B on the network");

            if (_world.View != null)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(label: "View", view, typeof(GameObject), allowSceneObjects: true);
                EditorGUI.EndDisabledGroup();
            }

            var rpcBuffer = _state.RpcBuffer.TemporalBuffer;
            NetDebugEditorGUI.Section($"RPCs ({rpcBuffer.Count})");
            foreach (var rpc in rpcBuffer)
                DrawRpc(rpc);

            var dataCollection = _state.DataCollection;
            NetDebugEditorGUI.Section($"Data ({dataCollection.Count})");
            foreach (var data in dataCollection)
            {
                if (!_dataFoldoutStates.TryGetValue(data.TypeHashCode, out var drawData))
                    _dataFoldoutStates.Add(data.TypeHashCode, drawData = false);

                var dataSize = data.GetSerializedSize_Editor();

                var color = GUI.color;
                GUI.color = Color.Lerp(Color.green, Color.red, Mathf.Clamp01(dataSize / 200f));
                drawData = EditorGUILayout.Foldout(drawData, content: $"{data.GetType().FullName}  Size: {dataSize}  Type: {data.TypeHashCode}", toggleOnLabelClick: true);
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

            if (removeObjectFromWorld)
                _world.RemoveObject(Uid);
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
