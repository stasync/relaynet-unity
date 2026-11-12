using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Keel.RelayNet.Debug;
using Keel.RelayNet.Debug.Editor;
using Keel.RelayNet.GameObjects;
using Keel.RelayNet.StateManagement;
using UnityEditor;
using UnityEngine;

namespace Keel.RelayNet.Editor
{
    /// <summary>
    /// The players and the objects of one world, for the debug window's Players and Objects tabs. Keeps what's
    /// expanded and what's searched for as long as the world lives.
    /// </summary>
    internal sealed class NetWorldDrawer
    {
        private enum ObjectFilter
        {
            All,
            Yours,
            Remote
        }

        public NetWorld World { get; }
        public bool IsValid => World is { IsValid: true };

        private readonly Dictionary<NetObjectUid, NetObjectStateDrawer> _objectDrawers = new();
        private readonly List<NetObjectUid> _objectUids = new();
        private readonly List<NetObjectStateDrawer> _matchingDrawers = new();
        private readonly Dictionary<ClientUid, int> _ownedObjectCounts = new();

        private readonly HashSet<ClientUid> _expandedPlayers = new();
        private readonly HashSet<ClientUid> _expandedPlayerProperties = new();
        private readonly HashSet<ClientUid> _expandedPlayerTagObjects = new();
        private bool _worldPropertiesExpanded;

        private string _search = string.Empty;
        private ObjectFilter _filter;
        private bool _unregisteredSceneObjectsExpanded;
        private bool _pendingViewsExpanded;

        public NetWorldDrawer(NetWorld world) =>
            World = world;

        #region Players

