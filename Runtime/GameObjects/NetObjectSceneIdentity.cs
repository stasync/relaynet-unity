using System.Collections.Generic;
using System.Linq;
using Core.Utils.Math;
using LROTech.Network.Sessions;
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

        [FormerlySerializedAs("m_SceneObjectId")] 
        [SerializeField, Range(NetObjectUid.FIRST_VALID_UID, NetObjectUid.FIRST_VALID_UID + NetObjectUid.SCENE_OBJECT_RESERVED_UID_SPACE)]
        private int _sceneObjectId;

        public NetObjectUid SceneObjectId => new((uint)_sceneObjectId);

        [FormerlySerializedAs("m_PrefabId")] 
        [SerializeField] 
        private string _prefabId;
        public string PrefabID => _prefabId;

        /// <summary>
        /// We need to register this object in object factory, if it placed in scene by default.
        /// </summary>
        private void Start()
        {
            // Register scene object.
            // Initialize object if we on master client, so new clients will not initialize this objects, but will receive updates.
            // TODO: fix situations when object can be not initialized.
            TryRegisterOrInitialize();
        }

        protected override void OnDisconnected(ClientUid clientUid)
        {
            base.OnDisconnected(clientUid);
            if (Identity.World.Players.LocalPlayerIsMaster)
                TryRegisterOrInitialize();
        }

        private void TryRegisterOrInitialize()
        {
            if (!Identity.Initialized)
                Network.World.TryRegisterOrInitializeSceneObjectIdentity(sceneIdentity: this);
        }

        public static implicit operator NetObjectIdentity(NetObjectSceneIdentity value) =>
            value.Identity;

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
                    Debug.LogError($"{obj.gameObject.name} has ivalid id {obj.SceneObjectId} (Has to be fixed manually)");
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
                        RandomExtended.Default.Int(NetObjectUid.FIRST_VALID_UID, NetObjectUid.SCENE_OBJECT_RESERVED_UID_SPACE);
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