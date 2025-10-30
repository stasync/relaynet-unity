using Core.Networking;
using LROTech.Network.Sessions;

namespace LROTech.Network.StateManagement.Values.Serialization
{
    internal sealed class ClientUidSerializer : VariableSerializer<ClientUid>
    {
        public override void Serialize(NetWriter writer, ClientUid value) => 
            writer.WriteClientUid(value);
        
        public override ClientUid Deserialize(NetReader reader) => 
            reader.ReadClientUid();
    }
}