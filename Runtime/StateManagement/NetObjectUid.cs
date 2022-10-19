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
    public const uint SCENE_OBJECT_RESERVED_UID_SPACE = 1000;
    public const uint FIRST_VALID_UID = 1;

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

    static NetObjectUID PeerStartId(ClientUID peerId) => FIRST_VALID_UID + SCENE_OBJECT_RESERVED_UID_SPACE + peerId * 10000;
    #endregion

    public NetObjectUID(ClientUID peerId)
    {
        //Check unallocated id
        if (s_UnAllocatedIds.Any())
        {
            m_Value = s_UnAllocatedIds.FirstOrDefault();
            s_UnAllocatedIds.Remove(m_Value);
        }
        //Allocate new id
        else
            m_Value = AllocateNewId(peerId);
    }

    /// <summary>
    /// This private constructor in use for casting.
    /// </summary>
    /// <param name="instanceId"></param>
    NetObjectUID(uint instanceId)
    {
        m_Value = instanceId;
    }

    uint m_Value;
    public const int Size = sizeof(uint);

    public bool IsValid => m_Value > Invalid.m_Value;
    public bool IsInvalid => !IsValid;
    public bool IsSceneObjectSpaceId => IsValid && m_Value <= SCENE_OBJECT_RESERVED_UID_SPACE;

    public int CompareTo(NetObjectUID other) => m_Value.CompareTo(other.m_Value);
    public static implicit operator uint(NetObjectUID value) => value.m_Value;
    public static implicit operator NetObjectUID(uint value) => new(value);

    public static bool operator ==(NetObjectUID value0, NetObjectUID value1) => value0.m_Value == value1.m_Value;
    public static bool operator !=(NetObjectUID value0, NetObjectUID value1) => value0.m_Value != value1.m_Value;

    public bool Equals(NetObjectUID other) => m_Value.Equals(other.m_Value);

    public override string ToString() => m_Value.ToString();
    public override bool Equals(object obj) => m_Value.Equals(obj);
    public override int GetHashCode() => m_Value.GetHashCode();
}
