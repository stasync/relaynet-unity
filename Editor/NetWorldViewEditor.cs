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
        var enabled = EditorGUILayout.Toggle("Enabled", !netWorldView.ForceRenderingOff && !netWorldView.ForceMute);
        if (EditorGUI.EndChangeCheck())
            netWorldView.SetEnabledImmidiate(enabled);

        EditorGUI.BeginChangeCheck();
        var renderEnabled = EditorGUILayout.Toggle("Visible", !netWorldView.ForceRenderingOff);
        if (EditorGUI.EndChangeCheck())
            netWorldView.SetVisibleImmediate(renderEnabled);

        EditorGUI.BeginChangeCheck();
        var soundEnabled = EditorGUILayout.Toggle("Sound", !netWorldView.ForceMute);
        if (EditorGUI.EndChangeCheck())
            netWorldView.SetSoundEnabledImmediate(soundEnabled);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "API used:\n" +
            "Renderer.forceRenderingOff\n" +
            "AudioSource.mute", MessageType.Warning);
    }
}