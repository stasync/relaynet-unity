using Core.Networking;
using System;
using LROTech.Network.StateManagement.Values;

namespace LROTech.Network.StateManagement
{
    public class SerializableValue<TValue> : SerializableValueBase_Legacy<TValue, TValue>
    {
        public SerializableValue(TValue defaultValue, Func<NetReader, bool, TValue> reader, Action<NetWriter, TValue> writer) :
            base(defaultValue, reader, writer)
        {
        }
    }
}