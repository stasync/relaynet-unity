using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(NetWorldView))]

public class NetWorldViewEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var netWorldView = target as NetWorldView;
        if (netWorldView == null)
            return;

        EditorGUI.BeginChangeCheck();
        var render = EditorGUILayout.Toggle("Visible", !netWorldView.ForceRenderingOff);
        if (EditorGUI.EndChangeCheck())
            netWorldView.SetVisibleImmediate(render);
    }
}