using System;
using System.Collections.Generic;

public readonly struct NetObjectUID : IComparable<NetObjectUID>, IEquatable<NetObjectUID>
{
    #region STATIC
    public static readonly NetObjectUID Invalid =0;
    private static uint s_IdCounter;

    /// <summary>
    /// This id's for network objects that located in scene by default.
    /// If you create scene object in runtime, they use default peer-based id space.
    /// 1000 means scene object in must be from 0 to 1000 (1000 inclusive).
    /// </summary>
    public const uint SCENE_OBJECT_RESERVED_UID_SPACE = 1000;
    public const uint FIRST_VALID_UID = 1;

    /// <summary>
    /// Reset when stop session.
    /// </summary>
    internal static void Reset() =>
        s_IdCounter = 0;
    
    private static NetObjectUID AllocateNewId(ClientUID clientUid)
    {
        // Increment internal id.
        s_IdCounter++;
        // Apply id offset.
        return ClientStartId(clientUid) + s_IdCounter;
    }

    private static NetObjectUID ClientStartId(ClientUID clientUid) => 
        FIRST_VALID_UID + SCENE_OBJECT_RESERVED_UID_SPACE + clientUid * 10000;
    #endregion

    public NetObjectUID(ClientUID clientUid) =>
        m_Value = AllocateNewId(clientUid);

    /// <summary>
    /// This private constructor in use for casting.
    /// </summary>
    /// <param name="value"></param>
    private NetObjectUID(uint value) =>
        m_Value = value;
    
    private readonly uint m_Value;
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
