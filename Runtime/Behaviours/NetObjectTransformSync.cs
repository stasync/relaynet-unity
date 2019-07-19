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

    [SerializeField, Range(0.1f, 2f)]
    float m_PredictionMultipler = 1f;

    [SerializeField]
    bool m_UseLocalTranformation = false;

    Vector3 m_TargetPosition = Vector3.zero;
    Quaternion m_TargetRotation = Quaternion.identity;

    Vector3 m_LastFramePosition = Vector3.zero;
    Vector3 m_LastPosition = Vector3.zero;
    Quaternion m_LastRotation = Quaternion.identity;

    Vector3 m_MovementDirection = Vector3.zero;

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

    public Vector3 Position { get { return m_UseLocalTranformation ? transform.localPosition : transform.position; } }
    public void SetPosition(Vector3 position)
    {
        if (m_UseLocalTranformation)
            transform.localPosition = position;
        else
            transform.position = position;
    }

    public Quaternion Rotation { get { return m_UseLocalTranformation ? transform.localRotation : transform.rotation; } }
    public void SetRotation(Quaternion rotation)
    {
        if (m_UseLocalTranformation)
            transform.localRotation = rotation;
        else
            transform.rotation = rotation;
    }

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

        OwnerToOthersRPC(SetTransformRpcHandler,
            new GSWriter()
                .Write(position)
                .Write(rotation)
                .AsArray());
    }

    void SetTransformRpcHandler(byte[] data)
    {
        var reader = new GSReader(data);
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
        m_TargetPosition = Position;
        m_TargetRotation = Rotation;

        m_LastPosition = Position;
        m_LastRotation = Rotation;

        m_MovementDirection = Vector3.zero;
    }

    void FixedUpdate()
    {
        if (IsLocal)
        {
            m_MovementDirection = Position - m_LastFramePosition;
            m_LastFramePosition = Position;

            if (!HasMoved(m_PositionThreshold, m_RotationThreshold))
                return;
            m_LastPosition = Position;
            m_LastRotation = Rotation;

            SetDirty();
        }
        else
        {
            //Position interpolation
            if (m_InterpolateMovement > Mathf.Epsilon)
            {
                SetPosition(Vector3.Lerp(
                        transform.position,
                        m_TargetPosition,
                        Time.fixedDeltaTime * m_InterpolateMovement * 10));
            }
            else
                SetPosition(m_TargetPosition);

            //Rotation interpolation
            if (m_InterpolateRotation > Mathf.Epsilon)
            {
                SetRotation(Quaternion.Slerp(
                        transform.rotation,
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
        var currentDirection = Position - m_LastPosition;
        if (currentDirection.sqrMagnitude > posDiff)
            return true;

        // check if rotation has changed
        var angle = Quaternion.Angle(m_LastRotation, Rotation);
        if (angle > rotDiff)
            return true;

        return false;
    }

    protected override void DeserializeDirtyChecked(GSReader reader, bool firstTime)
    {
        m_TargetPosition = reader.ReadVector3();
        m_TargetRotation = reader.ReadQuaternion();
        var remoteMovementDirection = reader.ReadVector3();
        var remoteSessionTime = reader.ReadSingle();

        if (firstTime)
        {
            SetPosition(m_TargetPosition);
            SetRotation(m_TargetRotation);
        }
        else if (m_Prediction)
            m_TargetPosition += remoteMovementDirection.normalized * (Network.Realtime.Time - remoteSessionTime) * m_PredictionMultipler;
    }

    protected override void SerializeDirtyChecked(GSWriter writer)
    {
        m_TargetPosition = Position;
        m_TargetRotation = Rotation;

        writer.Write(m_TargetPosition);
        writer.Write(m_TargetRotation);
        writer.Write(m_MovementDirection);
        writer.Write(Network.Realtime.Time);
    }
}
