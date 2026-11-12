using Keel.RelayNet.Debug;
using UnityEditor;
using UnityEngine;

namespace Keel.RelayNet.Editor
{
    /// <summary>
    /// Drawing helpers the debug window's tabs share.
    /// </summary>
    internal static class NetDebugEditorGUI
    {
        private static readonly Color s_separator = new(0.5f, 0.5f, 0.5f, 0.3f);
        private static readonly Color s_graphBackground = new(0f, 0f, 0f, 0.2f);
        private static GUIStyle s_coloredLabel;
        private static GUIStyle s_rightMiniLabel;

        public static GUIStyle RightMiniLabel =>
            s_rightMiniLabel ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };

        /// <summary>
        /// A bold title with a thin line under it.
        /// </summary>
        public static void Section(string title)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true)), s_separator);
            EditorGUILayout.Space(2f);
        }

        /// <summary>
        /// Puts the text on the clipboard, and says so in the window whose button was clicked.
        /// </summary>
        public static void Copy(string text, string what)
        {
            EditorGUIUtility.systemCopyBuffer = text;
            if (EditorWindow.focusedWindow != null)
                EditorWindow.focusedWindow.ShowNotification(new GUIContent($"Copied {what}"), fadeoutWait: 1d);
        }

        public static void Row(string label, string value) =>
            EditorGUILayout.LabelField(label, value);

        public static void Row(string label, string value, Color color)
        {
            s_coloredLabel ??= new GUIStyle(EditorStyles.label);
            s_coloredLabel.normal.textColor = color;
            EditorGUILayout.LabelField(label, value, s_coloredLabel);
        }

        /// <summary>
        /// A small square in the color, vertically centered in the line rect.
        /// </summary>
        public static void Swatch(Rect lineRect, Color color) =>
            EditorGUI.DrawRect(new Rect(lineRect.x, lineRect.y + (lineRect.height - 10f) * 0.5f, 10f, 10f), color);

        /// <summary>
        /// The last minute of traffic, the newest on the right: what was sent rises from the middle line, what was
        /// received hangs below it.
        /// </summary>
        public static void Graph(NetStatisticsSampler sampler, float height)
        {
            var rect = EditorGUI.IndentedRect(GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true)));
            if (Event.current.type != EventType.Repaint)
                return;

            EditorGUI.DrawRect(rect, s_graphBackground);

            var middle = rect.y + rect.height * 0.5f;
            var halfHeight = rect.height * 0.5f;
            var scale = Mathf.Max(sampler.HistoryMaxRate, 1024f);
            var barWidth = rect.width / NetStatisticsSampler.HISTORY_LENGTH;

            for (var i = 0; i < sampler.HistoryCount; i++)
            {
                sampler.GetHistory(i, out var sent, out var received);
                var x = rect.xMax - (sampler.HistoryCount - i) * barWidth;

                var sentHeight = halfHeight * sent / scale;
                EditorGUI.DrawRect(new Rect(x, middle - sentHeight, barWidth, sentHeight), NetDebugColors.Sent);

                var receivedHeight = halfHeight * received / scale;
                EditorGUI.DrawRect(new Rect(x, middle, barWidth, receivedHeight), NetDebugColors.Received);
            }

            EditorGUI.DrawRect(new Rect(rect.x, middle, rect.width, 1f), s_separator);
            GUI.Label(new Rect(rect.x + 4f, rect.y + 1f, rect.width - 8f, 16f), $"peak {NetDebugFormat.Rate(sampler.HistoryMaxRate)}", EditorStyles.miniLabel);
            GUI.Label(new Rect(rect.x + 4f, rect.y + 1f, rect.width - 8f, 16f), "1 min", RightMiniLabel);
        }
    }
}
