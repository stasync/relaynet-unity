using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Keel.Networking;
using UnityEngine;

namespace Keel.RelayNet.StateManagement.Values
{
    public abstract class NetworkValueBase
    {
        internal Func<bool> InternalWritePermission;
        internal Func<bool> InternalValidator;

        protected bool HasPermission
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => InternalWritePermission != null && InternalWritePermission();
        }

        protected bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => InternalValidator != null && InternalValidator();
        }

        internal abstract void InternalDeserialize(NetReader reader);
        internal abstract void InternalSerialize(NetWriter writer);

        public abstract int GetValueHashCode();

        private static readonly Dictionary<Type, IReadOnlyList<FieldInfo>> s_fieldCache = new();

        internal static IReadOnlyList<FieldInfo> GetFields(Type type)
        {
            if (s_fieldCache.TryGetValue(type, out var result))
                return result;

            var newFieldList = new List<FieldInfo>();
            s_fieldCache.Add(type, newFieldList);

            foreach (var fieldInfo in type.GetFields(bindingAttr: BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (!typeof(NetworkValueBase).IsAssignableFrom(fieldInfo.FieldType))
                    continue;

                if (!fieldInfo.IsInitOnly)
                {
                    Debug.LogError($"Network field '{type.FullName}.{fieldInfo.Name}' will be ignored as it has to be read-only. Mark it 'readonly'.");
                    continue;
                }

                newFieldList.Add(fieldInfo);
            }

            return newFieldList;
        }
    }
}