using LROTech.Network.GameObjects;
using UnityEditor;

namespace LROTech.Network.Debug.Editor
{
    [CustomEditor(typeof(NetObjectIdentity), editorForChildClasses: true)]
    internal sealed class NetObjectIdentityEditor : UnityEditor.Editor
    {
        private NetObjectStateDrawer _drawer;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            var identity = (NetObjectIdentity)target;

            if (identity.State == null || !EditorApplication.isPlaying)
                EditorGUILayout.HelpBox(message: "Network Identity is not initialized", MessageType.Warning);
            else
            {
                _drawer ??= new NetObjectStateDrawer(identity.State, identity.World);
                if (_drawer.IsValid())
                    _drawer.Draw();
            }
        }
    }
}