using Core.Networking;

namespace LROTech.Network.StateManagement.Snapshots
{
    public readonly struct NetObjectStateSnapshot
    {
        public readonly ClientUid OwnerUid;
        public readonly NetObjectUid ObjectUid;
        public readonly string PrefabId;
        public readonly bool IsSceneObject;
        public readonly float WorldTime;
        public readonly NetObjectStateChangeRequestType StateChangeType;
        public readonly NetObjectRpcBufferSnapshot RpcState;
        public readonly NetObjectDataCollectionSnapshot DataState;

        internal NetObjectStateSnapshot(NetReader reader)
        {
            OwnerUid = reader.ReadClientUid();
            ObjectUid = reader.ReadNetObjectUid();
            PrefabId = reader.ReadString();
            IsSceneObject = reader.ReadBoolean();
            WorldTime = reader.ReadSingle();
            StateChangeType = (NetObjectStateChangeRequestType)reader.ReadByte();
            RpcState = new NetObjectRpcBufferSnapshot(reader);
            DataState = new NetObjectDataCollectionSnapshot(reader);
        }

        internal NetObjectStateSnapshot(NetObjectState state)
        {
            OwnerUid = state.OwnerUid;
            ObjectUid = state.Uid;
            PrefabId = state.PrefabId;
            IsSceneObject = state.IsSceneObject;
            WorldTime = state.WorldTime;
            StateChangeType = state.StateChangeType;
            RpcState = state.RpcBuffer.ToSnapshot();
            DataState = state.DataCollection.ToSnapshot();
        }
        
        internal void Serialize(NetWriter writer)
        {
            writer.WriteClientUid(OwnerUid);
            writer.WriteNetObjectUid(ObjectUid);
            writer.WriteString(PrefabId);
            writer.WriteBoolean(IsSceneObject);
            writer.WriteSingle(WorldTime);
            writer.WriteByte((byte)StateChangeType);
            RpcState.Serialize(writer);
            DataState.Serialize(writer);
        }

        public override string ToString() =>
            $"{ObjectUid}:{PrefabId} owner:{OwnerUid} s:{IsSceneObject} st:{StateChangeType}";

        public int GetSize()
        {
            var result = 0;
            result += ClientUid.SIZE; // OwnerUid
            result += NetObjectUid.SIZE; // ObjectUID
            result += PrefabId.Length;
            result += sizeof(bool); // IsSceneObject
            result += sizeof(float); // Time
            result += sizeof(NetObjectStateChangeRequestType); // StateChangeType
            result += RpcState.GetSize();
            result += DataState.GetSize();
            return result;
        }
    }
}