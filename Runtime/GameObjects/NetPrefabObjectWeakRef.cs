using System;
using UnityEngine;

namespace Keel.RelayNet.GameObjects
{
    [Serializable]
    public struct NetPrefabObjectWeakRef : IComparable<NetPrefabObjectWeakRef>, IEquatable<NetPrefabObjectWeakRef>
    {
        public bool IsValid => _value > 0;

        [SerializeField] internal uint _value;

        public int CompareTo(NetPrefabObjectWeakRef other) => _value.CompareTo(other._value);

        public static bool operator ==(NetPrefabObjectWeakRef value0, NetPrefabObjectWeakRef value1) => value0._value == value1._value;
        public static bool operator !=(NetPrefabObjectWeakRef value0, NetPrefabObjectWeakRef value1) => value0._value != value1._value;
        public static explicit operator uint(NetPrefabObjectWeakRef value) => value._value;

        public bool Equals(NetPrefabObjectWeakRef other) =>
            _value.Equals(other._value);

        public override string ToString() =>
            IsValid ? _value.ToString() : "INVALID";

        public override bool Equals(object obj) =>
            obj is NetPrefabObjectWeakRef other && Equals(other);

        public override int GetHashCode() =>
            _value.GetHashCode();
    }
}