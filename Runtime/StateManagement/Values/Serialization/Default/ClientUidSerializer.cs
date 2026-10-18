using Keel.Networking;

namespace Keel.RelayNet.StateManagement.Values.Serialization.Default
{
    internal sealed class ClientUidSerializer : VariableSerializer<ClientUid>
    {
        public override void Serialize(NetWriter writer, ClientUid value) => 
            writer.WriteClientUid(value);
        
        public override ClientUid Deserialize(NetReader reader) => 
            reader.ReadClientUid();
    }
}