using System;
using System.Collections.Generic;

namespace Keel.RelayNet.StateManagement.Values.Serialization
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
            foreach (var type in Keel.Utils.Types.Lookup<VariableSerializer>.Value)
            {
                var genericArguments = type.BaseType!.GetGenericArguments();
                var targetType = genericArguments[0];
                if (s_serializers.TryAdd(targetType, (VariableSerializer)Activator.CreateInstance(type))) 
                    continue;
                
                var alreadyRegisteredType = s_serializers[targetType].GetType();
                throw new InvalidOperationException(
                    $"Serializer '{type.FullName}' could not be registered for type '{targetType.FullName}'. It's already registered with type '{alreadyRegisteredType.FullName}'.");
            }
        }

        internal static VariableSerializer<T> GetSerializer<T>() where T : unmanaged
        {
            s_serializers.TryGetValue(typeof(T), out var serializer);
            return serializer as VariableSerializer<T>;
        }
    }
}