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
            var identity = (NetObjectIdentity)target;

            EditorGUILayout.LabelField("Prefab Uid", label2: NetPrefabDatabase.Editor.GetPrefabUid(identity).ToString());

            if (identity.State != null)
            {
                _drawer ??= new NetObjectStateDrawer(identity.State, identity.World);
                if (_drawer.IsValid())
                {
                    EditorGUILayout.Space();
                    _drawer.Draw();
                }
            }
        }
    }
}