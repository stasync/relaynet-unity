using Core.Networking;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LROTech.Network.StateManagement.Values;
using UnityEngine;
using UnityEngine.Scripting;

namespace LROTech.Network.StateManagement
{
    [RequireDerived]
    public abstract class NetObjectStateData
    {
        internal bool HasWritePermission
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _writePermission != null && _writePermission();
        }

        internal int TypeHashCode
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set;
        }

        private Func<bool> _writePermission;
        private Func<bool> _validator;

        internal IReadOnlyCollection<NetworkValueBase> SerializableValues => _internalValueCollection.Values;
        private readonly Dictionary<string, NetworkValueBase> _internalValueCollection = new();

        private void Init(Func<bool> writePermission, Func<bool> validator)
        {
            _writePermission = writePermission;
            _validator = validator;
            TypeHashCode = GetTypeIndex(GetType());

            // Collect serializable values.
            foreach (var filedInfo in NetworkValueBase.GetFields(GetType()))
            {
                if (filedInfo.GetValue(this) is NetworkValueBase value)
                {
                    if (_internalValueCollection.ContainsKey(filedInfo.Name))
                        throw new InvalidOperationException($"'{GetType().FullName}' already contains value with name '{filedInfo.Name}'.");

                    value.InternalWritePermission = _writePermission;
                    value.InternalValidator = _validator;
                    _internalValueCollection.Add(filedInfo.Name, value);
                }
                else
                    Debug.LogError($"'{GetType().FullName}.{filedInfo.Name}' field is not assigned and will be skipped.");
            }
        }

        internal virtual int GetVersionHash()
        {
            var hash = TypeHashCode;
            foreach (var value in _internalValueCollection.Values)
                hash = hash * 7 + value.GetHashCode();

            return hash;
        }

        internal void SerializeInternal(NetWriter writer)
        {
            foreach (var serializableValue in _internalValueCollection.Values)
                serializableValue.InternalSerialize(writer);

            OnSerialize(writer);
        }

        protected virtual void OnSerialize(NetWriter writer)
        {
        }

        internal void DeserializeInternal(NetReader reader)
        {
            foreach (var value in _internalValueCollection.Values)
                value.InternalDeserialize(reader);

            OnDeserialize(reader);
        }

        protected virtual void OnDeserialize(NetReader reader)
        {
        }

        public override string ToString()
        {
            var result = $"'{GetType().FullName}'";
            foreach (var kvp in _internalValueCollection)
                result += $" {kvp.Key}:{kvp.Value}";

            return result;
        }

        internal static NetObjectStateData Provide(Type type, Func<bool> writePermission, Func<bool> validator)
        {
            var dataType = typeof(NetObjectStateData);
            if (!dataType.IsAssignableFrom(type) || type == dataType)
                throw new InvalidOperationException($"'{type.FullName} must derive from '{dataType.FullName}'.");

            var dataInstance = (NetObjectStateData)Activator.CreateInstance(type);
            dataInstance.Init(writePermission, validator);
            return dataInstance;
        }

        internal static T Provide<T>(Func<bool> writePermission, Func<bool> validator) where T : NetObjectStateData, new()
        {
            if (Provide(typeof(T), writePermission, validator) is not T instance)
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
            foreach (var kvp in _internalValueCollection)
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