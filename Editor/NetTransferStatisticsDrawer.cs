using UnityEditor;
using UnityEngine;

namespace Keel.RelayNet.Editor
{
    /// <summary>
    /// Draws the traffic of a <see cref="NetSession"/>. The transport only counts totals, so the rates are derived here
    /// by sampling those totals over time.
    /// </summary>
    internal sealed class NetTransferStatisticsDrawer
    {
        /// <summary>
        /// How often the totals are sampled to derive the rates from. Short enough to react, long enough not to turn
        /// the numbers into noise.
        /// </summary>
        private const double SAMPLE_INTERVAL_SECONDS = 0.5d;

        private NetSession _session;
        private bool _draw;

        // The totals the counters are displayed relative to - moved forward by the 'Reset' button to measure a single action.
        private NetTransferStatistics _baseline;

        // The last sample and the traffic between it and the one before, used to derive the rates.
        private NetTransferStatistics _lastSample;
        private NetTransferStatistics _lastSampleDelta;
        private double _lastSampleTime;
        private double _lastSampleDeltaTime;

        internal void Draw(NetSession session)
        {
            _draw = EditorGUILayout.Foldout(_draw, content: "Network Statistics");
            if (!_draw)
                return;

            EditorGUI.indentLevel++;
            {
                if (session == null)
                    EditorGUILayout.HelpBox("No session is running", MessageType.Info);
                else
                    DrawSession(session);
            }
            EditorGUI.indentLevel--;
        }

        private void DrawSession(NetSession session)
        {
            // A new session starts its own counters - everything sampled from the previous one is meaningless now.
            if (!ReferenceEquals(session, _session))
            {
                _session = session;
                _baseline = default;
                _lastSample = default;
                _lastSampleDelta = default;
                _lastSampleTime = 0d;
                _lastSampleDeltaTime = 0d;
            }

            var statistics = session.Statistics;
            Sample(statistics);

            var totals = statistics - _baseline;

            EditorGUILayout.LabelField($"Connection: {(session.IsConnected ? "Connected" : "Disconnected")}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Sent: {FormatBytes(totals.BytesSent)} in {totals.PacketsSent} packets ({FormatRate(_lastSampleDelta.BytesSent)}, {FormatPacketRate(_lastSampleDelta.PacketsSent)})");
            EditorGUILayout.LabelField($"Received: {FormatBytes(totals.BytesReceived)} in {totals.PacketsReceived} packets ({FormatRate(_lastSampleDelta.BytesReceived)}, {FormatPacketRate(_lastSampleDelta.PacketsReceived)})");
            EditorGUILayout.LabelField($"Total: {FormatBytes(totals.BytesSent + totals.BytesReceived)}");

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pixels: EditorGUI.indentLevel * 15); // indent for buttons.
            if (GUILayout.Button("Reset Counters"))
                _baseline = statistics;

            EditorGUILayout.EndHorizontal();
        }

        private void Sample(NetTransferStatistics statistics)
        {
            var time = EditorApplication.timeSinceStartup;
            if (time - _lastSampleTime < SAMPLE_INTERVAL_SECONDS)
                return;

            // The very first sample has nothing to be compared against - it only sets the starting point.
            if (_lastSampleTime > 0d)
            {
                _lastSampleDelta = statistics - _lastSample;
                _lastSampleDeltaTime = time - _lastSampleTime;
            }

            _lastSample = statistics;
            _lastSampleTime = time;
        }

        private string FormatRate(int bytes) =>
            _lastSampleDeltaTime <= 0d ? "-" : $"{FormatBytes((int)(bytes / _lastSampleDeltaTime))}/s";

        private string FormatPacketRate(int packets) =>
            _lastSampleDeltaTime <= 0d ? "-" : $"{packets / _lastSampleDeltaTime:f1} pkt/s";

        private static string FormatBytes(int bytes)
        {
            const int kilobyte = 1024;
            const int megabyte = kilobyte * kilobyte;

            return bytes switch
            {
                >= megabyte => $"{bytes / (float)megabyte:f2} mb",
                >= kilobyte => $"{bytes / (float)kilobyte:f2} kb",
                _ => $"{bytes} b"
            };
        }
    }
}