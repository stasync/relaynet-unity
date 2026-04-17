using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Core.Networking;
using UnityEngine;

namespace LROTech.Network.StateManagement.Values
{
    public abstract class NetworkValueBase
    {
        internal Func<bool> InternalWritePermission = null;
        internal Func<bool> InternalValidator = null;

        protected bool HasPermission
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => InternalWritePermission();
        }

        protected bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => InternalValidator();
        }

        internal abstract void InternalDeserialize(NetReader reader);
        internal abstract void InternalSerialize(NetWriter writer);

        private static readonly Dictionary<Type, IReadOnlyList<FieldInfo>> s_fieldCache = new();

        internal static IReadOnlyList<FieldInfo> GetFields(Type type)
        {
            if (s_fieldCache.TryGetValue(type, out var result))
                return result;

            var newFiledList = new List<FieldInfo>();
            s_fieldCache.Add(type, newFiledList);

            foreach (var filedInfo in type.GetFields(bindingAttr: BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (!typeof(NetworkValueBase).IsAssignableFrom(filedInfo.FieldType))
                    continue;

                if (!filedInfo.IsInitOnly)
                {
                    Debug.LogError($"Network field '{type.FullName}.{filedInfo.Name}' will be ignored as it has to be read-only. Please set 'readOnly' to keyword'");
                    continue;
                }

                newFiledList.Add(filedInfo);
            }

            return newFiledList;
        }
    }
}