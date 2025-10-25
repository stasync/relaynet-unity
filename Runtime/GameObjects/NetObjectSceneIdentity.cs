using System;
using System.Collections.Generic;
using System.Linq;
using Core.Utils.Math;
using LROTech.Network.StateManagement;
using UnityEngine;

[DisallowMultipleComponent]
public class GSSceneObjectIdentity : GSInstanceBehaviour<GSSceneObjectIdentity.BehaviourData>
{
    public sealed class BehaviourData : NetObjectStateData
    {
        public BehaviourData(Func<bool> writePermission) : base(writePermission)
        {
        }
    }

    [SerializeField, Range(NetObjectUID.FIRST_VALID_UID, NetObjectUID.FIRST_VALID_UID + NetObjectUID.SCENE_OBJECT_RESERVED_UID_SPACE)]
    private int m_SceneObjectId;

    public NetObjectUID SceneObjectId => (uint)m_SceneObjectId;

    [SerializeField] private string m_PrefabId;
    public string PrefabID => m_PrefabId;

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

    protected override void OnDisconnected(ClientUID clientUid)
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

    public static implicit operator NetObjectIdentity(GSSceneObjectIdentity value) =>
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

        var sceneObjects = FindObjectsOfType<GSSceneObjectIdentity>();
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
                $"'{obj.gameObject.name}' and '{objectWithSameId.gameObject.name}' game objects has same scene id '{obj.m_SceneObjectId}'.");
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

        var sceneObjects = FindObjectsByType<GSSceneObjectIdentity>(FindObjectsSortMode.None);
        foreach (var sceneObjectIdentity in sceneObjects)
        {
            UnityEditor.Undo.RecordObject(sceneObjectIdentity, "Peek scene instance id");
            while (ObjectWithSameId(sceneObjects, sceneObjectIdentity) != null)
                sceneObjectIdentity.m_SceneObjectId =
                    RandomExtended.Default.Int(NetObjectUID.FIRST_VALID_UID, NetObjectUID.SCENE_OBJECT_RESERVED_UID_SPACE);
        }

        ValidateSceneObjects();
    }

    private static GSSceneObjectIdentity ObjectWithSameId(IEnumerable<GSSceneObjectIdentity> c, GSSceneObjectIdentity obj) => c
        .Where(o => o != obj)
        .FirstOrDefault(o => o.m_SceneObjectId == obj.m_SceneObjectId);
#endif
}