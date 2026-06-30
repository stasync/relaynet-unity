using System;

namespace LROTech.Network.StateManagement
{
    public readonly struct NetObjectUid : IComparable<NetObjectUid>, IEquatable<NetObjectUid>
    {
        public const int SIZE = sizeof(uint);

        /// <summary>
        /// This id's for network objects that located in a scene by default.
        /// If you create a scene object in runtime, they use the default peer-based ID space.
        /// 1000 means scene object in must be from 0 to 1000 (1000 inclusive).
        /// </summary>
        public const uint SCENE_OBJECT_RESERVED_UID_SPACE = 1000;

        public const uint FIRST_VALID_UID = 1;
        private const uint RESERVED_UID_SPACE_PER_CLIENT = 10000;

        #region STATIC

        public static readonly NetObjectUid Invalid = new(0u);
        private static uint s_idCounter;

        /// <summary>
        /// Reset when stop session.
        /// </summary>
        internal static void Reset() =>
            s_idCounter = 0;

        internal static NetObjectUid FromClientUid(ClientUid clientUid)
        {
            // Increment internal id.
            s_idCounter++;

            // Apply id offset.
            var value = FIRST_VALID_UID + SCENE_OBJECT_RESERVED_UID_SPACE + (uint)clientUid * RESERVED_UID_SPACE_PER_CLIENT + s_idCounter;
            return new NetObjectUid(value);
        }

        #endregion

        public bool IsValid => _value > Invalid._value;
        public bool IsSceneObjectSpaceId => IsValid && _value <= SCENE_OBJECT_RESERVED_UID_SPACE;

        private readonly uint _value;

        /// <summary>
        /// This private constructor in use for casting.
        /// </summary>
        /// <param name="value"></param>
        internal NetObjectUid(uint value) =>
            _value = value;

        public int CompareTo(NetObjectUid other) => _value.CompareTo(other._value);

        public static bool operator ==(NetObjectUid value0, NetObjectUid value1) => value0._value == value1._value;
        public static bool operator !=(NetObjectUid value0, NetObjectUid value1) => value0._value != value1._value;
        public static explicit operator uint(NetObjectUid value) => value._value;

        public bool Equals(NetObjectUid other) =>
            _value.Equals(other._value);

        public override string ToString() =>
            IsValid ? _value.ToString() : "INVALID";

        public override bool Equals(object obj) =>
            _value.Equals(obj);

        public override int GetHashCode() =>
            _value.GetHashCode();
    }
}