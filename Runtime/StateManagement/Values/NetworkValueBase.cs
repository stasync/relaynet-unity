using System;
using System.Collections.Generic;
using System.Reflection;
using Core.Networking;

namespace LROTech.Network.StateManagement.Values
{
    public abstract class SerializableValueBase
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
                if (typeof(SerializableValueBase).IsAssignableFrom(filedInfo.FieldType))
                    newFiledList.Add(filedInfo);
            }

            return newFiledList;
        }
    }

    public abstract class SerializableValueBase<TValue, TExposed> : SerializableValueBase where TValue : TExposed
    {
        protected SerializableValueBase(TValue defaultValue, Func<NetReader, bool, TValue> reader, Action<NetWriter, TValue> writer)
        {
            m_InternalValue = defaultValue;
            m_Reader = reader;
            m_Writer = writer;
        }

        private readonly Func<NetReader, bool, TValue> m_Reader;
        private readonly Action<NetWriter, TValue> m_Writer;

        public TExposed Value => InternalValue;
        public void Set(TValue value) => InternalValue = value;

        private TValue m_InternalValue;
        private TValue m_LastDeserializedValue;

        protected TValue InternalValue
        {
            get => m_InternalValue;
            set
            {
                if (!HasPermission)
                    throw new InvalidOperationException($"No authority to change value '{typeof(TValue).FullName}'.");

                m_InternalValue = value;
            }
        }

        internal override void InternalDeserialize(NetReader reader, bool firstTime)
        {
            m_LastDeserializedValue = m_Reader(reader, firstTime);
            m_InternalValue = m_LastDeserializedValue;
            
            OnDeserialize(reader, firstTime);
        }

        internal override void InternalSerialize(NetWriter writer)
        {
            m_Writer(writer, InternalValue);
            OnSerialize(writer);
        }

        protected virtual void OnDeserialize(NetReader reader, bool firstTime)
        {
        }

        protected virtual void OnSerialize(NetWriter writer)
        {
        }

        public int GetValueHashCode() =>
            m_InternalValue.GetHashCode();

        public override string ToString() =>
            m_InternalValue.ToString();
    }
}