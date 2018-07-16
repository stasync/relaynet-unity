using System.Linq;
using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(GSInstanceIdentity))]
public class GSSceneObjectIdentity : GSInstanceBehaviour
{
    [SerializeField, Range(GSInstanceId.c_FirstValidId, GSInstanceId.c_SceneObjectReservedIdSpace)]
    int m_SceneObjectId;
    public int SceneObjectId { get { return m_SceneObjectId; } }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (UnityEditor.EditorApplication.isPlaying)
            return;

        var objects = FindObjectsOfType<GSSceneObjectIdentity>()
            .Where(o => o != this);

        var objectWithThisId = objects.FirstOrDefault(o => o.m_SceneObjectId == m_SceneObjectId);
        if (objectWithThisId == null)
            return;

        Debug.LogErrorFormat("{0} and {1} game objects has same scene id {2} ", gameObject.name, objectWithThisId, m_SceneObjectId);
    }
#endif

    /// <summary>
    /// We needs to register this object in object factory, if it placed in scene by default.
    /// </summary>
    private void Awake()
    {
        // Register scene object.
        // Initialize object if we on master client, so new clents will not initialize this objects, but will recieve updates.
        // TODO: fix situations when object can be not initialized.
        Network.Realtime.RegisterSceneObject(this);
    }

    public override void OnInitialze()
    {
    }

    protected override void SerializeDirtyChecked(GSWriter writer)
    {
    }

    protected override void DeserializeDirtyChecked(GSReader reader, bool firstTime)
    {
    }
}
