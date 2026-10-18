using System;

namespace Keel.RelayNet
{
    public readonly struct ClientUid : IComparable<ClientUid>, IEquatable<ClientUid>
    {
        public const int SIZE = sizeof(uint);
        public static readonly ClientUid Invalid = new(0u);

        public bool IsValid => _value > Invalid._value;
        private readonly uint _value;

        internal ClientUid(uint value) => _value = value;

        public static bool operator ==(ClientUid value0, ClientUid value1) => value0._value == value1._value;
        public static bool operator !=(ClientUid value0, ClientUid value1) => value0._value != value1._value;

        public static bool operator <(ClientUid value0, ClientUid value1) => value0._value < value1._value;
        public static bool operator >(ClientUid value0, ClientUid value1) => value0._value > value1._value;

        public static bool operator <=(ClientUid value0, ClientUid value1) => value0._value <= value1._value;
        public static bool operator >=(ClientUid value0, ClientUid value1) => value0._value >= value1._value;

        public static explicit operator uint(ClientUid value) => value._value;

        public int CompareTo(ClientUid other) =>
            _value.CompareTo(other._value);

        public bool Equals(ClientUid other) =>
            _value.Equals(other._value);

        public override bool Equals(object obj) =>
            obj is ClientUid other && Equals(other);

        public override string ToString() =>
            IsValid ? _value.ToString() : "INVALID";

        public override int GetHashCode() =>
            _value.GetHashCode();
    }
}