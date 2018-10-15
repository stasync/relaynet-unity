using GameSparks.RT;
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

    Vector3 m_TargetPosition = Vector3.zero;
    Quaternion m_TargetRotation = Quaternion.identity;

    Vector3 m_LastPosition = Vector3.zero;
    Quaternion m_LastRotation = Quaternion.identity;

    Vector3 m_MovementDirection;

    public bool SyncEnabled
    {
        get { return m_SyncEnabled; }
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

        AuthRPC(RpcCode.SetTransform,
            new GSWriter()
                .Write(position)
                .Write(rotation)
                .AsData());
    }

    private void SetTransformRpcHandler(RTData data)
    {
        var reader = new GSReader(data);
        var pos = reader.ReadVector3();
        var rot = reader.ReadQuaternion();

        transform.position = pos;
        transform.rotation = rot;

        ResetTemporaryData();
    }

    public override void OnInitialze()
    {
        Identity.RegisterRPCHandler(RpcCode.SetTransform, SetTransformRpcHandler);
        ResetTemporaryData();
    }

    private void ResetTemporaryData()
    {
        m_TargetPosition = transform.position;
        m_TargetRotation = transform.rotation;

        m_LastPosition = transform.position;
        m_LastRotation = transform.rotation;

        m_MovementDirection = transform.forward;
    }

    private void FixedUpdate()
    {
        if (IsLocal)
        {
            if (!HasMoved(m_PositionThreshold, m_RotationThreshold))
                return;
            m_LastPosition = transform.position;
            m_LastRotation = transform.rotation;

            SetDirty();
        }
        else
        {
            //Position interpolation
            if (m_InterpolateMovement != 0f)
            {
                transform.position = Vector3.Lerp(
                        transform.position,
                        m_TargetPosition,
                        Time.fixedDeltaTime * m_InterpolateMovement * 10);
            }
            else
                transform.position = m_TargetPosition;

            //Rotation interpolation
            if (m_InterpolateRotation != 0f)
            {
                transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        m_TargetRotation,
                        Time.fixedDeltaTime * m_InterpolateRotation * 10);
            }
            else
                transform.rotation = m_TargetRotation;
        }
        
    }

    private bool HasMoved(float posDiff, float rotDiff)
    {
        if (!SyncEnabled)
            return false;

        // check if position has changed
        m_MovementDirection = transform.position - m_LastPosition;
        if (m_MovementDirection.sqrMagnitude > posDiff)
            return true;

        // check if rotation has changed
        var angle = Quaternion.Angle(m_LastRotation, transform.rotation);
        if (angle > rotDiff)
            return true;

        return false;
    }

    protected override void DeserializeDirtyChecked(GSReader reader, bool firstTime)
    {
        m_TargetPosition = reader.ReadVector3();
        m_TargetRotation = reader.ReadQuaternion();
        var remoteMovementDirection = reader.ReadVector3();
        var remoteSessionTime = reader.ReadFloat();

        if (m_Prediction)
            m_TargetPosition += remoteMovementDirection.normalized * (Network.Realtime.SessionTime - remoteSessionTime);

        if (firstTime)
        {
            transform.position = m_TargetPosition;
            transform.rotation = m_TargetRotation;
        }
    }

    protected override void SerializeDirtyChecked(GSWriter writer)
    {
        m_TargetPosition = transform.position;
        m_TargetRotation = transform.rotation;

        writer.Write(m_TargetPosition);
        writer.Write(m_TargetRotation);
        writer.Write(m_MovementDirection);
        writer.Write(Network.Realtime.SessionTime);
    }
}
