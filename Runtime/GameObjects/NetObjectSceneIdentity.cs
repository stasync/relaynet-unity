using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public class GSSceneObjectIdentity : GSInstanceBehaviour
{
    [SerializeField, Range(NetObjectUID.FIRST_VALID_UID, NetObjectUID.FIRST_VALID_UID + NetObjectUID.SCENE_OBJECT_RESERVED_UID_SPACE)]
    private int m_SceneObjectId;
    public NetObjectUID SceneObjectId => (uint)m_SceneObjectId;

    [SerializeField]
    private string m_PrefabId;
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

    public override void OnDisconnected(ClientUID clientUid)
    {
        base.OnDisconnected(clientUid);
        if (Network.Realtime.IsMasterPlayer)
            TryRegisterOrInitialize();
    }

    private void TryRegisterOrInitialize()
    {
        if (!Identity.Initialized)
            Network.Realtime.TryRegisterOrInitializeSceneObject(this);
    }

    public static implicit operator NetObjectIdentity(GSSceneObjectIdentity value) => 
        value.Identity;

#if UNITY_EDITOR
    [UnityEditor.MenuItem("System/Network/Validate scene objects")]
    static void ValidateSceneObjects()
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

            Debug.LogErrorFormat("{0} and {1} game objects has same scene id {2} ", obj.gameObject.name, objectWithSameId.gameObject.name, obj.m_SceneObjectId);
        }
        Debug.LogWarning("Validation finished");
    }

    [UnityEditor.MenuItem("System/Network/Fix scene objects")]
#pragma warning disable IDE0051 // Удалите неиспользуемые закрытые члены
    private static void FixSceneObjects()
#pragma warning restore IDE0051 // Удалите неиспользуемые закрытые члены
    {
        if (UnityEditor.EditorApplication.isPlaying)
        {
            Debug.LogWarning("Unable to validate in play mode");
            return;
        }

        var sceneObjects = FindObjectsOfType<GSSceneObjectIdentity>();

        foreach (var obj in sceneObjects)
        {
            UnityEditor.Undo.RecordObject(obj, "Peek scene instance id");
            while (ObjectWithSameId(sceneObjects, obj) != null)
                obj.m_SceneObjectId = RandomExtended.Default.Int(NetObjectUID.FIRST_VALID_UID, NetObjectUID.SCENE_OBJECT_RESERVED_UID_SPACE);
        }
        ValidateSceneObjects();
    }

    private static GSSceneObjectIdentity ObjectWithSameId(IEnumerable<GSSceneObjectIdentity> c, GSSceneObjectIdentity obj) => c
                .Where(o => o != obj)
                .FirstOrDefault(o => o.m_SceneObjectId == obj.m_SceneObjectId);
#endif
}
