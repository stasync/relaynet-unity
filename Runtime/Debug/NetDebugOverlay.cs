using System;
using System.Collections.Generic;
using Keel.RelayNet.Drawing;
using Keel.RelayNet.GameObjects;
using Keel.RelayNet.StateManagement;
using UnityEngine;

namespace Keel.RelayNet.Debug
{
    /// <summary>
    /// RelayNet's in-game debug overlay, drawn with IMGUI. It installs itself in the editor, and in builds that define
    /// <c>RELAYNET_DEBUG</c>. Press <see cref="Hotkey"/> to step through the <see cref="NetDebugOverlayMode"/>s, or
    /// hold Shift to step back. The mode is remembered between runs.
    /// The hotkey is read from IMGUI events, so it works with either input system. To toggle the overlay another way,
    /// such as from a button on a touch screen, set <see cref="Mode"/> or call <see cref="NextMode"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetDebugOverlay : MonoBehaviour
    {
        private const string MODE_PREFS_KEY = "Keel.RelayNet.DebugOverlay.Mode";
        private const int MODE_COUNT = (int)NetDebugOverlayMode.Inspector + 1;

        private const float MARGIN = 10f;
        private const float PANEL_WIDTH = 300f;
        private const float GRAPH_HEIGHT = 36f;
        private const float INSPECTOR_WIDTH = 420f;
        private const float INSPECTOR_MAX_HEIGHT = 600f;
        private const float ROW_NAME_WIDTH = 130f;

        /// <summary>
        /// Objects farther from the camera than this get no label, so a crowd doesn't turn into a wall of text.
        /// </summary>
        private const float LABEL_DISTANCE = 30f;

        /// <summary>
        /// The inspector lists at most this many objects at once. The search narrows them down.
        /// </summary>
        private const int MAX_LISTED_OBJECTS = 200;

        /// <summary>
        /// The traffic graph's scale never goes below this, so a quiet session doesn't fill it with noise.
        /// </summary>
        private const float MIN_GRAPH_SCALE = 1024f;

        private static readonly string[] s_tabs = { "Session", "Players", "Objects" };
        private static readonly string[] s_objectFilters = { "All", "Yours", "Remote" };

        private static NetDebugOverlayMode s_mode;
        private static bool s_modeLoaded;

        /// <summary>
        /// The key that steps through the modes. With Shift, it steps back.
        /// </summary>
        public static KeyCode Hotkey { get; set; } = KeyCode.F3;

        public static NetDebugOverlayMode Mode
        {
            get
            {
                LoadMode();
                return s_mode;
            }
            set
            {
                LoadMode();
                if (s_mode == value)
                    return;

                s_mode = value;
                PlayerPrefs.SetInt(MODE_PREFS_KEY, (int)value);
            }
        }

        public static void NextMode() =>
            Mode = (NetDebugOverlayMode)(((int)Mode + 1) % MODE_COUNT);

        public static void PreviousMode() =>
            Mode = (NetDebugOverlayMode)(((int)Mode + MODE_COUNT - 1) % MODE_COUNT);

        /// <summary>
        /// Reads the mode saved by the last run, once. Not from a static constructor: Unity may run that off the main
        /// thread, where <see cref="PlayerPrefs"/> can't be used.
        /// </summary>
        private static void LoadMode()
        {
            if (s_modeLoaded)
                return;

            var savedMode = PlayerPrefs.GetInt(MODE_PREFS_KEY, (int)NetDebugOverlayMode.Off);
            s_mode = savedMode is >= 0 and < MODE_COUNT ? (NetDebugOverlayMode)savedMode : NetDebugOverlayMode.Off;
            s_modeLoaded = true;
        }

        private readonly NetStatisticsSampler _sampler = new();
        private readonly Dictionary<NetObjectUid, string> _labels = new();
        private readonly Dictionary<uint, string> _prefabNames = new();
        private readonly HashSet<NetObjectUid> _expandedObjects = new();

        // The panel's text, rebuilt with every sample instead of every frame.
        private string _panelHeader;
        private string _panelBody;
        private string _panelCompression;
        private string _panelFooter;
        private string _graphScale;
        private NetDebugOverlayMode _panelMode = (NetDebugOverlayMode)(-1);

        private Rect _inspectorRect;
        private int _tab;
        private int _objectFilter;
        private string _objectSearch = string.Empty;
        private Vector2 _scroll;

        private Texture2D _panelBackground;
        private GUIStyle _panelStyle;
        private GUIStyle _richStyle;
        private GUIStyle _plainStyle;
        private GUIStyle _dimStyle;
        private GUIStyle _headingStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _rowStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            LoadMode();

            var container = new GameObject(nameof(NetDebugOverlay));
            container.AddComponent<NetDebugOverlay>();
            DontDestroyOnLoad(container);
        }

