using System.Collections.Generic;
using Keel.RelayNet.Debug;
using Keel.RelayNet.GameObjects;
using Keel.RelayNet.StateManagement;
using UnityEditor;
using UnityEngine;

namespace Keel.RelayNet.Editor
{
    /// <summary>
    /// <b>Tools > RelayNet > Debug Window</b>: the sessions of a running game, their traffic, players and objects,
    /// and playback of the recorded history.
    /// </summary>
    internal sealed class NetDebugWindow : EditorWindow
    {
        private enum Tab
        {
            Overview,
            Players,
            Objects,
            History
        }

        private static readonly string[] s_tabNames = { "Overview", "Players", "Objects", "History" };

        /// <summary>
        /// The numbers change with every sample, a few times a second; repainting faster only costs editor time.
        /// </summary>
        private const double REPAINT_INTERVAL_SECONDS = 0.1d;

        private const float GRAPH_HEIGHT = 64f;

        /// <summary>
        /// The snapshots list's height while a playback shows below it.
        /// </summary>
        private const float SNAPSHOTS_HEIGHT = 120f;

        [SerializeField] private Tab _tab;
        [SerializeField] private int _sessionIndex;

        private readonly NetStatisticsSampler _sampler = new();
        private readonly List<string> _sessionNames = new();
        private Vector2 _scroll;
        private double _lastRepaintTime;

        private NetWorldDrawer _worldDrawer;
        private NetWorldDrawer _replicaDrawer;
        private NetWorldReplica _replica;
        private float _replicaLastGameTime;
        private bool _historyBufferExpanded;
        private Vector2 _snapshotsScroll;

        [MenuItem("Tools/RelayNet/Debug Window")]
        private static void Open() =>
            GetWindow<NetDebugWindow>("RelayNet").Show();

        private void Update()
        {
            if (!EditorApplication.isPlaying)
                return;

            _sampler.Sample(GetSelectedSession(), EditorApplication.timeSinceStartup);

            if (EditorApplication.timeSinceStartup - _lastRepaintTime < REPAINT_INTERVAL_SECONDS)
                return;

            _lastRepaintTime = EditorApplication.timeSinceStartup;
            Repaint();
        }

        private void OnEnable() =>
            Selection.selectionChanged += OnSelectionChanged;

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            StopPlayback();
        }

        /// <summary>
        /// Selecting an object's GameObject, in the Hierarchy or the Scene view, shows the object in the Objects tab, or
        /// in the playback when it's one of the replica's.
        /// </summary>
        private void OnSelectionChanged()
        {
            var gameObject = Selection.activeGameObject;
            if (gameObject == null)
                return;

            var identity = gameObject.GetComponentInParent<NetObjectIdentity>(includeInactive: true);
            if (identity == null || !identity.Initialized)
                return;

            if (_worldDrawer != null && _worldDrawer.World == identity.World)
                _worldDrawer.SelectObject(identity.InstanceId);
            else if (_replicaDrawer != null && _replicaDrawer.World == identity.World)
                _replicaDrawer.SelectObject(identity.InstanceId);
            else
                return;

            Repaint();
        }

        private void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                // Nothing survives the play mode exit - the world the replica was taken from is gone by now.
                StopPlayback();
                _worldDrawer = null;

