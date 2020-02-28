using Core.Networking;
using System.Linq;

public struct ObjectUpdateSnapshot
{
    public ClientUID Owner { get; private set; }
    public NetObjectUID ObjectUID { get; private set; }
    public string PrefabId { get; private set; }
    public bool IsSceneObject { get; private set; }
    public ObjectStateChangeRequestType StateChangeType { get; private set; }
    public byte[] Data { get; private set; }

    void Restore(NetReader reader)
    {
        ObjectUID = reader.ReadInstanceId();
        PrefabId = reader.ReadString();
        IsSceneObject = reader.ReadBoolean();
        StateChangeType = (ObjectStateChangeRequestType)reader.ReadByte();
        Data = reader.ReadBytesAndSize();
    }

    public static ObjectUpdateSnapshot FormReader(ClientUID sender, NetReader reader)
    {
        var snapshot = new ObjectUpdateSnapshot();
        snapshot.Restore(reader);
        return snapshot;
    }

    public static ObjectUpdateSnapshot FormState(NetObjectState state) =>
        new ObjectUpdateSnapshot
        {
            Owner = state.OwnerUID,
            ObjectUID = state.UID,
            PrefabId = state.PrefabId,
            IsSceneObject = state.IsSceneObject,
            StateChangeType = state.StateChangeType,
            Data = state.Data.ToArray()
        };
}
