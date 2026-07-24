using LROTech.Network.GameObjects;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace LROTech.Network.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="NetPrefabDatabase"/>: a simple reorderable list of prefab
    /// references.
    /// </summary>
    [CustomEditor(typeof(NetPrefabDatabase), editorForChildClasses: true)]
    [CanEditMultipleObjects]
    internal sealed class NetPrefabsDatabaseEditor : UnityEditor.Editor
    {
        private ReorderableList _list;

        private void OnEnable()
        {
            _list = new ReorderableList(serializedObject, elements: serializedObject.FindProperty("_list"), draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, label: "Network Prefabs"),
                elementHeightCallback = _ => EditorGUIUtility.singleLineHeight + 6
            };

            _list.drawElementCallback = (rect, index, _, _) =>
            {
                rect.y += 3;
                rect.height = EditorGUIUtility.singleLineHeight;

                var element = _list.serializedProperty.GetArrayElementAtIndex(index);
                var prefabProp = element.FindPropertyRelative(nameof(NetPrefabDatabase.SerializedData._prefab));
                EditorGUI.PropertyField(rect, prefabProp, GUIContent.none);
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            _list.DoLayoutList();
            serializedObject.ApplyModifiedProperties();
        }
    }
}