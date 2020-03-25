using Core.Networking;
using System.Linq;

public struct ObjectUpdateSnapshot
{
    public ClientUID OwnerUID { get; private set; }
    public NetObjectUID ObjectUID { get; private set; }
    public string PrefabId { get; private set; }
    public bool IsSceneObject { get; private set; }
    public ObjectStateChangeRequestType StateChangeType { get; private set; }
    public byte[] Data { get; private set; }

    void Restore(NetReader reader)
    {
        OwnerUID = reader.ReadConnectionUID();
        ObjectUID = reader.ReadInstanceId();
        PrefabId = reader.ReadString();
        IsSceneObject = reader.ReadBoolean();
        StateChangeType = (ObjectStateChangeRequestType)reader.ReadByte();
        Data = reader.ReadBytesAndSize();
    }

    public void Serialize(NetWriter writer)
    {
        writer.WriteConnectionUID(OwnerUID);
        writer.WriteInstanceId(ObjectUID);
        writer.Write(PrefabId);
        writer.Write(IsSceneObject);
        writer.Write((byte)StateChangeType);
        writer.WriteBytesAndSize(Data);
    }

    public static ObjectUpdateSnapshot FormReader(NetReader reader)
    {
        var snapshot = new ObjectUpdateSnapshot();
        snapshot.Restore(reader);
        return snapshot;
    }

    public static ObjectUpdateSnapshot FormState(NetObjectState state) =>
        new ObjectUpdateSnapshot
        {
            OwnerUID = state.OwnerUID,
            ObjectUID = state.UID,
            PrefabId = state.PrefabId,
            IsSceneObject = state.IsSceneObject,
            StateChangeType = state.StateChangeType,
            Data = state.Data.ToArray()
        };

    public override string ToString() => $"{ObjectUID}:{PrefabId} owner:{OwnerUID} s:{IsSceneObject} st:{StateChangeType}";
}
