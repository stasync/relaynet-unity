using System.Collections.Generic;
using System.Linq;
using Core.Utils.Math;
using LROTech.Network.StateManagement;
using UnityEngine;
using UnityEngine.Serialization;

namespace LROTech.Network.GameObjects
{
    [DisallowMultipleComponent]
    public sealed class NetObjectSceneIdentity : NetObjectBehaviour<NetObjectSceneIdentity.BehaviourData>
    {
        public sealed class BehaviourData : NetObjectStateData
        {
        }

        [FormerlySerializedAs("m_SceneObjectId")] [SerializeField, Range(NetObjectUid.FIRST_VALID_UID, NetObjectUid.FIRST_VALID_UID + NetWorldRuntime.SCENE_OBJECT_RESERVED_UID_SPACE)]
        private int _sceneObjectId;

        public NetObjectUid SceneObjectId => new((uint)_sceneObjectId);

        [FormerlySerializedAs("m_PrefabId")] [SerializeField]
        private string _prefabId;

        public string PrefabID => _prefabId;

#if UNITY_EDITOR
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