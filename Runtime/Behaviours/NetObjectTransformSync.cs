using System;
using System.Collections.Generic;
using Core.Networking;
using UnityEngine;

public class NetObjectTransformData : NetObjectStateData
{
    public NetObjectTransformData(Func<bool> writePermission) : base(writePermission) { }

    public SerializableVector3 Position { get; private set; } = null;
    public SerializableQuaternion Rotation { get; private set; } = null;
    public SerializableVector3 Velocity { get; private set; } = null;
    public SerializableFloat WorldTime { get; private set; } = null;
    public SerializableBool HasParent { get; private set; } = null;

    protected override void OnDefineValues()
    {
        base.OnDefineValues();

        Position = new SerializableVector3(default, WritePemission);
        Rotation = new SerializableQuaternion(default, WritePemission);
        Velocity = new SerializableVector3(default, WritePemission);
        WorldTime = new SerializableFloat(default, WritePemission);
        HasParent = new SerializableBool(false, WritePemission);
    }
}


[DisallowMultipleComponent]
public sealed class GSTransformSync : GSInstanceBehaviour<NetObjectTransformData>
{
    [SerializeField, Range(0f, 1f)]
    float m_InterpolateMovement = 1f;

    [SerializeField, Range(0f, 1f)]
    float m_InterpolateRotation = 1f;

    [SerializeField]
    bool m_Prediction = true;

    [SerializeField, Range(0.1f, 2f)]
    float m_PredictionMultipler = 1f;

    [SerializeField]
    List<Transform> m_NestedTransforms = new ();

    Vector3 m_LastFramePosition;
    Vector3 m_Velocity;

#if UNITY_EDITOR
    private void OnValidate()
    {
        for (var i = m_NestedTransforms.Count - 1; i >= 0; i--)
        {
            if (m_NestedTransforms[i].IsChildOf(transform))
                continue;

            m_NestedTransforms.RemoveAt(i);
            Debug.LogError($"'{m_NestedTransforms[i].gameObject.name}' is not a child of '{gameObject.name}'");
        }
    }
#endif

    public override void OnPostInitialize()
    {
        ResetTemporaryData();
    }

    public override void OnInitializeViewData()
    {
        base.OnInitializeViewData();
        if (IsLocal)
        {
            Data.Position.Set(transform.position);
            Data.Rotation.Set(transform.rotation);
        }
    }

    void ResetTemporaryData()
    {
        m_LastFramePosition = transform.position;
        m_Velocity = Vector3.zero;

        if(!IsLocal)
        {
            transform.position = Data.Position.Value;
            transform.rotation = Data.Rotation.Value;
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
            Data.Rotation.Set(transform.rotation);

            Data.Velocity.Set(m_Velocity);
            Data.WorldTime.Set(Identity.World.Time);
            Data.HasParent.Set(hasParentLocaly);
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
                            Data.Rotation.Value,
                            Time.fixedDeltaTime * m_InterpolateRotation * 10);
                }
                else
                    transform.rotation = Data.Rotation.Value;
            }
        }
    }

    protected override void SerializeDirtyChecked(NetWriter writer)
    {
        base.SerializeDirtyChecked(writer);

        foreach (var nestedTransform in m_NestedTransforms)
        {
            writer.Write(nestedTransform.localPosition);
            writer.Write(nestedTransform.localRotation);
        }
    }

    protected override void DeserializeDirtyChecked(NetReader reader, bool firstTime)
    {
        base.DeserializeDirtyChecked(reader, firstTime);

        foreach (var nestedTransform in m_NestedTransforms)
        {
            nestedTransform.localPosition = reader.ReadVector3();
            nestedTransform.localRotation = reader.ReadQuaternion();
        }
    }
}
