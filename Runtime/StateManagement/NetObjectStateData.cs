using Core.Networking;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LROTech.Network.StateManagement.Values;
using UnityEngine;

namespace LROTech.Network.StateManagement
{
    public abstract class NetObjectStateData
    {
        public bool HasWritePermission
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _writePermission != null && _writePermission();
        }

        public int TypeHashCode
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set;
        }

        private Func<bool> _writePermission;

        public IReadOnlyDictionary<string, SerializableValueBase> SerializableValues => m_InternalValueCollection;
        private readonly Dictionary<string, SerializableValueBase> m_InternalValueCollection = new();

        private void Init(Func<bool> writePermission)
        {
            _writePermission = writePermission;
            TypeHashCode = GetTypeIndex(GetType());

            // Collect serializable values.
            foreach (var filedInfo in SerializableValueBase.GetFields(GetType()))
            {
                if (filedInfo.GetValue(this) is SerializableValueBase value)
                {
                    if (m_InternalValueCollection.ContainsKey(filedInfo.Name))
                        throw new InvalidOperationException($"'{GetType().FullName}' already contains value with name '{filedInfo.Name}'.");

                    value.InternalWritePermission = _writePermission;
                    m_InternalValueCollection.Add(filedInfo.Name, value);
                }
                else
                    Debug.LogError($"'{GetType().FullName}.{filedInfo.Name}' field is not assigned and will be skipped.");
            }
        }

        internal virtual int GetVersionHash()
        {
            var hash = TypeHashCode;
            foreach (var value in m_InternalValueCollection.Values)
                hash = hash * 7 + value.GetHashCode();

            return hash;
        }

        internal void SerializeInternal(NetWriter writer)
        {
            foreach (var serializableValue in m_InternalValueCollection.Values)
                serializableValue.InternalSerialize(writer);

            OnSerialize(writer);
        }

        protected virtual void OnSerialize(NetWriter writer)
        {
        }

        internal void DeserializeInternal(NetReader reader, bool firstTime)
        {
            foreach (var value in m_InternalValueCollection.Values)
                value.InternalDeserialize(reader, firstTime);

            OnDeserialize(reader, firstTime);
        }

        protected virtual void OnDeserialize(NetReader reader, bool firstTime)
        {
        }

        public override string ToString()
        {
            var result = $"'{GetType().FullName}'";
            foreach (var kvp in m_InternalValueCollection)
                result += $" {kvp.Key}:{kvp.Value}";

            return result;
        }

        internal static NetObjectStateData Provide(Type type, Func<bool> writePermission)
        {
            var dataType = typeof(NetObjectStateData);
            if (!dataType.IsAssignableFrom(type))
                throw new InvalidOperationException($"'{type.FullName} must derive '{dataType.FullName}'.");

            var dataInstance = (NetObjectStateData)Activator.CreateInstance(type);
            dataInstance.Init(writePermission);
            return dataInstance;
        }

        internal static T Provide<T>(Func<bool> writePermission) where T : NetObjectStateData, new()
        {
            if (Provide(typeof(T), writePermission) is not T instance)
                throw new InvalidOperationException($"Unable to produce instance of data of type '{typeof(T).FullName}'.");

            return instance;
        }

        private static byte GetTypeIndex(Type dataType) =>
            Core.Utils.Types.Lookup<NetObjectStateData>.IndexOf(dataType);

        internal static int GetTypeIndex<TData>() where TData : NetObjectStateData, new() =>
            GetTypeIndex(typeof(TData));

        internal static bool TryGetType(byte index, out Type type) =>
            Core.Utils.Types.Lookup<NetObjectStateData>.WithIndex(index, out type);

#if UNITY_EDITOR
        public virtual IEnumerable<KeyValuePair<string, string>> GetValuesInfo_Editor()
        {
            foreach (var kvp in m_InternalValueCollection)
                yield return new KeyValuePair<string, string>(kvp.Key, kvp.Value.ToString());
        }

        public int GetSerializedSize_Editor()
        {
            var writer = new NetWriter();
            SerializeInternal(writer);
            return writer.Position;
        }
#endif
    }
}