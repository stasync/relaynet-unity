using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GameSparks.RT;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public sealed class GSTransformSync : GSInstanceBehaviour
{
    const string c_SetTransformRpcName = "SetTransform";

    [SerializeField, Range(0f, 1f)]
    float m_InterpolateMovement = 1f;
    [SerializeField, Range(0f, 1f)]
    float m_InterpolateRotation = 1f;
   
    Vector3 m_TargetPosition = Vector3.zero;
    Quaternion m_TargetRotation = Quaternion.identity;

    Vector3 m_LastPosition = Vector3.zero;
    Quaternion m_LastRotation = Quaternion.identity;

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

    private void OnEnable()
    {
        Identity.RegisterRPCHandler(c_SetTransformRpcName, SetTransformRpcHandler);
    }

    private void OnDisable()
    {
        Identity.UnregisterRPCHandler(c_SetTransformRpcName, SetTransformRpcHandler);
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

        AuthRpc(c_SetTransformRpcName,
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
        ResetTemporaryData();
    }

    private void ResetTemporaryData()
    {
        m_TargetPosition = transform.position;
        m_TargetRotation = transform.rotation;

        m_LastPosition = transform.position;
        m_LastRotation = transform.rotation;
    }

    private void FixedUpdate()
    {
        if (IsLocal)
        {
            if (!HasMoved(0.1f, 1f))
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
        var diff = (m_LastPosition - transform.position).sqrMagnitude;
        if (diff > posDiff)
            return true;

        // check if rotation has changed
        var angle = Quaternion.Angle(m_LastRotation, transform.rotation);
        if (angle > rotDiff)
            return true;

        return false;
    }

    protected override void DeserializeDirtyChecked(GSReader reader, bool firstTime)
    {
        if(firstTime)
        {
            transform.position = reader.ReadVector3();
            transform.rotation = reader.ReadQuaternion();
        }
        else
        {
            m_TargetPosition = reader.ReadVector3();
            m_TargetRotation = reader.ReadQuaternion();
        }
    }

    protected override void SerializeDirtyChecked(GSWriter writer)
    {
        writer.Write(transform.position);
        writer.Write(transform.rotation);
    }
}
