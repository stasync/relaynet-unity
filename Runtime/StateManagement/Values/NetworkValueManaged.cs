using System;
using System.Runtime.CompilerServices;
using Keel.Networking;

namespace LROTech.Network.StateManagement.Values
{
    public abstract class NetworkValueManaged<TValue, TExposed> : NetworkValueBase where TValue : TExposed
    {
        public TExposed Value
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => InternalValue;
        }

        protected TValue InternalValue
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => IsValid ? _internalValue : throw new InvalidOperationException($"Object of type '{GetType().FullName}' is invalid.");
        }

        private TValue _internalValue;

        protected NetworkValueManaged(TValue defaultValue) =>
            _internalValue = defaultValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(TValue value)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change value '{typeof(TValue).FullName}'.");

            _internalValue = value;
        }

        internal override void InternalDeserialize(NetReader reader) =>
            _internalValue = OnDeserialize(reader);

        internal override void InternalSerialize(NetWriter writer) =>
            OnSerialize(writer);

        protected abstract TValue OnDeserialize(NetReader reader);

        protected abstract void OnSerialize(NetWriter writer);

        public int GetValueHashCode() =>
            _internalValue.GetHashCode();

        public override string ToString() =>
            _internalValue.ToString();
    }
}