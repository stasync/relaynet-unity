using Core.Networking;
using LROTech.Network.Sessions;

namespace LROTech.Network.StateManagement.Snapshots
{
    public readonly struct NetObjectStateSnapshot
    {
        public readonly ClientUid OwnerUID;
        public readonly NetObjectUid ObjectUID;
        public readonly string PrefabId;
        public readonly bool IsSceneObject;
        public readonly NetObjectStateChangeRequestType StateChangeType;
        public readonly NetObjectRpcBufferSnapshot RpcState;
        public readonly NetObjectDataCollectionSnapshot DataState;

        internal NetObjectStateSnapshot(NetReader reader)
        {
            OwnerUID = reader.ReadClientUid();
            ObjectUID = reader.ReadInstanceId();
            PrefabId = reader.ReadString();
            IsSceneObject = reader.ReadBoolean();
            StateChangeType = (NetObjectStateChangeRequestType)reader.ReadByte();
            RpcState = new NetObjectRpcBufferSnapshot(reader);
            DataState = new NetObjectDataCollectionSnapshot(reader);
        }

        internal NetObjectStateSnapshot(NetObjectState state)
        {
            OwnerUID = state.OwnerUid;
            ObjectUID = state.Uid;
            PrefabId = state.PrefabId;
            IsSceneObject = state.IsSceneObject;
            StateChangeType = state.StateChangeType;
            RpcState = state.RpcBuffer.ToSnapshot();
            DataState = state.DataCollection.ToSnapshot();
        }
        
        internal void Serialize(NetWriter writer)
        {
            writer.WriteClientUid(OwnerUID);
            writer.WriteInstanceId(ObjectUID);
            writer.WriteString(PrefabId);
            writer.WriteBool(IsSceneObject);
            writer.WriteByte((byte)StateChangeType);
            RpcState.Serialize(writer);
            DataState.Serialize(writer);
        }

        public override string ToString() =>
            $"{ObjectUID}:{PrefabId} owner:{OwnerUID} s:{IsSceneObject} st:{StateChangeType}";

        public int GetSize()
        {
            var result = 0;
            result += ClientUid.Size; // OwnerUid
            result += NetObjectUid.Size; // ObjectUID
            result += PrefabId.Length;
            result += sizeof(bool); // IsSceneObject
            result += sizeof(NetObjectStateChangeRequestType); // StateChangeType
            result += RpcState.GetSize();
            result += DataState.GetSize();
            return result;
        }
    }
}