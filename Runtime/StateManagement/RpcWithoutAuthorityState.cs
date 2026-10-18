using Keel.Networking;
using Keel.RelayNet.StateManagement.Snapshots;

namespace Keel.RelayNet.StateManagement
{
    public readonly struct RpcWithoutAuthorityState
    {
        public readonly NetObjectUid ObjectUid;
        public readonly NoAuthorityRpcType Type;
        public readonly NetObjectRpcState RpcState;

        public bool IsValid => ObjectUid.IsValid && RpcState.IsValid;

        internal RpcWithoutAuthorityState(NetObjectUid objectUid, NoAuthorityRpcType type, int hashCode, byte[] payload)
        {
            ObjectUid = objectUid;
            Type = type;
            RpcState = new NetObjectRpcState(hashCode, NetObjectRpcUtils.UidAllocator.NextUid(), payload);
        }

        internal RpcWithoutAuthorityState(NetReader reader)
        {
            ObjectUid = reader.ReadNetObjectUid();
            Type = (NoAuthorityRpcType)reader.ReadByte();
            RpcState = new NetObjectRpcState(new NetObjectRpcStateSnapshot(reader));
        }

        internal void Serialize(NetWriter writer)
        {
            writer.WriteNetObjectUid(ObjectUid);
            writer.WriteByte((byte)Type);
            RpcState.ToSnapshot().Serialize(writer);
        }
    }
}