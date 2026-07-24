using System.Collections.Generic;
using System.Linq;
using Core.Utils.Math;
using LROTech.Network.StateManagement;
using UnityEngine;

namespace LROTech.Network.GameObjects
{
    [DisallowMultipleComponent]
    public sealed class NetObjectSceneIdentity : NetObjectBehaviour<NetObjectSceneIdentity.BehaviourData>
    {
        public sealed class BehaviourData : NetObjectStateData
        {
        }

        [field: HideInInspector]
        [field: SerializeField]
        internal uint PrefabUid { get; private set; }

        [SerializeField, Range(NetObjectUid.FIRST_VALID_UID, NetObjectUid.FIRST_VALID_UID + NetWorldRuntime.SCENE_OBJECT_RESERVED_UID_SPACE)]
        private int _sceneObjectId;

        public NetObjectUid SceneObjectId => new((uint)_sceneObjectId);

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Regenerates the baked id whenever the asset is edited/imported in the Editor.
            // At runtime (player build) this method is not compiled and the serialized value is used.

            var newPrefabUid = 0U;
            // Only run this code for prefabs.
            if (!gameObject.scene.IsValid())
                newPrefabUid = NetPrefabDatabase.Editor.GetPrefabUid(Identity);

            // A 0 result means "do not touch" (play-mode transition, unsaved object, etc.)
            if (newPrefabUid != 0 && newPrefabUid != PrefabUid)
            {
                PrefabUid = newPrefabUid;
                UnityEditor.EditorUtility.SetDirty(target: this);
            }
        }

        [UnityEditor.MenuItem("System/Network/Validate scene objects")]
        private static void ValidateSceneObjects()
        {
            if (UnityEditor.EditorApplication.isPlaying)
            {
                Debug.LogWarning("Unable to validate in play mode");
                return;
            }

            var sceneObjects = FindObjectsOfType<NetObjectSceneIdentity>();
            foreach (var obj in sceneObjects)
            {
                if (!obj.SceneObjectId.IsValid)
                {
                    Debug.LogError($"{obj.gameObject.name} has invalid id {obj.SceneObjectId} (Has to be fixed manually)");
                    continue;
                }

                var objectWithSameId = ObjectWithSameId(sceneObjects, obj);

                if (objectWithSameId == null)
                    continue;

                Debug.LogErrorFormat(
                    $"'{obj.gameObject.name}' and '{objectWithSameId.gameObject.name}' game objects has same scene id '{obj._sceneObjectId}'.");
            }

            Debug.LogWarning("Validation finished");
        }

        [UnityEditor.MenuItem("System/Network/Fix scene objects")]
        private static void FixSceneObjects()
        {
            if (UnityEditor.EditorApplication.isPlaying)
            {
                Debug.LogWarning("Unable to validate in play mode");
                return;
            }

            var sceneObjects = FindObjectsByType<NetObjectSceneIdentity>(FindObjectsSortMode.None);
            foreach (var sceneObjectIdentity in sceneObjects)
            {
                UnityEditor.Undo.RecordObject(sceneObjectIdentity, "Peek scene instance id");
                while (ObjectWithSameId(sceneObjects, sceneObjectIdentity) != null)
                    sceneObjectIdentity._sceneObjectId =
                        RandomExtended.Default.Int((int)NetObjectUid.FIRST_VALID_UID, (int)NetWorldRuntime.SCENE_OBJECT_RESERVED_UID_SPACE);

                UnityEditor.EditorUtility.SetDirty(sceneObjectIdentity);
            }

            ValidateSceneObjects();
        }

        private static NetObjectSceneIdentity ObjectWithSameId(IEnumerable<NetObjectSceneIdentity> sceneObjects, NetObjectSceneIdentity target) =>
            sceneObjects
                .Where(o => o != target)
                .FirstOrDefault(o => o._sceneObjectId == target._sceneObjectId);
#endif
    }
}