using Keel.Networking;
using UnityEngine.Scripting;

namespace Keel.RelayNet.StateManagement.Values.Serialization
{
    [RequireDerived]
    internal abstract class VariableSerializer
    {
    }
    
    internal abstract class VariableSerializer<T> : VariableSerializer
    {
        public abstract void Serialize(NetWriter writer, T value);
        
        public abstract T Deserialize(NetReader reader);
    }
}