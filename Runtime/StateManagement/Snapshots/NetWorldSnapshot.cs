using System.Collections.Generic;

public readonly struct NetWorldSnapshot
{
    public readonly float DeltaTime;
    public readonly NetWorldID UID;
    public readonly NetWorldID ID;
    public readonly float Time;
    public readonly float LocalTime;
    public IReadOnlyDictionary<NetObjectUID, NetObjectStateSnapshot> Objects => m_ObjectStates;
    private readonly Dictionary<NetObjectUID, NetObjectStateSnapshot> m_ObjectStates;

    public readonly NetWorldPlayersCollectionsSnapshot Players;

    private NetWorldSnapshot(float deltaTime, NetWorld world)
    {
        DeltaTime = deltaTime;
        UID = world.UID;
        ID = world.ID;
        Time = world.Time;
        LocalTime = world.LocalTime;

        m_ObjectStates = new Dictionary<NetObjectUID, NetObjectStateSnapshot>();
        foreach (var kvp in world.ObjectStates)
            m_ObjectStates.Add(kvp.Key, kvp.Value.ToSnapshot());

        Players = world.Players.ToSnapshot();
    }

    public static NetWorldSnapshot FromWorld(float deltaTime, NetWorld world) =>
        new(deltaTime, world);

    public int GetSize()
    {
        var size = 0;
        size += sizeof(float); // DeltaTime
        size += NetWorldID.Size; // Uid
        size += NetWorldID.Size; // ID
        size += sizeof(float); // Time
        size += sizeof(float); // Local time

        foreach (var o in m_ObjectStates.Values)
        {
            size += NetObjectUID.Size; // NetObjectUID
            size += o.GetSize();
        }

        size += Players.GetSize();
        return size;
    }
}