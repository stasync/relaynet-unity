using System;
using Core.Networking;

namespace LROTech.Network.StateManagement.Values
{
    public abstract class NetworkValueManaged<TValue, TExposed> : NetworkValueBase where TValue : TExposed
    {
        public TExposed Value => InternalValue;
        protected TValue InternalValue => _internalValue;
        
        private TValue _internalValue;

        protected NetworkValueManaged(TValue defaultValue) =>
            _internalValue = defaultValue;

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