        public void DrawPlayers()
        {
            CountOwnedObjects();

            var players = World.Players.All;
            if (players.Count == 0)
                EditorGUILayout.HelpBox("No players in this world yet.", MessageType.Info);

            foreach (var player in players)
            {
                var rect = EditorGUILayout.GetControlRect();
                NetDebugEditorGUI.Swatch(rect, NetDebugColors.ForPlayer(player));

                _ownedObjectCounts.TryGetValue(player.Uid, out var owned);
                var info = $"{(player.IsReady ? "Ready" : "Loading")} · {player.Platform} · owns {owned}{(player.RttMs > 0 ? $" · {player.RttMs} ms" : "")}";
                GUI.Label(rect, info, NetDebugEditorGUI.RightMiniLabel);

                var title = $"[{player.Uid}] {player.Name}{(player.IsLocal() ? "  (you)" : "")}{(player.IsMaster() ? "  (master)" : "")}";
                var expanded = EditorGUI.Foldout(new Rect(rect.x + 16f, rect.y, rect.width - 16f, rect.height), _expandedPlayers.Contains(player.Uid), title, toggleOnLabelClick: true);
                SetExpanded(_expandedPlayers, player.Uid, expanded);

                if (!expanded)
                    continue;

                EditorGUI.indentLevel++;
                DrawPlayer(player);
                EditorGUI.indentLevel--;
            }

            NetDebugEditorGUI.Section("World properties");
            _worldPropertiesExpanded = EditorGUILayout.Foldout(_worldPropertiesExpanded, "Set by the master client", toggleOnLabelClick: true);
            if (_worldPropertiesExpanded)
            {
                EditorGUI.indentLevel++;
                DrawProperties(World.Properties);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawPlayer(NetWorldPlayer player)
        {
            NetDebugEditorGUI.Row("World time", $"{player.WorldTime:0.00} s");

            var tagObjects = player.TagObjectUidCollection;
            var tagObjectsExpanded = EditorGUILayout.Foldout(_expandedPlayerTagObjects.Contains(player.Uid), $"Tag objects ({tagObjects.Count})", toggleOnLabelClick: true);
            SetExpanded(_expandedPlayerTagObjects, player.Uid, tagObjectsExpanded);
            if (tagObjectsExpanded)
            {
                EditorGUI.indentLevel++;
                foreach (var tagObjectUid in tagObjects)
                {
                    var label = tagObjectUid == player.MainTagObjectUid ? $"{tagObjectUid} (main)" : tagObjectUid.ToString();
                    var tagObjectState = World.GetObject(tagObjectUid);
                    EditorGUILayout.LabelField(label, tagObjectState != null ? $"prefab {tagObjectState.PrefabUid}" : "missing from the world");
                }

                EditorGUI.indentLevel--;
            }

            var propertiesExpanded = EditorGUILayout.Foldout(_expandedPlayerProperties.Contains(player.Uid), "Properties", toggleOnLabelClick: true);
            SetExpanded(_expandedPlayerProperties, player.Uid, propertiesExpanded);
            if (propertiesExpanded)
            {
                EditorGUI.indentLevel++;
                DrawProperties(player.Properties);
                EditorGUI.indentLevel--;
            }
        }

        private static void DrawProperties(NetProperties properties)
        {
            foreach (var fieldInfo in properties.GetType().GetFields(bindingAttr: BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!typeof(IDictionary).IsAssignableFrom(fieldInfo.FieldType))
                    continue;

                var dictionary = (IDictionary)fieldInfo.GetValue(properties);
                if (dictionary.Count == 0)
                    continue;

                EditorGUILayout.LabelField($"{ObjectNames.NicifyVariableName(fieldInfo.Name)} ({dictionary.Count})", EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                foreach (var key in dictionary.Keys)
                    EditorGUILayout.LabelField(key.ToString(), dictionary[key]?.ToString());
                EditorGUI.indentLevel--;
            }
        }

        #endregion

        #region Objects

        public void DrawObjects()
        {
            CollectMatchingObjects();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(100f));
            _filter = (ObjectFilter)EditorGUILayout.EnumPopup(_filter, EditorStyles.toolbarPopup, GUILayout.Width(70f));
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(new GUIContent("Copy JSON", "Copy the whole world as JSON: its properties, its players and all of its objects."), EditorStyles.toolbarButton))
                NetDebugEditorGUI.Copy(NetDebugJson.World(World), "the world as JSON");

            if (GUILayout.Button("Expand all", EditorStyles.toolbarButton))
            {
                foreach (var drawer in _matchingDrawers)
                    drawer.SetDrawingEnabled(true);
            }

            if (GUILayout.Button("Collapse all", EditorStyles.toolbarButton))
            {
                foreach (var drawer in _matchingDrawers)
                    drawer.SetDrawingEnabled(false);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(_matchingDrawers.Count == World.ObjectStates.Count
                ? $"{World.ObjectStates.Count} objects"
                : $"{_matchingDrawers.Count} of {World.ObjectStates.Count} objects", EditorStyles.miniLabel);

            foreach (var drawer in _matchingDrawers)
            {
                // Removing an object, with the button in its drawer or anything reacting to that, can take others with it.
                if (drawer.IsValid())
                    drawer.Draw();
            }

            if (World is NetWorldRuntime runtimeWorld)
                DrawPendingObjects(runtimeWorld);
        }

        private void CollectMatchingObjects()
        {
            _objectUids.Clear();
            foreach (var uid in _objectDrawers.Keys)
            {
                if (!World.ObjectExists(uid) || !_objectDrawers[uid].IsValid())
                    _objectUids.Add(uid);
            }

            foreach (var uid in _objectUids)
                _objectDrawers.Remove(uid);

            _matchingDrawers.Clear();
            foreach (var state in World.ObjectStates.Values)
            {
                if ((_filter == ObjectFilter.Yours && !state.IsOwnedByLocalPlayer) || (_filter == ObjectFilter.Remote && state.IsOwnedByLocalPlayer))
                    continue;

                if (!_objectDrawers.TryGetValue(state.Uid, out var drawer))
                {
                    drawer = new NetObjectStateDrawer(state, World);
                    _objectDrawers.Add(state.Uid, drawer);
                }

                if (string.IsNullOrEmpty(_search) || drawer.GetSearchText().IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                    _matchingDrawers.Add(drawer);
            }
        }

        private void DrawPendingObjects(NetWorldRuntime world)
        {
            var unregisteredSceneObjects = world.UnregisteredSceneObjects;
            var pendingViews = world.View == null ? null : world.View.TemporaryUnregisteredViews;
            if (unregisteredSceneObjects.Count == 0 && (pendingViews == null || pendingViews.Count == 0))
                return;

            NetDebugEditorGUI.Section("Waiting");

            if (unregisteredSceneObjects.Count > 0)
            {
                _unregisteredSceneObjectsExpanded = EditorGUILayout.Foldout(_unregisteredSceneObjectsExpanded, $"Scene objects not in the world yet ({unregisteredSceneObjects.Count})", toggleOnLabelClick: true);
                if (_unregisteredSceneObjectsExpanded)
                {
                    EditorGUI.indentLevel++;
                    foreach (var sceneObject in unregisteredSceneObjects.Values)
                        EditorGUILayout.ObjectField($"Scene id {sceneObject.SceneObjectId}", sceneObject, typeof(NetObjectSceneIdentity), allowSceneObjects: true);
                    EditorGUI.indentLevel--;
                }
            }

            if (pendingViews is { Count: > 0 })
            {
                _pendingViewsExpanded = EditorGUILayout.Foldout(_pendingViewsExpanded, $"Views waiting for their state ({pendingViews.Count})", toggleOnLabelClick: true);
                if (_pendingViewsExpanded)
                {
                    EditorGUI.indentLevel++;
                    foreach (var pair in pendingViews)
                        EditorGUILayout.ObjectField($"Uid {pair.Key}", pair.Value, typeof(NetObjectIdentity), allowSceneObjects: true);
                    EditorGUI.indentLevel--;
                }
            }
        }

        #endregion

        private void CountOwnedObjects()
        {
            _ownedObjectCounts.Clear();
            foreach (var state in World.ObjectStates.Values)
            {
                _ownedObjectCounts.TryGetValue(state.OwnerUid, out var count);
                _ownedObjectCounts[state.OwnerUid] = count + 1;
            }
        }

        private static void SetExpanded(HashSet<ClientUid> expanded, ClientUid uid, bool value)
        {
            if (value)
                expanded.Add(uid);
            else
                expanded.Remove(uid);
        }
    }
}
