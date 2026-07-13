using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LROTech.Network.StateManagement
{
    public static class NetObjectRpcUtils
    {
        private static readonly Dictionary<MethodInfo, bool> s_methodValidityCache = new();
        private static readonly Dictionary<MethodInfo, int> s_rpcMethodToHashLookup = new();
        private static readonly Dictionary<Type, IReadOnlyList<MethodInfo>> s_autoRegisterRpcHandlerMethodsCache = new();

        /// <summary>
        /// Converts action to RPC code.
        /// </summary>
        /// <param name="a"></param>
        /// <returns>An RPC function hash</returns>
        internal static int ActionToHash(Action<byte[]> a)
        {
            if (!IsValidMethod(a.Method))
                throw new InvalidOperationException("Unable to create RPC code with anonymous delegates.");

            if (s_rpcMethodToHashLookup.TryGetValue(a.Method, out var result))
                return result;

            result = NameToHash($"{a.Method.DeclaringType!.FullName}.{a.Method.Name}");
            s_rpcMethodToHashLookup.Add(a.Method, result);
            return result;
        }

        private static bool IsValidMethod(MethodInfo method)
        {
            if (s_methodValidityCache.TryGetValue(method, out var result))
                return result;

            // Anonymous delegate method name usually looks like '<Foo>m__0';
            // TODO: More 'beautiful' way to know is delegate is anonymous.
            s_methodValidityCache.Add(method, result = !method.Name.StartsWith("<"));
            return result;
        }

        /// <summary>
        /// Calculates a stable hash code for the string.
        /// </summary>
        /// <param name="inputString"></param>
        private static int NameToHash(string inputString)
        {
            unchecked
            {
                var hash = inputString.Length + 32;
                for (var i = 0; i != inputString.Length; i++)
                    hash = (hash << 5) - hash + inputString[i];

                return hash;
            }
        }

        /// <summary>
        /// Backwards slow version to get method info by the hash.
        /// May return the valid <see cref="MethodInfo"/>, if it was registered once.
        /// Should only be used for debug matters.
        /// </summary>
        public static MethodInfo TryFindMethodInfoByHashSlow(int hash)
        {
            foreach (var kvp in s_rpcMethodToHashLookup)
            {
                if (kvp.Value == hash)
                    return kvp.Key;
            }

            return null;
        }

        public static IReadOnlyList<MethodInfo> GetAutoRegisterRpcHandlers(Type type)
        {
            if (s_autoRegisterRpcHandlerMethodsCache.TryGetValue(type, out var result))
                return result;

            var methodCollection = new List<MethodInfo>();
            s_autoRegisterRpcHandlerMethodsCache.Add(type, methodCollection);

            foreach (var methodInfo in type.GetMethods(bindingAttr: BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            {
                var attribute = methodInfo.GetCustomAttribute<AutoRegisterRpcHandler>();
                if (attribute == null)
                    continue;

                var parameters = methodInfo.GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != typeof(byte[]))
                {
                    Debug.LogError($"Invalid method info parameters for method '{methodInfo.Name}' that decorated with '{typeof(AutoRegisterRpcHandler).FullName}' on '{type.FullName}'.");
                    continue;
                }

                methodCollection.Add(methodInfo);
            }

            return methodCollection;
        }

        internal static class UidAllocator
        {
            private static uint s_counter;

            internal static uint NextUid() =>
                ++s_counter;
        }
    }
}