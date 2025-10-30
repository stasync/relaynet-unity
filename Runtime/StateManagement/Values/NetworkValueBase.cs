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
                if (typeof(NetworkValueBase).IsAssignableFrom(filedInfo.FieldType))
                    newFiledList.Add(filedInfo);
            }

            return newFiledList;
        }
    }
}