using System;

namespace LROTech.Network.StateManagement
{
    public readonly struct NetWorldId : IComparable<NetWorldId>, IEquatable<NetWorldId>
    {
        public const int Size = sizeof(int);

        #region STATIC

        internal static NetWorldId Allocate() => new(++s_IdCounter);
        internal static NetWorldId Invalid => new(0);
        private static int s_IdCounter;

        #endregion

        private NetWorldId(int value) =>
            m_Value = value;

        private readonly int m_Value;

        public bool IsValid => m_Value > Invalid.m_Value;

        public int CompareTo(NetWorldId other) => m_Value.CompareTo(other.m_Value);
        public static implicit operator int(NetWorldId value) => value.m_Value;

        public static bool operator ==(NetWorldId value0, NetWorldId value1) => value0.m_Value == value1.m_Value;
        public static bool operator !=(NetWorldId value0, NetWorldId value1) => value0.m_Value != value1.m_Value;

        public bool Equals(NetWorldId other) => m_Value.Equals(other.m_Value);

        public override string ToString() => m_Value.ToString();
        public override bool Equals(object obj) => m_Value.Equals(obj);
        public override int GetHashCode() => m_Value.GetHashCode();
    }
}