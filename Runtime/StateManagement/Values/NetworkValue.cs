using Core.Networking;
using System;

namespace LROTech.Network.StateManagment
{
    public class SerializableValue<TValue> : SerializableValueDefinition<TValue, TValue>
    {
        public SerializableValue(TValue defaultValue, Func<NetReader, bool, TValue> reader, Action<NetWriter, TValue> writer) :
            base(defaultValue, reader, writer)
        {
        }
    }
}