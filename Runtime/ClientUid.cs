using System;

namespace LROTech.Network.Sessions
{
    public readonly struct ClientUid : IComparable<ClientUid>, IEquatable<ClientUid>
    {
        public static readonly ClientUid Invalid = 0;

        private ClientUid(uint value) => m_Value = value;

        private readonly uint m_Value;
        public const int Size = sizeof(uint);

        public bool IsValid => m_Value > Invalid;

        public static implicit operator uint(ClientUid value) => value.m_Value;
        public static implicit operator ClientUid(uint value) => new(value);

        public int CompareTo(ClientUid other) => m_Value.CompareTo(other.m_Value);
        public bool Equals(ClientUid other) => m_Value.Equals(other.m_Value);

        public override string ToString() => m_Value.ToString();
        public override int GetHashCode() => m_Value.GetHashCode();
    }
}
