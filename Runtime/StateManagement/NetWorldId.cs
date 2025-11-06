using System;

namespace LROTech.Network.StateManagement
{
    public readonly struct NetWorldId : IComparable<NetWorldId>, IEquatable<NetWorldId>
    {
        public const int SIZE = sizeof(int);

        #region STATIC

        internal static NetWorldId Allocate() => new(++s_idCounter);
        internal static NetWorldId Invalid => new(0);
        private static int s_idCounter;

        #endregion

        private NetWorldId(int value) =>
            _value = value;

        private readonly int _value;

        public bool IsValid => _value > Invalid._value;

        public int CompareTo(NetWorldId other) => _value.CompareTo(other._value);
        public static implicit operator int(NetWorldId value) => value._value;

        public static bool operator ==(NetWorldId value0, NetWorldId value1) => value0._value == value1._value;
        public static bool operator !=(NetWorldId value0, NetWorldId value1) => value0._value != value1._value;

        public bool Equals(NetWorldId other) => _value.Equals(other._value);

        public override string ToString() =>
            IsValid ? _value.ToString() : "INVALID";

        public override bool Equals(object obj) =>
            _value.Equals(obj);

        public override int GetHashCode() =>
            _value.GetHashCode();
    }
}