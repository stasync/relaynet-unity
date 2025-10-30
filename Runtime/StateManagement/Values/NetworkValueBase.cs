using System;
using System.Collections.Generic;
using System.Reflection;
using Core.Networking;

namespace LROTech.Network.StateManagement.Values
{
    public abstract class NetworkValueBase
    {
        internal Func<bool> InternalWritePermission = null;
        protected bool HasPermission => InternalWritePermission();

        internal abstract void InternalDeserialize(NetReader reader, bool firstTime);
        internal abstract void InternalSerialize(NetWriter writer);

        private static readonly Dictionary<Type, IReadOnlyList<FieldInfo>> _fieldCache = new();

        internal static IReadOnlyList<FieldInfo> GetFields(Type type)
        {
            if (_fieldCache.TryGetValue(type, out var result))
                return result;

            var newFiledList = new List<FieldInfo>();
            _fieldCache.Add(type, newFiledList);

            foreach (var filedInfo in type.GetFields(bindingAttr: BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (typeof(NetworkValueBase).IsAssignableFrom(filedInfo.FieldType))
                    newFiledList.Add(filedInfo);
            }

            return newFiledList;
        }
    }
    
    public abstract class NetworkValueBase<TValue, TExposed> : NetworkValueBase where TValue : TExposed
    {
        public TExposed Value => InternalValue;
        private TValue _internalValue;

        protected TValue InternalValue
        {
            get => _internalValue;
            set
            {
                if (!HasPermission)
                    throw new InvalidOperationException($"No authority to change value '{typeof(TValue).FullName}'.");

                _internalValue = value;
            }
        }

        protected NetworkValueBase(TValue defaultValue) =>
            _internalValue = defaultValue;
        
        public void Set(TValue value) => 
            InternalValue = value;

        internal override void InternalDeserialize(NetReader reader, bool firstTime) =>
            _internalValue = OnDeserialize(reader, firstTime);

        internal override void InternalSerialize(NetWriter writer) =>
            OnSerialize(writer);

        protected abstract TValue OnDeserialize(NetReader reader, bool firstTime);

        protected abstract void OnSerialize(NetWriter writer);

        public int GetValueHashCode() =>
            _internalValue.GetHashCode();

        public override string ToString() =>
            _internalValue.ToString();
    }
}