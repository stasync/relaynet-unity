using Core.Networking;

public struct NetObjectStateSnapshot
{
    public ClientUID OwnerUID { get; private set; }
    public NetObjectUID ObjectUID { get; private set; }
    public string PrefabId { get; private set; }
    public bool IsSceneObject { get; private set; }
    public ObjectStateChangeRequestType StateChangeType { get; private set; }
    public NetObjectRPCStateSnapshot RPCState { get; private set; }
    public NetObjectDataCollectionSnapshot DataState { get; private set; }

    void Restore(NetReader reader)
    {
        OwnerUID = reader.ReadConnectionUID();
        ObjectUID = reader.ReadInstanceId();
        PrefabId = reader.ReadString();
        IsSceneObject = reader.ReadBoolean();
        StateChangeType = (ObjectStateChangeRequestType)reader.ReadByte();
        RPCState = NetObjectRPCStateSnapshot.FromReader(reader);
        DataState = NetObjectDataCollectionSnapshot.FromReader(reader);
    }

    public void Serialize(NetWriter writer)
    {
        writer.WriteConnectionUID(OwnerUID);
        writer.WriteInstanceId(ObjectUID);
        writer.Write(PrefabId);
        writer.Write(IsSceneObject);
        writer.Write((byte)StateChangeType);
        RPCState.Serialize(writer);
        DataState.Serialize(writer);
    }

    public static NetObjectStateSnapshot FormReader(NetReader reader)
    {
        var snapshot = new NetObjectStateSnapshot();
        snapshot.Restore(reader);
        return snapshot;
    }

    public static NetObjectStateSnapshot FormState(NetObjectState state) =>
        new NetObjectStateSnapshot
        {
            OwnerUID = state.OwnerUID,
            ObjectUID = state.UID,
            PrefabId = state.PrefabId,
            IsSceneObject = state.IsSceneObject,
            StateChangeType = state.StateChangeType,
            RPCState = state.RPCBuffer.ToSnapshot(),
            DataState = state.DataCollection.ToSnapshot(),
        };

    public override string ToString() => $"{ObjectUID}:{PrefabId} owner:{OwnerUID} s:{IsSceneObject} st:{StateChangeType}";

    public int GetSize()
    {
        var result = 0;
        result += ClientUID.Size; // OwnerUID
        result += NetObjectUID.Size; // ObjectUID
        result += PrefabId.Length;
        result += sizeof(bool); // IsSceneObject
        result += sizeof(ObjectStateChangeRequestType); // StateChangeType
        result += RPCState.GetSize();
        result += DataState.GetSize();
        return result;
    }
}