        private void Update()
        {
            if (s_mode == NetDebugOverlayMode.Off)
                return;

            var session = NetSession.All.Count > 0 ? NetSession.All[0] : null;
            var sessionChanged = !ReferenceEquals(session, _sampler.Session);
            if (_sampler.Sample(session, Time.realtimeSinceStartupAsDouble) || sessionChanged || _panelMode != s_mode)
                RebuildPanel();
        }

        private void LateUpdate()
        {
            if (s_mode < NetDebugOverlayMode.World)
                return;

            var drawer = NetDebugDraw.Drawer;
            var camera = Camera.main;
            if (drawer != null && camera != null)
                NetWorldMarkers.Draw(drawer, camera);
        }

        private void OnDestroy()
        {
            if (_panelBackground != null)
                Destroy(_panelBackground);
        }

        private void OnGUI()
        {
            var current = Event.current;
            if (current.type == EventType.KeyDown && current.keyCode == Hotkey)
            {
                if (current.shift)
                    PreviousMode();
                else
                    NextMode();

                // This event was laid out for the previous mode, so the new one is drawn from the next event on.
                current.Use();
                return;
            }

            if (s_mode == NetDebugOverlayMode.Off || _panelHeader == null)
                return;

            EnsureStyles();

            var scale = GetScale();
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var screen = new Vector2(Screen.width / scale, Screen.height / scale);

            if (s_mode >= NetDebugOverlayMode.World && current.type == EventType.Repaint)
                DrawLabels(scale);

            DrawPanel(screen);

            if (s_mode == NetDebugOverlayMode.Inspector)
            {
                if (_inspectorRect.width <= 0f)
                    _inspectorRect = new Rect(screen.x - INSPECTOR_WIDTH - MARGIN, MARGIN, INSPECTOR_WIDTH, Mathf.Min(INSPECTOR_MAX_HEIGHT, screen.y - 2f * MARGIN));

                // Keep the window reachable when the screen shrinks.
                _inspectorRect.x = Mathf.Clamp(_inspectorRect.x, 0f, Mathf.Max(0f, screen.x - _inspectorRect.width));
                _inspectorRect.y = Mathf.Clamp(_inspectorRect.y, 0f, Mathf.Max(0f, screen.y - 40f));
                _inspectorRect = GUI.Window(GetInstanceID(), _inspectorRect, DrawInspector, "RelayNet");
            }

            GUI.matrix = matrix;
        }

        /// <summary>
        /// Scales the overlay up on dense and large screens, where IMGUI's pixel sizes come out tiny.
        /// </summary>
        private static float GetScale() =>
            Mathf.Max(1f, Mathf.Max(Screen.height / 1080f, Screen.dpi / 160f));

        #region Panel

