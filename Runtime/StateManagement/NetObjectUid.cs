using System;
using System.Runtime.CompilerServices;

namespace Keel.RelayNet.StateManagement
{
    public readonly struct NetObjectUid : IComparable<NetObjectUid>, IEquatable<NetObjectUid>
    {
        public const int SIZE = sizeof(uint);
        public const uint FIRST_VALID_UID = 1;

        public static readonly NetObjectUid Invalid = new(0u);

        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _value > Invalid._value;
        }

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
            obj is NetObjectUid other && Equals(other);

        public override int GetHashCode() =>
            _value.GetHashCode();
    }
}