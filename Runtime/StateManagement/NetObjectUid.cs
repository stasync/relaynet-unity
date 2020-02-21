using System;
using System.Collections.Generic;
using System.Linq;

public struct NetObjectUID : IComparable<NetObjectUID>, IEquatable<NetObjectUID>
{
    #region STATIC
    public static NetObjectUID Invalid => 0;
    static IList<NetObjectUID> s_UnAllocatedIds = new List<NetObjectUID>();
    static uint s_IdCounter = 0;

    /// <summary>
    /// This id's for network objects that located in scene by default.
    /// If you create scene object in runtime, they use default peer-based id space.
    /// 1000 means scene object in must be from 0 to 1000 (1000 inclusive).
    /// </summary>
    public const uint c_SceneObjectReservedIdSpace = 1000;
    public const uint c_FirstValidId = 1;

    public static void UnAllocateId(NetObjectUID id)
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
        s_UnAllocatedIds = new List<NetObjectUID>();
        s_IdCounter = 0;
    }

    static NetObjectUID AllocateNewId(ClientUID peerId)
    {
        //Increment internal id
        s_IdCounter++;
        //Apply id offset
        return PeerStartId(peerId) + s_IdCounter;
    }

    static NetObjectUID PeerStartId(ClientUID peerId) => c_FirstValidId + c_SceneObjectReservedIdSpace + peerId * 1000;
    #endregion

    public NetObjectUID(ClientUID peerId)
    {
        //Check unallocated id
        if (s_UnAllocatedIds.Any())
        {
            m_InstanceIdValue = s_UnAllocatedIds.FirstOrDefault();
            s_UnAllocatedIds.Remove(m_InstanceIdValue);
        }
        //Allocate new id
        else
            m_InstanceIdValue = AllocateNewId(peerId);
    }

    /// <summary>
    /// This private constructor in use for casting.
    /// </summary>
    /// <param name="instanceId"></param>
    private NetObjectUID(uint instanceId)
    {
        m_InstanceIdValue = instanceId;
    }

    public bool IsValid => m_InstanceIdValue != Invalid;
    public bool IsSceneObjectSpaceId => IsValid && m_InstanceIdValue <= c_SceneObjectReservedIdSpace;

    uint m_InstanceIdValue;

    public int CompareTo(NetObjectUID other) => m_InstanceIdValue.CompareTo(other.m_InstanceIdValue);
    public static implicit operator uint(NetObjectUID value) => value.m_InstanceIdValue;
    public static implicit operator NetObjectUID(uint value) => new NetObjectUID(value);

    public static bool operator ==(NetObjectUID value0, NetObjectUID value1) => value0.m_InstanceIdValue == value1.m_InstanceIdValue;
    public static bool operator !=(NetObjectUID value0, NetObjectUID value1) => value0.m_InstanceIdValue != value1.m_InstanceIdValue;

    public bool Equals(NetObjectUID other) => m_InstanceIdValue.Equals(other.m_InstanceIdValue);

    public override string ToString() => m_InstanceIdValue.ToString();
    public override bool Equals(object obj) => m_InstanceIdValue.Equals(obj);
    public override int GetHashCode() => m_InstanceIdValue.GetHashCode();
}
