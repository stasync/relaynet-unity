using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public class GSSceneObjectIdentity : GSInstanceEmptyBehaviour
{
    [SerializeField, Range(GSInstanceId.c_FirstValidId, GSInstanceId.c_FirstValidId + GSInstanceId.c_SceneObjectReservedIdSpace)]
    int m_SceneObjectId;
    public GSInstanceId SceneObjectId => (uint)m_SceneObjectId;

    /// <summary>
    /// We needs to register this object in object factory, if it placed in scene by default.
    /// </summary>
    void Awake()
    {
        // Register scene object.
        // Initialize object if we on master client, so new clents will not initialize this objects, but will recieve updates.
        // TODO: fix situations when object can be not initialized.
        Network.Realtime.TryRegisterOrInitializeSceneObject(this);
    }

    public override void OnDisconnected(ClientUID peedId)
    {
        base.OnDisconnected(peedId);
        if (Identity.Initialized || !Network.Realtime.IsMasterPlayer)
            return;

        Network.Realtime.TryRegisterOrInitializeSceneObject(this);
    }

    public override void OnPostInitialize()
    {
    }

    public static implicit operator GSInstanceIdentity(GSSceneObjectIdentity value) => value.Identity;

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Network/Validate scene objects")]
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
            var objectWithSameId = ObjectWithSameId(sceneObjects, obj);

            if (objectWithSameId == null)
                continue;

            Debug.LogErrorFormat("{0} and {1} game objects has same scene id {2} ", obj.gameObject.name, objectWithSameId.gameObject.name, obj.m_SceneObjectId);
        }
        Debug.LogWarning("Validation finished");
    }

    [UnityEditor.MenuItem("Network/Fix scene objects")]
    static void FixSceneObjects()
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
                obj.m_SceneObjectId = RandomExtended.Default.Int(GSInstanceId.c_FirstValidId, GSInstanceId.c_SceneObjectReservedIdSpace);
        }
        ValidateSceneObjects();
    }

    static GSSceneObjectIdentity ObjectWithSameId(IEnumerable<GSSceneObjectIdentity> c, GSSceneObjectIdentity obj) => c
                .Where(o => o != obj)
                .FirstOrDefault(o => o.m_SceneObjectId == obj.m_SceneObjectId);
#endif
}
