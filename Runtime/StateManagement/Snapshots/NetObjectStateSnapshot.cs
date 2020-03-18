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
        var owner = reader.ReadConnectionUID();
        if(owner != OwnerUID)
            UnityEngine.Debug.LogError($"Deserialized owner {owner} doesn't match object owner {OwnerUID}");

        ObjectUID = reader.ReadInstanceId();
        PrefabId = reader.ReadString();
        var isSceneObject = reader.ReadBoolean();
        if(isSceneObject != IsSceneObject)
            UnityEngine.Debug.LogError($"Deserialized scene object state {isSceneObject} doesn't match object scene state {IsSceneObject}");

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
