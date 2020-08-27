using UnityEngine;

public class GSTransformSyncData : GSBehaviourAbstractData
{
    public SerializableVector3 Position { get; private set; }
    public SerializableQuaternion Rotaiton { get; private set; }
    public SerializableVector3 Velocity { get; private set; }
    public SerializableFloat WorldTime { get; private set; }

    protected override void OnDefineValues()
    {
        Position = new SerializableVector3(default, WritePemission);
        Rotaiton = new SerializableQuaternion(default, WritePemission);
        Velocity = new SerializableVector3(default, WritePemission);
        WorldTime = new SerializableFloat(default, WritePemission);
    }
}

[DisallowMultipleComponent]
public sealed class GSTransformSync : GSInstanceBehaviour<GSTransformSyncData>
{
    [SerializeField, Range(0f, 1f)]
    float m_InterpolateMovement = 1f;

    [SerializeField, Range(0f, 1f)]
    float m_InterpolateRotation = 1f;

    [SerializeField]
    bool m_Prediction = true;

    [SerializeField, Range(0.1f, 2f)]
    float m_PredictionMultipler = 1f;

    Vector3 m_LastFramePosition;
    Vector3 m_Velocity;

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

        m_Velocity = transform.position - m_LastFramePosition;
        m_LastFramePosition = transform.position;

        if (IsLocal)
        {
            Data.Position.Set(transform.position);
            Data.Rotaiton.Set(transform.rotation);
            Data.Velocity.Set(m_Velocity);
            Data.WorldTime.Set(Identity.World.Time);

            Identity.State.Properties.SetVector(0, transform.position);
        }
        else
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
    }
}
