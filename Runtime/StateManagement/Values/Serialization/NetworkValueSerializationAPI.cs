using System;
using System.Collections.Generic;

namespace LROTech.Network.StateManagement.Values.Serialization
{
    internal static class NetworkValueSerializationAPI<T> where T : unmanaged
    {
        internal static readonly VariableSerializer<T> Serializer;

        static NetworkValueSerializationAPI()
        {
            var serializer = NetworkValueSerializationAPI.GetSerializer<T>();
            Serializer = serializer ?? new MemCpySerializer<T>();
        }
    }

    internal static class NetworkValueSerializationAPI
    {
        private static readonly Dictionary<Type, VariableSerializer> s_serializers = new();

        static NetworkValueSerializationAPI()
        {
            foreach (var type in Core.Utils.Types.Lookup<VariableSerializer>.Value)
            {
                foreach (var interfaceImpl in type.GetInterfaces())
                {
                    if (interfaceImpl != typeof(VariableSerializer<>))
                        continue;

                    var genericTypeDefinition = interfaceImpl.GetGenericTypeDefinition();
                    s_serializers.Add(genericTypeDefinition, (VariableSerializer)Activator.CreateInstance(type));
                }
            }
        }

        internal static VariableSerializer<T> GetSerializer<T>() where T : unmanaged
        {
            s_serializers.TryGetValue(typeof(T), out var serializer);
            return serializer as VariableSerializer<T>;
        }
    }
}