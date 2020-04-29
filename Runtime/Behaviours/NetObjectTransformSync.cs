using Core.Networking;
using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GSTransformSync : GSInstanceBehaviour
{
    [SerializeField, Range(0f, 1f)]
    float m_InterpolateMovement = 1f;
    [SerializeField, Range(0f, 1f)]
    float m_InterpolateRotation = 1f;

    [SerializeField, Range(0.001f, 1f)]
    float m_PositionThreshold = 0.025f;

    [SerializeField, Range(0.001f, 1f)]
    float m_RotationThreshold = 1f;

    [SerializeField]
    bool m_Prediction = true;

    [SerializeField, Range(0.1f, 2f)]
    float m_PredictionMultipler = 1f;

    Vector3 m_TargetPosition = Vector3.zero;
    Quaternion m_TargetRotation = Quaternion.identity;

    public void SetPosition(Vector3 position) => transform.localPosition = position;
    public void SetRotation(Quaternion rotation) => transform.localRotation = rotation;

    Vector3 m_LastFramePosition = Vector3.zero;
    Vector3 m_LastSendedPosition = Vector3.zero;
    Quaternion m_LastSendedRotation = Quaternion.identity;

    Vector3 m_MovementDirection = Vector3.zero;
    Transform m_LastParent;

    public bool SyncEnabled
    {
        get => m_SyncEnabled;
        set
        {
            if (!IsLocal)
                throw new Exception("Cannot change transform sync state on remote object");
            m_SyncEnabled = value;
        }
    }
    [SerializeField]
    bool m_SyncEnabled = true;

    /// <summary>
    /// Set position and rotation and send to other clients.
    /// Do not use in every frame to update transform.
    /// Use this only if you need one time position and rotation update.
    /// Example: Car enter/exit, teleports, etc.
    /// </summary>
    /// <param name="position"></param>
    /// <param name="rotation"></param>
    public void SetTransformAndSync(Vector3 position, Quaternion rotation)
    {
        if (!IsLocal)
            throw new Exception("Trying to set position and rotation of remote player object, this is not allowed.");

        var w = new NetWriter();
        w.Write(position);
        w.Write(rotation);

        OwnerToOthersRPC(SetTransformRpcHandler, w.ToArray());
    }

    void SetTransformRpcHandler(byte[] data)
    {
        var reader = new NetReader(data);
        var pos = reader.ReadVector3();
        var rot = reader.ReadQuaternion();

        transform.position = pos;
        transform.rotation = rot;

        ResetTemporaryData();
    }


    public override void OnPostInitialize()
    {
        Identity.RegisterRPCHandler(SetTransformRpcHandler);
        ResetTemporaryData();
    }

    void ResetTemporaryData()
    {
        m_TargetPosition = transform.localPosition;
        m_TargetRotation = transform.localRotation;

        m_LastSendedPosition = m_TargetPosition;
        m_LastSendedRotation = m_TargetRotation;

        m_MovementDirection = Vector3.zero;
    }

    public void ForceUpdate() => FixedUpdate();

    void FixedUpdate()
    {
        if (m_LastParent != transform.parent)
        {
            m_LastParent = transform.parent;
            ResetTemporaryData();
        }

        if (IsLocal)
        {
            m_MovementDirection = transform.position - m_LastFramePosition;
            m_LastFramePosition = transform.position;

            if (HasMoved(m_PositionThreshold, m_RotationThreshold))
            {
                m_LastSendedPosition = transform.position;
                m_LastSendedRotation = transform.rotation;
            }
        }
        else
        {
            //Position interpolation
            if (m_InterpolateMovement > Mathf.Epsilon)
            {
                SetPosition(Vector3.Lerp(
                        transform.localPosition,
                        m_TargetPosition,
                        Time.fixedDeltaTime * m_InterpolateMovement * 10));
            }
            else
                SetPosition(m_TargetPosition);

            //Rotation interpolation
            if (m_InterpolateRotation > Mathf.Epsilon)
            {
                SetRotation(Quaternion.Slerp(
                        transform.localRotation,
                        m_TargetRotation,
                        Time.fixedDeltaTime * m_InterpolateRotation * 10));
            }
            else
                SetRotation(m_TargetRotation);
        }
    }

    bool HasMoved(float posDiff, float rotDiff)
    {
        if (!SyncEnabled)
            return false;

        // check if position has changed
        var currentDirection = transform.position - m_LastSendedPosition;
        if (currentDirection.sqrMagnitude > posDiff)
            return true;

        // check if rotation has changed
        var angle = Quaternion.Angle(m_LastSendedRotation, transform.rotation);
        if (angle > rotDiff)
            return true;

        return false;
    }

    protected override void DeserializeDirtyChecked(NetReader reader, bool firstTime)
    {
        m_TargetPosition = reader.ReadVector3();
        m_TargetRotation = reader.ReadQuaternion();
        var hasParentRemotely = reader.ReadBoolean(); //If we has parent remotely, 
        if(hasParentRemotely)
        {
            //Read local position and rotation;
            var localPosition = reader.ReadVector3();
            var localRotation = reader.ReadQuaternion();

            if (transform.parent != null)
            {
                m_TargetPosition = localPosition;
                m_TargetRotation = localRotation;
            }
        }

        var remoteMovementDirection = reader.ReadVector3();
        var remoteSessionTime = reader.ReadSingle();

        if (firstTime)
        {
            SetPosition(m_TargetPosition);
            SetRotation(m_TargetRotation);
        }
        else if (m_Prediction)
            m_TargetPosition += remoteMovementDirection.normalized * (Network.Realtime.ServerTime - remoteSessionTime) * m_PredictionMultipler;
    }

    protected override void SerializeDirtyChecked(NetWriter writer)
    {
        m_TargetPosition = transform.localPosition;
        m_TargetRotation = transform.localRotation;

        writer.Write(transform.position);
        writer.Write(transform.rotation);
        var hasParent = transform.parent != null;
        writer.Write(hasParent);
        if(hasParent)
        {
            writer.Write(transform.localPosition);
            writer.Write(transform.localRotation);
        }

        writer.Write(m_MovementDirection);
        writer.Write(Network.Realtime.ServerTime);
    }
}
