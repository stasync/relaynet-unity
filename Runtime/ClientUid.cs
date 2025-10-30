using System;

namespace LROTech.Network.Sessions
{
    public readonly struct ClientUid : IComparable<ClientUid>, IEquatable<ClientUid>
    {
        public const int Size = sizeof(uint);
        public static readonly ClientUid Invalid = new(0u);

        public bool IsValid => m_Value > Invalid.m_Value;
        private readonly uint m_Value;

        internal ClientUid(uint value) => m_Value = value;

        public static bool operator ==(ClientUid value0, ClientUid value1) => value0.m_Value == value1.m_Value;
        public static bool operator !=(ClientUid value0, ClientUid value1) => value0.m_Value != value1.m_Value;

        public static bool operator <(ClientUid value0, ClientUid value1) => value0.m_Value < value1.m_Value;
        public static bool operator >(ClientUid value0, ClientUid value1) => value0.m_Value > value1.m_Value;
        
        public static bool operator <=(ClientUid value0, ClientUid value1) => value0.m_Value <= value1.m_Value;
        public static bool operator >=(ClientUid value0, ClientUid value1) => value0.m_Value >= value1.m_Value;
        
        public static explicit operator uint(ClientUid value) => value.m_Value;

        public int CompareTo(ClientUid other) =>
            m_Value.CompareTo(other.m_Value);

        public bool Equals(ClientUid other) =>
            m_Value.Equals(other.m_Value);

        public override bool Equals(object obj) =>
            obj is ClientUid other && Equals(other);

        public override string ToString() =>
            m_Value.ToString();

        public override int GetHashCode() =>
            m_Value.GetHashCode();
    }
}