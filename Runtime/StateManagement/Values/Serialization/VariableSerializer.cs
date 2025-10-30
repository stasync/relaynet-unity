using Core.Networking;
using UnityEngine.Scripting;

namespace LROTech.Network.StateManagement.Values.Serialization
{
    [RequireDerived]
    public abstract class VariableSerializer
    {
    }
    
    internal abstract class VariableSerializer<T> : VariableSerializer
    {
        public abstract void Serialize(NetWriter writer, T value);
        
        public abstract T Deserialize(NetReader reader);
    }
}