                DrawToolbar(session: null);
                EditorGUILayout.Space(20f);
                EditorGUILayout.LabelField("Enter Play Mode to see the running sessions.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var session = GetSelectedSession();
            DrawToolbar(session);

            if (session == null)
            {
                EditorGUILayout.Space(20f);
                EditorGUILayout.LabelField("No session is running. Create a NetSession to see it here.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var world = session.MainWorld;
            if (world != null && (_worldDrawer is not { IsValid: true } || _worldDrawer.World != world))
                _worldDrawer = new NetWorldDrawer(world);

            EditorGUILayout.Space(4f);
            _tab = (Tab)GUILayout.Toolbar((int)_tab, s_tabNames, GUILayout.Height(24f));

            switch (_tab)
            {
                case Tab.Overview:
                    _scroll = EditorGUILayout.BeginScrollView(_scroll);
                    DrawOverview(session, world);
                    EditorGUILayout.Space(8f);
                    EditorGUILayout.EndScrollView();
                    break;

                case Tab.Players:
                    if (DrawWorldMissing(world))
                        break;

                    _scroll = EditorGUILayout.BeginScrollView(_scroll);
                    _worldDrawer.DrawPlayers();
                    EditorGUILayout.Space(8f);
                    EditorGUILayout.EndScrollView();
                    break;

                // The objects fill the window, with their list and their details each scrolling on its own.
                case Tab.Objects:
                    if (DrawWorldMissing(world))
                        break;

                    _worldDrawer.DrawObjects();
                    break;

                case Tab.History:
                    if (DrawWorldMissing(world))
                        break;

                    DrawHistory(world);
                    break;
            }
        }

        private NetSession GetSelectedSession()
        {
            var sessions = NetSession.All;
            if (sessions.Count == 0)
                return null;

            _sessionIndex = Mathf.Clamp(_sessionIndex, 0, sessions.Count - 1);
            return sessions[_sessionIndex];
        }

        #region Toolbar

        private void DrawToolbar(NetSession session)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            var sessions = NetSession.All;
            if (sessions.Count > 1)
            {
                _sessionNames.Clear();
                for (var i = 0; i < sessions.Count; i++)
                    _sessionNames.Add($"Session {i + 1} ({sessions[i].SessionOwner})");

                _sessionIndex = EditorGUILayout.Popup(_sessionIndex, _sessionNames.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(140f));
            }

            if (session != null)
            {
                var connected = session.IsConnected;
                var rect = GUILayoutUtility.GetRect(new GUIContent(connected ? "Connected" : "Disconnected"), EditorStyles.label, GUILayout.ExpandWidth(false));
                NetDebugEditorGUI.Swatch(new Rect(rect.x + 4f, rect.y, 10f, rect.height), connected ? NetDebugColors.Good : NetDebugColors.Bad);
                GUI.Label(new Rect(rect.x + 18f, rect.y, rect.width, rect.height), connected ? "Connected" : "Disconnected", EditorStyles.miniLabel);
                GUILayout.Space(18f);
            }

            GUILayout.FlexibleSpace();

            // The in-game overlay of this editor: the same mode as its hotkey sets.
            GUILayout.Label(new GUIContent("Overlay", $"The in-game overlay. Its hotkey is {NetDebugOverlay.Hotkey}."), EditorStyles.miniLabel);
            NetDebugOverlay.Mode = (NetDebugOverlayMode)EditorGUILayout.EnumPopup(NetDebugOverlay.Mode, EditorStyles.toolbarPopup, GUILayout.Width(80f));

            EditorGUI.BeginDisabledGroup(session == null);
            if (GUILayout.Button(new GUIContent("Reset", "Count the totals from now on, to measure a single action."), EditorStyles.toolbarButton))
                _sampler.Reset();

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Overview

        private void DrawOverview(NetSession session, NetWorldRuntime world)
        {
            var transfer = _sampler.TransferTotals;
            var payload = _sampler.PayloadTotals;
            var lastTransfer = _sampler.LastTransfer;
            var lastPayload = _sampler.LastPayload;

            NetDebugEditorGUI.Section("Connection");
            var localPlayer = world?.Players.LocalPlayer;
            NetDebugEditorGUI.Row("Status", session.IsConnected ? "Connected" : "Disconnected", session.IsConnected ? NetDebugColors.Good : NetDebugColors.Bad);
            NetDebugEditorGUI.Row("This client", localPlayer == null ? "Joining the room..." : $"{localPlayer.Uid}{(localPlayer.IsMaster() ? " (master)" : "")}");
            NetDebugEditorGUI.Row("Master client", session.SessionOwner.ToString());
            NetDebugEditorGUI.Row("Players", world == null ? "-" : world.Players.All.Count.ToString());
            if (localPlayer is { RttMs: > 0 })
                NetDebugEditorGUI.Row("Round trip", $"{localPlayer.RttMs} ms");

            NetDebugEditorGUI.Section("Traffic");
            NetDebugEditorGUI.Graph(_sampler, GRAPH_HEIGHT);
            NetDebugEditorGUI.Row("Out", $"{NetDebugFormat.Rate(_sampler.PerSecond(lastTransfer.BytesSent))} · {_sampler.PerSecond(lastTransfer.PacketsSent):0} packets/s · {_sampler.PerSecond(lastPayload.MessagesSent):0} messages/s", NetDebugColors.Sent);
            NetDebugEditorGUI.Row("In", $"{NetDebugFormat.Rate(_sampler.PerSecond(lastTransfer.BytesReceived))} · {_sampler.PerSecond(lastTransfer.PacketsReceived):0} packets/s · {_sampler.PerSecond(lastPayload.MessagesReceived):0} messages/s", NetDebugColors.Received);
            NetDebugEditorGUI.Row("Sent in total", $"{NetDebugFormat.Bytes(transfer.BytesSent)} in {transfer.PacketsSent} packets");
            NetDebugEditorGUI.Row("Received in total", $"{NetDebugFormat.Bytes(transfer.BytesReceived)} in {transfer.PacketsReceived} packets");
            NetDebugEditorGUI.Row("Average message", $"{NetDebugFormat.Bytes(Average(payload.BytesSent, payload.MessagesSent))} out · {NetDebugFormat.Bytes(Average(payload.BytesReceived, payload.MessagesReceived))} in");

            NetDebugEditorGUI.Section("Compression");
            if (session.Compressor == null)
                NetDebugEditorGUI.Row("Compressor", "None. Pass one to the NetSession to compress what it sends.");
            else
            {
                NetDebugEditorGUI.Row("Compressor", session.Compressor.GetType().Name);
                NetDebugEditorGUI.Row("Out", $"{NetDebugFormat.Bytes(payload.BytesSent)} packed to {NetDebugFormat.Bytes(payload.CompressedBytesSent)}, saves {NetDebugFormat.Savings(payload.BytesSent, payload.CompressedBytesSent)} ({NetDebugFormat.Rate(_sampler.PerSecond(lastPayload.BytesSent - lastPayload.CompressedBytesSent))})");
                NetDebugEditorGUI.Row("In", $"{NetDebugFormat.Bytes(payload.CompressedBytesReceived)} unpacked to {NetDebugFormat.Bytes(payload.BytesReceived)}, saves {NetDebugFormat.Savings(payload.BytesReceived, payload.CompressedBytesReceived)}");
            }

            // What the packets carry on top of the payloads: the relay's envelope and the transport's header.
            NetDebugEditorGUI.Row("Headers", $"{NetDebugFormat.Share(Mathf.Max(0, transfer.BytesSent - payload.CompressedBytesSent), transfer.BytesSent)} of the bytes sent");

            if (world == null)
                return;

            var owned = 0;
            foreach (var state in world.ObjectStates.Values)
            {
                if (state.IsOwnedByLocalPlayer)
                    owned++;
            }

            NetDebugEditorGUI.Section("World");
            NetDebugEditorGUI.Row("Time", $"{world.Time:0.00} s (local {world.LocalTime:0.00} s)");
            NetDebugEditorGUI.Row("Objects", $"{world.ObjectStates.Count} · {owned} yours · {world.ObjectStates.Count - owned} remote");
            NetDebugEditorGUI.Row("Pending updates", world.IncomingStateChangeRequests.Count.ToString());
            NetDebugEditorGUI.Row("Hash", world.GetVersionHash().ToString());
        }

        private static float Average(int total, int count) =>
            count <= 0 ? 0f : total / (float)count;

        #endregion

        #region History

        private void DrawHistory(NetWorldRuntime world)
        {
            var history = world.HistoryBuffer;

            NetDebugEditorGUI.Section("Recorded");
            NetDebugEditorGUI.Row("Length", $"{history.LengthInSeconds:0.0} s in {history.Count} snapshots");
            NetDebugEditorGUI.Row("Size", $"{NetDebugFormat.Bytes(history.Size)}, {NetDebugFormat.Bytes(history.AverageFrameSize)} per snapshot");

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15f);

            // Replicating an empty history produces a replica with no snapshot to stand on.
            EditorGUI.BeginDisabledGroup(_replica != null || history.Count == 0);
            if (GUILayout.Button("Play back"))
                StartPlayback(world);
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(_replica == null);
            if (GUILayout.Button("Stop"))
                StopPlayback();
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            _historyBufferExpanded = EditorGUILayout.Foldout(_historyBufferExpanded, "Snapshots", toggleOnLabelClick: true);
            if (_historyBufferExpanded)
            {
                // The snapshots scroll on their own, and leave room for the playback below them.
                _snapshotsScroll = EditorGUILayout.BeginScrollView(_snapshotsScroll, _replica == null ? GUILayout.ExpandHeight(true) : GUILayout.Height(SNAPSHOTS_HEIGHT));
                EditorGUI.indentLevel++;
                foreach (var snapshot in history)
                    EditorGUILayout.LabelField($"{snapshot.Time:0.00} s", $"+{snapshot.DeltaTime:0.00} s · {NetDebugFormat.Bytes(snapshot.GetSize())} · {snapshot.Objects.Count} objects · {snapshot.Players.PlayersCollection.Count} players");
                EditorGUI.indentLevel--;
                EditorGUILayout.EndScrollView();
            }

            if (_replica == null)
                return;

            if (!_replica.IsValid)
            {
                StopPlayback();
                return;
            }

            NetDebugEditorGUI.Section("Playback");
            DrawPlaybackControls();

            EditorGUILayout.Space(4f);
            _replicaDrawer.DrawObjects();
        }

        private void DrawPlaybackControls()
        {
            var lastSnapshotIndex = _replica.BufferCount - 1;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_replica.Paused ? "Play" : "Pause", GUILayout.Width(60f)))
                _replica.SetPaused(!_replica.Paused);

