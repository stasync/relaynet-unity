using System.Collections.Generic;
using Keel.RelayNet.StateManagement;
using UnityEngine;

namespace Keel.RelayNet.GameObjects
{
    /// <summary>
    /// Marks a networked object that is placed in a scene, instead of created at runtime. Every client loads the same
    /// scene instance; the master registers it in the world, and the other clients bind their copy to that state.
    /// The object must be an instance of a network prefab, and its scene id must be unique among the scenes loaded together
    /// (see Tools > RelayNet > Fix scene objects). Scene objects are shared: they stay when their owner leaves.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetObjectSceneIdentity : NetObjectBehaviour<NetObjectSceneIdentity.BehaviourData>
    {
        public sealed class BehaviourData : NetObjectStateData
        {
        }

        private const int MAX_SCENE_OBJECT_ID = (int)(NetObjectUid.FIRST_VALID_UID + NetWorldRuntime.SCENE_OBJECT_RESERVED_UID_SPACE);

        [field: HideInInspector]
        [field: SerializeField]
        internal uint PrefabUid { get; private set; }

        [SerializeField, Range(NetObjectUid.FIRST_VALID_UID, MAX_SCENE_OBJECT_ID)]
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

        [UnityEditor.MenuItem("Tools/RelayNet/Validate scene objects")]
        private static void ValidateSceneObjects()
        {
            if (UnityEditor.EditorApplication.isPlaying)
            {
                Debug.LogWarning("Scene objects can't be validated in play mode.");
                return;
            }

            var objectsById = new Dictionary<int, NetObjectSceneIdentity>();
            var problemCount = 0;
            foreach (var sceneObject in FindSceneObjects())
            {
                if (!HasValidId(sceneObject))
                {
                    Debug.LogError($"'{sceneObject.gameObject.name}' has the invalid scene id {sceneObject._sceneObjectId}. Use Tools > RelayNet > Fix scene objects.", sceneObject);
                    problemCount++;
                }
                else if (objectsById.TryGetValue(sceneObject._sceneObjectId, out var otherObject))
                {
                    Debug.LogError($"'{sceneObject.gameObject.name}' and '{otherObject.gameObject.name}' have the same scene id {sceneObject._sceneObjectId}. Use Tools > RelayNet > Fix scene objects.", sceneObject);
                    problemCount++;
                }
                else
                    objectsById.Add(sceneObject._sceneObjectId, sceneObject);
            }

            Debug.Log(problemCount == 0
                ? $"All {objectsById.Count} scene objects have valid, unique ids."
                : $"{problemCount} scene object(s) have invalid or duplicate ids.");
        }

        /// <summary>
        /// Gives every scene object of the loaded scenes a valid, unique id. Valid unique ids never change. When objects share
        /// an id, the first one in a stable order (by GlobalObjectId) keeps it. Every other object gets the lowest free id, in
        /// the same order, so running it twice on the same scenes gives the same result.
        /// </summary>
        [UnityEditor.MenuItem("Tools/RelayNet/Fix scene objects")]
        private static void FixSceneObjects()
        {
            if (UnityEditor.EditorApplication.isPlaying)
            {
                Debug.LogWarning("Scene objects can't be fixed in play mode.");
                return;
            }

            var usedIds = new HashSet<int>();
            var objectsToFix = new List<NetObjectSceneIdentity>();
            foreach (var sceneObject in FindSceneObjects())
            {
                if (!HasValidId(sceneObject) || !usedIds.Add(sceneObject._sceneObjectId))
                    objectsToFix.Add(sceneObject);
            }

            var nextId = (int)NetObjectUid.FIRST_VALID_UID;
            foreach (var sceneObject in objectsToFix)
            {
                while (usedIds.Contains(nextId))
                    nextId++;

                if (nextId > MAX_SCENE_OBJECT_ID)
                {
                    Debug.LogError($"No free scene id left for '{sceneObject.gameObject.name}'. Scenes loaded together can hold {MAX_SCENE_OBJECT_ID} scene objects.", sceneObject);
                    break;
                }

                UnityEditor.Undo.RecordObject(sceneObject, "Fix scene object id");
                sceneObject._sceneObjectId = nextId;
                usedIds.Add(nextId);
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(sceneObject);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(sceneObject.gameObject.scene);
            }

            ValidateSceneObjects();
        }

        private static bool HasValidId(NetObjectSceneIdentity sceneObject) =>
            sceneObject._sceneObjectId >= NetObjectUid.FIRST_VALID_UID && sceneObject._sceneObjectId <= MAX_SCENE_OBJECT_ID;

        /// <summary>
        /// The scene objects of every loaded scene, inactive ones included, in a stable order.
        /// </summary>
        private static List<NetObjectSceneIdentity> FindSceneObjects()
        {
            var sceneObjects = new List<NetObjectSceneIdentity>();
            foreach (var sceneObject in FindObjectsByType<NetObjectSceneIdentity>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (sceneObject.gameObject.scene.IsValid())
                    sceneObjects.Add(sceneObject);
            }

            sceneObjects.Sort((a, b) => string.CompareOrdinal(
                UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(a).ToString(),
                UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(b).ToString()));

            return sceneObjects;
        }
#endif
    }
}
