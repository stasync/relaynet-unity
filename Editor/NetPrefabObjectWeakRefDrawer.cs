using Keel.RelayNet.GameObjects;
using UnityEditor;
using UnityEngine;

namespace Keel.RelayNet.Editor
{
    /// <summary>
    /// Draws a <see cref="NetPrefabObjectWeakRef"/> as a GameObject picker instead of a raw number.
    ///
    /// The struct itself only stores the uid; the assigned object is never serialized. Instead, the
    /// object shown in the field is resolved back from the uid through the registry every repaint,
    /// and picking a new object writes the uid that the registry reports for it:
    ///  - assigning an object that validates -> the uid (and object) are taken from the registry;
    ///  - assigning an object that fails validation (or clearing it) -> the uid stays 0 (Invalid).
    /// </summary>
    [CustomPropertyDrawer(typeof(NetPrefabObjectWeakRef))]
    internal sealed class NetPrefabObjectWeakRefDrawer : PropertyDrawer
    {
        private const string VALUE_FIELD = "_value";
        private const float UID_LABEL_WIDTH = 80f;
        private const float SPACING = 4f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var valueProp = property.FindPropertyRelative(VALUE_FIELD);

            label = EditorGUI.BeginProperty(position, label, property);

            var fieldRect = EditorGUI.PrefixLabel(position, label);

            // Split into [ object field ][ read-only uid ].
            var objectRect = new Rect(fieldRect.x, fieldRect.y, fieldRect.width - UID_LABEL_WIDTH - SPACING, fieldRect.height);
            var uidRect = new Rect(objectRect.xMax + SPACING, fieldRect.y, UID_LABEL_WIDTH, fieldRect.height);

            var currentUid = valueProp.uintValue;

            // Object shown in the field is derived from the stored uid, not serialized on the struct.
            TryResolvePrefab(currentUid, out var currentPrefab);

            var prevIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            EditorGUI.BeginChangeCheck();
            var picked = (GameObject)EditorGUI.ObjectField(objectRect, currentPrefab, typeof(GameObject), allowSceneObjects: false);
            if (EditorGUI.EndChangeCheck())
                valueProp.uintValue = ResolveUid(picked);

            using (new EditorGUI.DisabledScope(true))
                EditorGUI.LabelField(uidRect, currentUid == 0u ? "INVALID" : currentUid.ToString());

            EditorGUI.indentLevel = prevIndent;
            EditorGUI.EndProperty();
        }

        /// <summary>
        /// Validates <paramref name="prefab"/> against the registry and returns the uid to store:
        /// the registered uid on success, or 0 when the object is null or fails validation.
        /// </summary>
        private static uint ResolveUid(GameObject prefab)
        {
            if (prefab == null)
                return 0u;

            return TryValidate(prefab, out var uid) ? uid : 0u;
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="prefab"/> is a registered net prefab, and hands
        /// back its uid. Return <c>false</c> to reject the assignment (the field stays Invalid).
        /// </summary>
        private static bool TryValidate(GameObject prefab, out uint uid)
        {
            uid = NetPrefabDatabase.FindUidByGameObject(prefab);
            return uid != 0;
        }

        /// <summary>
        /// Resolves the GameObject to display for a stored <paramref name="uid"/>. Return
        /// <c>false</c> (with <paramref name="prefab"/> = null) when the uid is 0 or unknown.
        /// </summary>
        private static void TryResolvePrefab(uint uid, out GameObject prefab) =>
            NetPrefabDatabase.TryGetPrefab(uid, out prefab);
    }
}