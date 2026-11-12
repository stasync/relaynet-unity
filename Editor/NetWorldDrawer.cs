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
    /// expanded, selected and searched for as long as the world lives.
    /// </summary>
    internal sealed class NetWorldDrawer
    {
        private enum ObjectFilter
        {
            All,
            Yours,
            Remote
        }

        private const string LIST_WIDTH_PREF = "Keel.RelayNet.DebugWindow.ObjectListWidth";
        private const float MIN_LIST_WIDTH = 160f;
        private const float MIN_DETAILS_WIDTH = 240f;
        private const float DIVIDER_WIDTH = 5f;

        private static readonly Color s_selectedRow = new(0.24f, 0.48f, 0.9f, 0.35f);
        private static readonly Color s_divider = new(0.5f, 0.5f, 0.5f, 0.3f);
        private static readonly Comparison<NetObjectStateDrawer> s_byUid = (a, b) => a.Uid.CompareTo(b.Uid);

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

        private NetObjectUid _selectedUid;
        private string _selectedTitle;
        private float _listWidth;
        private Vector2 _listScroll;
        private Vector2 _detailsScroll;

        public NetWorldDrawer(NetWorld world)
        {
            World = world;
            _listWidth = EditorPrefs.GetFloat(LIST_WIDTH_PREF, 360f);
        }

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

        /// <summary>
        /// Shows the object in the details, whether or not the list shows it.
        /// </summary>
        public void SelectObject(NetObjectUid uid)
        {
            if (uid == _selectedUid)
                return;

            _selectedUid = uid;
            _selectedTitle = null;
            _detailsScroll = Vector2.zero;
        }

        /// <summary>
        /// The objects on the left, the selected one on the right. The details scroll on their own, so objects coming
        /// and going in the list don't move them.
        /// </summary>
        public void DrawObjects()
        {
            CollectMatchingObjects();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(100f));
            _filter = (ObjectFilter)EditorGUILayout.EnumPopup(_filter, EditorStyles.toolbarPopup, GUILayout.Width(70f));
            GUILayout.Label(_matchingDrawers.Count == World.ObjectStates.Count
                ? $"{World.ObjectStates.Count} objects"
                : $"{_matchingDrawers.Count} of {World.ObjectStates.Count} objects", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(new GUIContent("Copy JSON", "Copy the whole world as JSON: its properties, its players and all of its objects."), EditorStyles.toolbarButton))
                NetDebugEditorGUI.Copy(NetDebugJson.World(World), "the world as JSON");

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            {
                _listWidth = ClampListWidth(_listWidth);
                _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Width(_listWidth));
                DrawObjectList();
                if (World is NetWorldRuntime runtimeWorld)
                    DrawPendingObjects(runtimeWorld);
                EditorGUILayout.EndScrollView();

                DrawDivider();

                _detailsScroll = EditorGUILayout.BeginScrollView(_detailsScroll);
                DrawSelectedObject();
                EditorGUILayout.Space(8f);
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// A click shows the object in the details, a double-click also selects its GameObject.
        /// </summary>
        private void DrawObjectList()
        {
            var current = Event.current;
            foreach (var drawer in _matchingDrawers)
            {
                var rect = EditorGUILayout.GetControlRect();
                if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition))
                {
                    SelectObject(drawer.Uid);
                    if (current.clickCount == 2)
                        drawer.SelectView();

                    current.Use();
                }

                if (current.type != EventType.Repaint)
                    continue;

                if (drawer.Uid == _selectedUid)
                    EditorGUI.DrawRect(rect, s_selectedRow);

                drawer.DrawRow(rect);
            }
        }

        private void DrawSelectedObject()
        {
            if (!_selectedUid.IsValid)
            {
                EditorGUILayout.Space(20f);
                EditorGUILayout.LabelField("Select an object to see it here.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // The details stay on the removed object rather than move on to another one.
            var state = World.GetObject(_selectedUid);
            if (state == null)
            {
                EditorGUILayout.Space(20f);
                EditorGUILayout.LabelField($"{_selectedTitle ?? _selectedUid.ToString()} was removed from the world.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var drawer = GetDrawer(state);
            _selectedTitle = drawer.Title;
            drawer.DrawDetails();
        }

        /// <summary>
        /// The line between the list and the details: drag it to resize the list.
        /// </summary>
        private void DrawDivider()
        {
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Width(DIVIDER_WIDTH), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(new Rect(rect.center.x, rect.y, 1f, rect.height), s_divider);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            var id = GUIUtility.GetControlID(FocusType.Passive);
            var current = Event.current;
            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown when current.button == 0 && rect.Contains(current.mousePosition):
                    GUIUtility.hotControl = id;
                    current.Use();
                    break;

                // The list starts at the window's left edge, so the mouse is where its right edge goes.
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    _listWidth = ClampListWidth(current.mousePosition.x - DIVIDER_WIDTH * 0.5f);
                    current.Use();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    EditorPrefs.SetFloat(LIST_WIDTH_PREF, _listWidth);
                    current.Use();
                    break;
            }
        }

        private static float ClampListWidth(float width) =>
            Mathf.Clamp(width, MIN_LIST_WIDTH, Mathf.Max(MIN_LIST_WIDTH, EditorGUIUtility.currentViewWidth - DIVIDER_WIDTH - MIN_DETAILS_WIDTH));

        private NetObjectStateDrawer GetDrawer(NetObjectState state)
        {
            if (!_objectDrawers.TryGetValue(state.Uid, out var drawer))
            {
                drawer = new NetObjectStateDrawer(state, World);
                _objectDrawers.Add(state.Uid, drawer);
            }

            return drawer;
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

                var drawer = GetDrawer(state);
                if (string.IsNullOrEmpty(_search) || drawer.GetSearchText().IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                    _matchingDrawers.Add(drawer);
            }

            // The world keeps its objects in no particular order: a new one can take the place of a removed one.
            _matchingDrawers.Sort(s_byUid);
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