        private void RebuildPanel()
        {
            _panelMode = s_mode;
            _labels.Clear();

            _panelFooter = $"{s_mode}  ·  {Hotkey} next  ·  Shift+{Hotkey} back";

            var session = _sampler.Session;
            if (session == null)
            {
                _panelHeader = $"<b>RelayNet</b>   <color={NetDebugColors.ToHex(NetDebugColors.Dim)}>no session</color>";
                _panelBody = null;
                _panelCompression = null;
                return;
            }

            _panelHeader = session.IsConnected
                ? $"<b>RelayNet</b>   <color={NetDebugColors.ToHex(NetDebugColors.Good)}>connected</color>"
                : $"<b>RelayNet</b>   <color={NetDebugColors.ToHex(NetDebugColors.Bad)}>disconnected</color>";

            var world = session.MainWorld;
            var localPlayer = world?.Players.LocalPlayer;

            var client = localPlayer == null
                ? "Joining the room..."
                : $"Client {localPlayer.Uid}{(localPlayer.IsMaster() ? " · master" : "")} · {world.Players.All.Count} players{(localPlayer.RttMs > 0 ? $" · {localPlayer.RttMs} ms" : "")}";

            var transfer = _sampler.LastTransfer;
            var payload = _sampler.LastPayload;
            var sent = $"<color={NetDebugColors.ToHex(NetDebugColors.Sent)}>Out</color>  {NetDebugFormat.Rate(_sampler.PerSecond(transfer.BytesSent))} · {_sampler.PerSecond(payload.MessagesSent):0} msg/s";
            var received = $"<color={NetDebugColors.ToHex(NetDebugColors.Received)}>In</color>    {NetDebugFormat.Rate(_sampler.PerSecond(transfer.BytesReceived))} · {_sampler.PerSecond(payload.MessagesReceived):0} msg/s";

            var objects = string.Empty;
            if (world != null)
            {
                var owned = 0;
                foreach (var state in world.ObjectStates.Values)
                {
                    if (state.IsOwnedByLocalPlayer)
                        owned++;
                }

                objects = $"\nObjects {world.ObjectStates.Count} · {owned} yours · {world.ObjectStates.Count - owned} remote";
            }

            _panelBody = $"{client}\n{sent}\n{received}{objects}";

            var totals = _sampler.PayloadTotals;
            _panelCompression = session.Compressor == null
                ? $"<color={NetDebugColors.ToHex(NetDebugColors.Dim)}>No compression</color>"
                : $"{session.Compressor.GetType().Name} saves {NetDebugFormat.Savings(totals.BytesSent, totals.CompressedBytesSent)} out, {NetDebugFormat.Savings(totals.BytesReceived, totals.CompressedBytesReceived)} in";

            _graphScale = $"peak {NetDebugFormat.Rate(_sampler.HistoryMaxRate)}";
        }

        private void DrawPanel(Vector2 screen)
        {
            GUILayout.BeginArea(new Rect(MARGIN, MARGIN, PANEL_WIDTH, screen.y - 2f * MARGIN));
            GUILayout.BeginVertical(_panelStyle);

            GUILayout.Label(_panelHeader, _richStyle);
            if (_panelBody != null)
            {
                GUILayout.Label(_panelBody, _richStyle);
                DrawGraph(GUILayoutUtility.GetRect(0f, GRAPH_HEIGHT, GUILayout.ExpandWidth(true)));
                GUILayout.Label(_panelCompression, _richStyle);
            }

            GUILayout.Label(_panelFooter, _dimStyle);

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        /// <summary>
        /// A minute of traffic, the newest on the right: what was sent rises from the middle line, what was received
        /// hangs below it.
        /// </summary>
        private void DrawGraph(Rect rect)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            var color = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.05f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            var middle = rect.y + rect.height * 0.5f;
            var halfHeight = rect.height * 0.5f;
            var scale = Mathf.Max(_sampler.HistoryMaxRate, MIN_GRAPH_SCALE);
            var barWidth = rect.width / NetStatisticsSampler.HISTORY_LENGTH;

            for (var i = 0; i < _sampler.HistoryCount; i++)
            {
                _sampler.GetHistory(i, out var sentRate, out var receivedRate);
                var x = rect.xMax - (_sampler.HistoryCount - i) * barWidth;

                var sentHeight = halfHeight * sentRate / scale;
                GUI.color = NetDebugColors.Sent;
                GUI.DrawTexture(new Rect(x, middle - sentHeight, barWidth, sentHeight), Texture2D.whiteTexture);

                var receivedHeight = halfHeight * receivedRate / scale;
                GUI.color = NetDebugColors.Received;
                GUI.DrawTexture(new Rect(x, middle, barWidth, receivedHeight), Texture2D.whiteTexture);
            }

            GUI.color = color;
            GUI.Label(new Rect(rect.x + 2f, rect.y, rect.width, 16f), _graphScale, _dimStyle);
        }

