using LROTech.Network.Debug.Editor;
using LROTech.Network.GameObjects;
using UnityEditor;

namespace LROTech.Network.Editor
{
    [CustomEditor(typeof(NetObjectIdentity), editorForChildClasses: true)]
    internal sealed class NetObjectIdentityEditor : UnityEditor.Editor
    {
        private NetObjectStateDrawer _drawer;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var identity = (NetObjectIdentity)target;

            var prefabUid = NetPrefabDatabase.Editor.GetPrefabUid(identity);
            NetPrefabDatabase.TryGetPrefab(prefabUid, out var prefab);

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField(label: $"Prefab ({prefabUid})", prefab, typeof(NetObjectIdentity), allowSceneObjects: false);
            EditorGUI.EndDisabledGroup();

            if (identity.State == null)
                return;

            _drawer ??= new NetObjectStateDrawer(identity.State, identity.World);
            if (!_drawer.IsValid())
                return;

            EditorGUILayout.Space();
            _drawer.Draw();
        }
    }
}