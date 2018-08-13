using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public struct GSInstanceId : IComparable<GSInstanceId>, IEquatable<GSInstanceId>
{
    #region STATIC
    public static GSInstanceId Invalid { get { return -1; } }
    static IList<int> s_UnAllocatedIds = new List<int>();
    static int s_IdCounter = 0;

    /// <summary>
    /// This id's for network objects that located in scene by default.
    /// If you create scene object in runtime, they use default peer-based id space.
    /// 1000 means scene object in must be from 0 to 1000 (1000 inclusive).
    /// </summary>
    public const int c_SceneObjectReservedIdSpace = 1000;
    public const int c_FirstValidId = 0;

    public static void UnAllocateId(GSInstanceId id)
    {
        if (s_UnAllocatedIds.Contains(id))
            throw new Exception("Trying to unallocate id, that already unallocated");
        s_UnAllocatedIds.Add(id);
    }

    /// <summary>
    /// Reset when stop session
    /// </summary>
    public static void Reset()
    {
        s_UnAllocatedIds = new List<int>();
        s_IdCounter = 0;
    }

    private static GSInstanceId AllocateNewId(GSPeerId peerId)
    {
        //Increment internal id
        s_IdCounter++;
        //Apply id offset
        return PeerStartId(peerId) + s_IdCounter;
    }

    private static int PeerStartId(GSPeerId peerId)
    {
        return c_FirstValidId + c_SceneObjectReservedIdSpace + peerId * 1000;
    }
    #endregion

    public GSInstanceId(GSPeerId peerId)
    {
        //Check unallocated id
        if (s_UnAllocatedIds.Any())
        {
            m_InstanceId = s_UnAllocatedIds.FirstOrDefault();
            s_UnAllocatedIds.Remove(m_InstanceId);
        }
        //Allocate new id
        else
            m_InstanceId = AllocateNewId(peerId);
    }

    /// <summary>
    /// This private constructor in use for casting.
    /// </summary>
    /// <param name="instanceId"></param>
    private GSInstanceId(int instanceId)
    {
        m_InstanceId = instanceId;
    }

    public bool IsValid { get { return m_InstanceId >= c_FirstValidId; } }

    public bool IsSceneObjectSpaceId { get { return IsValid && m_InstanceId <= c_SceneObjectReservedIdSpace; } }

    private int m_InstanceId;

    public int CompareTo(GSInstanceId other)
    {
        return m_InstanceId.CompareTo(other.m_InstanceId);
    }

    public static implicit operator int(GSInstanceId value)
    {
        return value.m_InstanceId;
    }

    public static implicit operator GSInstanceId(int value)
    {
        return new GSInstanceId(value);
    }

    public override string ToString()
    {
        return m_InstanceId.ToString();
    }

    public bool Equals(GSInstanceId other)
    {
        return m_InstanceId.Equals(other.m_InstanceId);
    }
}
