using Core.Networking;

namespace LROTech.Network.StateManagement
{
    public readonly struct NetObjectStateSnapshot
    {
        public readonly ClientUid OwnerUID;
        public readonly NetObjectUid ObjectUID;
        public readonly string PrefabId;
        public readonly bool IsSceneObject;
        public readonly ObjectStateChangeRequestType StateChangeType;
        public readonly NetObjectRPCStateSnapshot RPCState;
        public readonly NetObjectDataCollectionSnapshot DataState;

        internal NetObjectStateSnapshot(NetReader reader)
        {
            OwnerUID = reader.ReadClientUid();
            ObjectUID = reader.ReadInstanceId();
            PrefabId = reader.ReadString();
            IsSceneObject = reader.ReadBoolean();
            StateChangeType = (ObjectStateChangeRequestType)reader.ReadByte();
            RPCState = NetObjectRPCStateSnapshot.FromReader(reader);
            DataState = NetObjectDataCollectionSnapshot.FromReader(reader);
        }

        internal NetObjectStateSnapshot(NetObjectState state)
        {
            OwnerUID = state.OwnerUid;
            ObjectUID = state.Uid;
            PrefabId = state.PrefabId;
            IsSceneObject = state.IsSceneObject;
            StateChangeType = state.StateChangeType;
            RPCState = state.RPCBuffer.ToSnapshot();
            DataState = state.DataCollection.ToSnapshot();
        }
        
        internal void Serialize(NetWriter writer)
        {
            writer.WriteClientUid(OwnerUID);
            writer.WriteInstanceId(ObjectUID);
            writer.WriteString(PrefabId);
            writer.WriteBool(IsSceneObject);
            writer.WriteByte((byte)StateChangeType);
            RPCState.Serialize(writer);
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
            result += sizeof(ObjectStateChangeRequestType); // StateChangeType
            result += RPCState.GetSize();
            result += DataState.GetSize();
            return result;
        }
    }
}