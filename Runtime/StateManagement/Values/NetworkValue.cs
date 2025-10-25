using Core.Networking;
using System;

namespace LROTech.Network.StateManagement
{
    public class SerializableValue<TValue> : SerializableValueDefinition<TValue, TValue>
    {
        public SerializableValue(TValue defaultValue, Func<NetReader, bool, TValue> reader, Action<NetWriter, TValue> writer) :
            base(defaultValue, reader, writer)
        {
        }
    }
}