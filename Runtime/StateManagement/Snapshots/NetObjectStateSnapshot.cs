using Core.Networking;
using System.Linq;

public struct NetObjectUpdateSnapshot
{
    public ClientUID OwnerUID { get; private set; }
    public NetObjectUID ObjectUID { get; private set; }
    public string PrefabId { get; private set; }
    public bool IsSceneObject { get; private set; }
    public ObjectStateChangeRequestType StateChangeType { get; private set; }
    public byte[] Data { get; private set; }
    public NetObjectRPCStateSnapshot RPCState { get; private set; }

    void Restore(NetReader reader)
    {
        OwnerUID = reader.ReadConnectionUID();
        ObjectUID = reader.ReadInstanceId();
        PrefabId = reader.ReadString();
        IsSceneObject = reader.ReadBoolean();
        StateChangeType = (ObjectStateChangeRequestType)reader.ReadByte();
        Data = reader.ReadBytesAndSize();
        RPCState = NetObjectRPCStateSnapshot.FromReader(reader);
    }

    public void Serialize(NetWriter writer)
    {
        writer.WriteConnectionUID(OwnerUID);
        writer.WriteInstanceId(ObjectUID);
        writer.Write(PrefabId);
        writer.Write(IsSceneObject);
        writer.Write((byte)StateChangeType);
        writer.WriteBytesAndSize(Data);
        RPCState.Serialize(writer);
    }

    public static NetObjectUpdateSnapshot FormReader(NetReader reader)
    {
        var snapshot = new NetObjectUpdateSnapshot();
        snapshot.Restore(reader);
        return snapshot;
    }

    public static NetObjectUpdateSnapshot FormState(NetObjectState state) =>
        new NetObjectUpdateSnapshot
        {
            OwnerUID = state.OwnerUID,
            ObjectUID = state.UID,
            PrefabId = state.PrefabId,
            IsSceneObject = state.IsSceneObject,
            StateChangeType = state.StateChangeType,
            Data = state.Data.ToArray(),
            RPCState = state.RPCBuffer.ToSnapshot()
        };

    public override string ToString() => $"{ObjectUID}:{PrefabId} owner:{OwnerUID} s:{IsSceneObject} st:{StateChangeType}";

    public int GetSize()
    {
        var result = 0;
        result += sizeof(uint); // OwnerUID
        result += sizeof(uint); // ObjectUID
        result += PrefabId.Length;
        result += sizeof(bool); // IsSceneObject
        result += 1; // StateChangeType
        result += Data.Length;
        result += RPCState.GetSize();
        return result;
    }
}