            // Every seek below addresses a snapshot by index - there is nothing to address in an empty buffer.
            EditorGUI.BeginDisabledGroup(!_replica.Paused || lastSnapshotIndex < 0);

            if (GUILayout.Button("< Previous"))
                _replica.MoveToPreviousSnapshot(true);

            if (GUILayout.Button("Next >"))
                _replica.MoveToNextSnapshot(true);

            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            var targetSnapshot = EditorGUILayout.IntSlider($"Snapshot {_replica.CurrentSnapshotIndex + 1} of {_replica.BufferCount}", _replica.CurrentSnapshotIndex, leftValue: 0, Mathf.Max(lastSnapshotIndex, 0));
            if (EditorGUI.EndChangeCheck() && targetSnapshot != _replica.CurrentSnapshotIndex && lastSnapshotIndex >= 0)
                _replica.MoveToSnapshotSafe(targetSnapshot, true);

            EditorGUI.EndDisabledGroup();
        }

        private void StartPlayback(NetWorldRuntime world)
        {
            StopPlayback();

            _replica = world.Replicate();
            _replicaDrawer = new NetWorldDrawer(_replica);
            _replicaLastGameTime = Time.time;

            EditorApplication.update += OnReplicaUpdate;
        }

        /// <summary>
        /// The replica is driven by this window alone - nothing else updates or disposes it, so it has to be released
        /// here, and on every path out (the window closing included), or it keeps running with its views alive.
        /// </summary>
        private void StopPlayback()
        {
            if (_replica == null)
                return;

            EditorApplication.update -= OnReplicaUpdate;

            if (_replica.IsValid)
                _replica.Dispose();

            _replica = null;
            _replicaDrawer = null;
        }

        private void OnReplicaUpdate()
        {
            if (_replica == null)
                return;

            // The editor loop also ticks in edit mode and while paused - the replica only advances with the game.
            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                return;

            // The replica disposes itself once it reaches the end of the history it was taken from.
            if (!_replica.IsValid)
            {
                StopPlayback();
                return;
            }

            // The editor loop can tick several times in one game frame, and Time.deltaTime is that frame's: advance by the
            // game time since the last tick instead, or the playback runs as many times too fast.
            var gameTime = Time.time;
            _replica.Update(gameTime - _replicaLastGameTime);
            _replicaLastGameTime = gameTime;
        }

        #endregion

        private static bool DrawWorldMissing(NetWorldRuntime world)
        {
            if (world != null)
                return false;

            EditorGUILayout.Space(20f);
            EditorGUILayout.LabelField("The session has no world.", EditorStyles.centeredGreyMiniLabel);
            return true;
        }
    }
}
