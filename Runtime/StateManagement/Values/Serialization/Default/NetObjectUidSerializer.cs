using Core.Networking;

namespace LROTech.Network.StateManagement.Values.Serialization.Default
{
    internal sealed class NetObjectUidSerializer : VariableSerializer<NetObjectUid>
    {
        public override void Serialize(NetWriter writer, NetObjectUid value) =>
            writer.WriteNetObjectUid(value);

        public override NetObjectUid Deserialize(NetReader reader) =>
            reader.ReadNetObjectUid();
    }
}