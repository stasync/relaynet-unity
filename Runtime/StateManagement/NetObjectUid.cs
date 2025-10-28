using System;
using LROTech.Network.Sessions;

namespace LROTech.Network.StateManagement
{
    public readonly struct NetObjectUid : IComparable<NetObjectUid>, IEquatable<NetObjectUid>
    {
        public const int Size = sizeof(uint);

        #region STATIC

        public static readonly NetObjectUid Invalid = new(0u);
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

        internal static NetObjectUid FromClientUid(ClientUid clientUid)
        {
            // Increment internal id.
            s_IdCounter++;

            // Apply id offset.
            var value = FIRST_VALID_UID + SCENE_OBJECT_RESERVED_UID_SPACE + (uint)clientUid * 10000 + s_IdCounter;
            return new NetObjectUid(value);
        }

        #endregion

        public bool IsValid => m_Value > Invalid.m_Value;
        public bool IsInvalid => !IsValid;
        public bool IsSceneObjectSpaceId => IsValid && m_Value <= SCENE_OBJECT_RESERVED_UID_SPACE;

        private readonly uint m_Value;

        /// <summary>
        /// This private constructor in use for casting.
        /// </summary>
        /// <param name="value"></param>
        internal NetObjectUid(uint value) =>
            m_Value = value;

        public int CompareTo(NetObjectUid other) => m_Value.CompareTo(other.m_Value);

        public static bool operator ==(NetObjectUid value0, NetObjectUid value1) => value0.m_Value == value1.m_Value;
        public static bool operator !=(NetObjectUid value0, NetObjectUid value1) => value0.m_Value != value1.m_Value;
        public static explicit operator uint(NetObjectUid value) => value.m_Value;

        public bool Equals(NetObjectUid other) =>
            m_Value.Equals(other.m_Value);

        public override string ToString() =>
            m_Value.ToString();

        public override bool Equals(object obj) =>
            m_Value.Equals(obj);

        public override int GetHashCode() =>
            m_Value.GetHashCode();
    }
}