        #endregion

        #region Labels

        private void DrawLabels(float scale)
        {
            var camera = Camera.main;
            if (camera == null)
                return;

            var cameraPosition = camera.transform.position;
            var color = GUI.color;

            foreach (var world in NetWorld.All.Values)
            {
                foreach (var state in world.ObjectStates.Values)
                {
                    if (!state.DataCollection.TryGetData<NetObjectTransformData>(out var data))
                        continue;

                    var position = data.Position.Value;
                    if ((position - cameraPosition).sqrMagnitude > LABEL_DISTANCE * LABEL_DISTANCE)
                        continue;

                    var screenPoint = camera.WorldToScreenPoint(position);
                    if (screenPoint.z <= 0f)
                        continue;

                    // IMGUI counts from the top of the screen, and in the scaled units the overlay draws in.
                    var x = screenPoint.x / scale;
                    var y = (Screen.height - screenPoint.y) / scale;
                    var rect = new Rect(x - 100f, y - 44f, 200f, 32f);
                    var label = GetLabel(state);

                    // A shadow keeps the label readable on any background.
                    _labelStyle.normal.textColor = Color.black;
                    GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), label, _labelStyle);
                    _labelStyle.normal.textColor = NetDebugColors.ForPlayer(state.Owner);
                    GUI.Label(rect, label, _labelStyle);
                }
            }

            GUI.color = color;
        }

        private string GetLabel(NetObjectState state)
        {
            if (!_labels.TryGetValue(state.Uid, out var label))
            {
                var owner = state.Owner;
                label = $"{state.Uid} {GetPrefabName(state.PrefabUid)}\n{(owner == null ? "nobody" : owner.Name)}";
                _labels.Add(state.Uid, label);
            }

            return label;
        }

        private string GetPrefabName(uint prefabUid)
        {
            if (!_prefabNames.TryGetValue(prefabUid, out var name))
            {
                name = NetPrefabDatabase.TryGetPrefab(prefabUid, out var prefab) ? prefab.name : prefabUid.ToString();
                _prefabNames.Add(prefabUid, name);
            }

            return name;
        }

        #endregion

        #region Inspector

        private void DrawInspector(int id)
        {
            if (GUI.Button(new Rect(_inspectorRect.width - 24f, 2f, 20f, 16f), "x"))
                Mode = NetDebugOverlayMode.World;

            var tab = GUILayout.Toolbar(_tab, s_tabs);
            if (tab != _tab)
            {
                _tab = tab;
                _scroll = Vector2.zero;
            }

            _scroll = GUILayout.BeginScrollView(_scroll);

            var session = _sampler.Session;
            var world = session?.MainWorld;
            if (world == null)
                GUILayout.Label("No session is running.", _dimStyle);
            else
            {
                switch (_tab)
                {
                    case 0:
                        DrawSessionTab(session, world);
                        break;
                    case 1:
                        DrawPlayersTab(world);
                        break;
                    default:
                        DrawObjectsTab(world);
                        break;
                }
            }

            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, _inspectorRect.width, 20f));
        }

        private void DrawSessionTab(NetSession session, NetWorldRuntime world)
        {
            var transfer = _sampler.TransferTotals;
            var payload = _sampler.PayloadTotals;

            Heading("Traffic");
            Row("Sent", $"{NetDebugFormat.Bytes(transfer.BytesSent)} in {transfer.PacketsSent} packets");
            Row("Received", $"{NetDebugFormat.Bytes(transfer.BytesReceived)} in {transfer.PacketsReceived} packets");
            Row("Messages", $"{payload.MessagesSent} sent · {payload.MessagesReceived} received");
            Row("Average message", $"{NetDebugFormat.Bytes(Average(payload.BytesSent, payload.MessagesSent))} out · {NetDebugFormat.Bytes(Average(payload.BytesReceived, payload.MessagesReceived))} in");

            Heading("Compression");
            Row("Compressor", session.Compressor == null ? "None" : session.Compressor.GetType().Name);
            Row("Out", $"{NetDebugFormat.Bytes(payload.BytesSent)} packed to {NetDebugFormat.Bytes(payload.CompressedBytesSent)}, saves {NetDebugFormat.Savings(payload.BytesSent, payload.CompressedBytesSent)}");
            Row("In", $"{NetDebugFormat.Bytes(payload.CompressedBytesReceived)} unpacked to {NetDebugFormat.Bytes(payload.BytesReceived)}, saves {NetDebugFormat.Savings(payload.BytesReceived, payload.CompressedBytesReceived)}");
            Row("Headers", $"{NetDebugFormat.Share(Mathf.Max(0, transfer.BytesSent - payload.CompressedBytesSent), transfer.BytesSent)} of the bytes sent");

            Heading("World");
            Row("Time", $"{world.Time:0.0} s");
            Row("Objects", world.ObjectStates.Count.ToString());
            Row("Pending updates", world.IncomingStateChangeRequests.Count.ToString());
            Row("History", $"{world.HistoryBuffer.Count} snapshots, {world.HistoryBuffer.LengthInSeconds:0.0} s, {NetDebugFormat.Bytes(world.HistoryBuffer.Size)}");

            GUILayout.Space(6f);
            if (GUILayout.Button("Reset counters"))
                _sampler.Reset();
        }

        private void DrawPlayersTab(NetWorldRuntime world)
        {
            foreach (var player in world.Players.All)
            {
                var owned = 0;
                foreach (var state in world.ObjectStates.Values)
                {
                    if (state.OwnerUid == player.Uid)
                        owned++;
                }

                GUILayout.BeginHorizontal();
                Swatch(NetDebugColors.ForPlayer(player));
                GUILayout.Label($"{player.Name}  [{player.Uid}]{(player.IsLocal() ? " · you" : "")}{(player.IsMaster() ? " · master" : "")}", _plainStyle);
                GUILayout.EndHorizontal();

                GUILayout.Label($"{(player.IsReady ? "Ready" : "Loading")} · {player.Platform} · owns {owned}{(player.RttMs > 0 ? $" · {player.RttMs} ms" : "")}", _dimStyle);
                GUILayout.Space(4f);
            }
        }

        private void DrawObjectsTab(NetWorldRuntime world)
        {
            _objectFilter = GUILayout.Toolbar(_objectFilter, s_objectFilters);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", _dimStyle, GUILayout.Width(50f));
            _objectSearch = GUILayout.TextField(_objectSearch);
            GUILayout.EndHorizontal();

            var listed = 0;
            var matching = 0;
            foreach (var state in world.ObjectStates.Values)
            {
                if ((_objectFilter == 1 && !state.IsOwnedByLocalPlayer) || (_objectFilter == 2 && state.IsOwnedByLocalPlayer))
                    continue;

                var prefabName = GetPrefabName(state.PrefabUid);
                if (!MatchesSearch(state, prefabName))
                    continue;

                matching++;
                if (listed == MAX_LISTED_OBJECTS)
                    continue;

                listed++;
                DrawObjectRow(world, state, prefabName);
            }

            if (matching == 0)
                GUILayout.Label("No objects match.", _dimStyle);
            else if (matching > listed)
                GUILayout.Label($"{matching - listed} more. Search to narrow the list.", _dimStyle);
        }

        private bool MatchesSearch(NetObjectState state, string prefabName)
        {
            if (string.IsNullOrEmpty(_objectSearch))
                return true;

            var owner = state.Owner;
            return state.Uid.ToString().IndexOf(_objectSearch, StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf(_objectSearch, StringComparison.OrdinalIgnoreCase) >= 0
                || (owner != null && owner.Name != null && owner.Name.IndexOf(_objectSearch, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DrawObjectRow(NetWorldRuntime world, NetObjectState state, string prefabName)
        {
            var expanded = _expandedObjects.Contains(state.Uid);
            var owner = state.Owner;

            // A remote object is only as fresh as its owner's last update.
            var age = state.IsOwnedByLocalPlayer ? string.Empty : $" · {world.Time - state.WorldTime:0.0} s ago";

            GUILayout.BeginHorizontal();
            Swatch(NetDebugColors.ForPlayer(owner));
            if (GUILayout.Button($"{(expanded ? "-" : "+")} {state.Uid}  {prefabName}  {(owner == null ? "nobody" : owner.Name)}{age}", _rowStyle))
            {
                if (expanded)
                    _expandedObjects.Remove(state.Uid);
                else
                    _expandedObjects.Add(state.Uid);
            }

            GUILayout.EndHorizontal();

            if (!expanded)
                return;

            GUILayout.BeginHorizontal();
            GUILayout.Space(18f);
            GUILayout.BeginVertical();

            GUILayout.Label($"{state.StateChangeType} · {(state.IsShared ? "shared" : "not shared")}{(state.IsTagObjectForCurrentOwner ? " · tag object" : "")} · {state.RpcBuffer.TemporalBuffer.Count} RPCs", _dimStyle);
            foreach (var data in state.DataCollection)
            {
                GUILayout.Label($"{data.GetType().Name} · {data.GetSerializedSize_Editor()} B", _plainStyle);
                foreach (var (field, value) in data.GetValuesInfo_Editor())
                    Row(field, value);
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }

        private void Heading(string text)
        {
            GUILayout.Space(6f);
            GUILayout.Label(text, _headingStyle);
        }

        private void Row(string name, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, _dimStyle, GUILayout.Width(ROW_NAME_WIDTH));
            GUILayout.Label(value, _plainStyle);
            GUILayout.EndHorizontal();
        }

        private static void Swatch(Color color)
        {
            var rect = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f), GUILayout.Height(10f));
            rect.y += 4f;

            if (Event.current.type != EventType.Repaint)
                return;

            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static float Average(int total, int count) =>
            count <= 0 ? 0f : total / (float)count;

        #endregion

        private void EnsureStyles()
        {
            if (_panelStyle != null)
                return;

            _panelBackground = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _panelBackground.SetPixel(0, 0, new Color(0.07f, 0.08f, 0.1f, 0.85f));
            _panelBackground.Apply();

            _panelStyle = new GUIStyle { normal = { background = _panelBackground }, padding = new RectOffset(10, 10, 8, 8) };

            _richStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = true,
                wordWrap = false,
                margin = new RectOffset(0, 0, 1, 1),
                padding = new RectOffset(0, 0, 1, 1),
                normal = { textColor = new Color(0.92f, 0.93f, 0.95f) }
            };

            _plainStyle = new GUIStyle(_richStyle) { richText = false };
            _dimStyle = new GUIStyle(_plainStyle) { fontSize = 11, normal = { textColor = NetDebugColors.Dim } };
            _headingStyle = new GUIStyle(_plainStyle) { fontStyle = FontStyle.Bold };
            _labelStyle = new GUIStyle(_plainStyle) { fontSize = 11, alignment = TextAnchor.LowerCenter };
            _rowStyle = new GUIStyle(_plainStyle) { hover = { textColor = Color.white } };
        }
    }
}
