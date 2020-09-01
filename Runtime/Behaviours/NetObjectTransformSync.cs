using System.Collections.Generic;
using UnityEngine;

public class GSTransformSyncData : GSBehaviourAbstractData
{
    public SerializableVector3 Position { get; private set; } = null;
    public SerializableQuaternion Rotaiton { get; private set; } = null;
    public SerializableVector3 Velocity { get; private set; } = null;
    public SerializableFloat WorldTime { get; private set; } = null;
    public SerializableBool HasParent { get; private set; } = null;
    public SerializableValueCollection NestedTransformUnstructuredData { get; private set; } = null;

    protected override void OnDefineValues()
    {
        Position = new SerializableVector3(default, WritePemission);
        Rotaiton = new SerializableQuaternion(default, WritePemission);
        Velocity = new SerializableVector3(default, WritePemission);
        WorldTime = new SerializableFloat(default, WritePemission);
        HasParent = new SerializableBool(false, WritePemission);

        NestedTransformUnstructuredData = new SerializableValueCollection(() => !Initialized);
    }
}

[DisallowMultipleComponent]
public sealed class GSTransformSync : GSInstanceBehaviour<GSTransformSyncData>
{
    [System.Serializable]
    struct NestedTransfromData
    {
        [SerializeField]
        Transform m_Transform;
        public Transform Transform => m_Transform;
    }

    class NestedTransformSyncData
    {
        readonly NestedTransfromData m_NestedTransfromData;
        readonly SerializableVector3 m_PositionValue;
        readonly SerializableQuaternion m_RotationValue;

        public NestedTransformSyncData(NestedTransfromData data, SerializableVector3 positionValue, SerializableQuaternion rotationValue)
        {
            m_NestedTransfromData = data;
            m_PositionValue = positionValue;
            m_RotationValue = rotationValue;
        }

        public void OnUpdateWithAuthority()
        {
            m_PositionValue.Set(m_NestedTransfromData.Transform.localPosition);
            m_RotationValue.Set(m_NestedTransfromData.Transform.localRotation);
        }

        public void OnUpdateWithoutAuthority()
        {
            m_NestedTransfromData.Transform.localPosition = m_PositionValue.Value;
            m_NestedTransfromData.Transform.localRotation = m_RotationValue.Value;
        }
    }
    protected override bool UpdateSquenced => true;

    [SerializeField, Range(0f, 1f)]
    float m_InterpolateMovement = 1f;

    [SerializeField, Range(0f, 1f)]
    float m_InterpolateRotation = 1f;

    [SerializeField]
    bool m_Prediction = true;

    [SerializeField, Range(0.1f, 2f)]
    float m_PredictionMultipler = 1f;

    [SerializeField]
    List<NestedTransfromData> m_NestedTransformData = new List<NestedTransfromData>();

    List<NestedTransformSyncData> m_NestedTransformSyncData = new List<NestedTransformSyncData>();

    Vector3 m_LastFramePosition;
    Vector3 m_Velocity;

    protected override void OnDataValuesCollected()
    {
        base.OnDataValuesCollected();
        foreach(var nestedData in m_NestedTransformData)
        {
            if (nestedData.Transform == null)
                continue;

            var positionValue = new SerializableVector3(default, () => Data.HasPermission);
            var rotationValue = new SerializableQuaternion(default, () => Data.HasPermission);

            Data.NestedTransformUnstructuredData.Add($"{nestedData.Transform.gameObject.name}.localPosition", positionValue);
            Data.NestedTransformUnstructuredData.Add($"{nestedData.Transform.gameObject.name}.localRotation", rotationValue);

            m_NestedTransformSyncData.Add(new NestedTransformSyncData(nestedData, positionValue, rotationValue));
        }
    }

    public override void OnPostInitialize()
    {
        ResetTemporaryData();
    }

    void ResetTemporaryData()
    {
        m_LastFramePosition = transform.position;
        m_Velocity = Vector3.zero;

        if(!IsLocal)
        {
            transform.position = Data.Position.Value;
            transform.rotation = Data.Rotaiton.Value;
        }
    }

    void FixedUpdate()
    {
        if (!Identity.Initialized)
            return;

        var hasParentLocaly = transform.parent != transform.root && transform.parent != null;

        m_Velocity = transform.position - m_LastFramePosition;
        m_LastFramePosition = transform.position;

        if (IsLocal)
        {
            Data.Position.Set(transform.position);
            Data.Rotaiton.Set(transform.rotation);
            Data.Velocity.Set(m_Velocity);
            Data.WorldTime.Set(Identity.World.Time);
            Data.HasParent.Set(hasParentLocaly);

            foreach(var nestedTransformSync in m_NestedTransformSyncData)
                nestedTransformSync.OnUpdateWithAuthority();

            Identity.State.Properties.SetVector(0, transform.position);
        }
        else
        {
            if(!hasParentLocaly && !Data.HasParent.Value)
            {
                //Position interpolation
                if (m_InterpolateMovement > Mathf.Epsilon)
                {
                    var targetPosition = Data.Position.Value;
                    if (m_Prediction)
                        targetPosition += Data.Velocity.Value.normalized * (Identity.World.Time - Data.WorldTime.Value) * m_PredictionMultipler;

                    transform.position = Vector3.Lerp(
                            transform.position,
                            targetPosition,
                            Time.fixedDeltaTime * m_InterpolateMovement * 10);
                }
                else
                    transform.position = Data.Position.Value;

                //Rotation interpolation
                if (m_InterpolateRotation > Mathf.Epsilon)
                {
                    transform.rotation = Quaternion.Slerp(
                            transform.rotation,
                            Data.Rotaiton.Value,
                            Time.fixedDeltaTime * m_InterpolateRotation * 10);
                }
                else
                    transform.rotation = Data.Rotaiton.Value;
            }
            

            foreach (var nestedTransformSync in m_NestedTransformSyncData)
                nestedTransformSync.OnUpdateWithoutAuthority();
        }
    }
}
