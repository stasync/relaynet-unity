using Keel.Networking;

namespace Keel.RelayNet.StateManagement.Snapshots
{
    public readonly struct NetObjectStateSnapshot
    {
        public readonly ClientUid OwnerUid;
        public readonly NetObjectUid ObjectUid;
        public readonly uint PrefabUid;
        public readonly bool IsShared;
        public readonly float WorldTime;
        public readonly NetObjectStateChangeRequestType StateChangeType;
        public readonly NetObjectRpcBufferSnapshot RpcState;
        public readonly NetObjectDataCollectionSnapshot DataState;

        internal NetObjectStateSnapshot(NetReader reader)
        {
            OwnerUid = reader.ReadClientUid();
            ObjectUid = reader.ReadNetObjectUid();
            PrefabUid = reader.ReadUInt32();
            IsShared = reader.ReadBoolean();
            WorldTime = reader.ReadSingle();
            StateChangeType = (NetObjectStateChangeRequestType)reader.ReadByte();
            RpcState = new NetObjectRpcBufferSnapshot(reader);
            DataState = new NetObjectDataCollectionSnapshot(reader);
        }

        internal NetObjectStateSnapshot(NetObjectState state)
        {
            OwnerUid = state.OwnerUid;
            ObjectUid = state.Uid;
            PrefabUid = state.PrefabUid;
            IsShared = state.IsShared;
            WorldTime = state.WorldTime;
            StateChangeType = state.StateChangeType;
            RpcState = state.RpcBuffer.ToSnapshot();
            DataState = state.DataCollection.ToSnapshot();
        }
        
        internal void Serialize(NetWriter writer)
        {
            writer.WriteClientUid(OwnerUid);
            writer.WriteNetObjectUid(ObjectUid);
            writer.WriteUInt32(PrefabUid);
            writer.WriteBoolean(IsShared);
            writer.WriteSingle(WorldTime);
            writer.WriteByte((byte)StateChangeType);
            RpcState.Serialize(writer);
            DataState.Serialize(writer);
        }

        public override string ToString() =>
            $"{ObjectUid}:{PrefabUid} owner:{OwnerUid} s:{IsShared} st:{StateChangeType}";

        public int GetSize()
        {
            var result = 0;
            result += ClientUid.SIZE; // OwnerUid
            result += NetObjectUid.SIZE; // ObjectUID
            result += sizeof(uint); // PrefabId
            result += sizeof(bool); // IsShared
            result += sizeof(float); // Time
            result += sizeof(NetObjectStateChangeRequestType); // StateChangeType
            result += RpcState.GetSize();
            result += DataState.GetSize();
            return result;
        }
    }
}