using System.Collections.Generic;

public struct NetWorldSnapshot
{
    public readonly float DeltaTime;
    public readonly NetWorldUID UID;
    public readonly float Time;
    public readonly float LocalTime;
    public IReadOnlyDictionary<NetObjectUID, ObjectUpdateSnapshot> Objects => m_ObjectStates;
    readonly Dictionary<NetObjectUID, ObjectUpdateSnapshot> m_ObjectStates;

    public readonly NetWorldPlayersCollectionsSnapshot Players;

    public NetWorldSnapshot(float deltaTime, NetWorld world)
    {
        DeltaTime = deltaTime;
        UID = world.UID;
        Time = world.Time;
        LocalTime = world.LocalTime;

        m_ObjectStates = new Dictionary<NetObjectUID, ObjectUpdateSnapshot>();
        foreach (var kvp in world.ObjectStates)
            m_ObjectStates.Add(kvp.Key, kvp.Value.ToSnapshot());

        Players = world.Players.ToSnapshot();
    }
    public static NetWorldSnapshot FromWorld(float deltaTime, NetWorld world) => new NetWorldSnapshot(deltaTime, world);
    public int GetSize()
    {
        var size = 0;
        size += sizeof(float); // DeltaTime
        size += sizeof(uint); // UID
        size += sizeof(float); // Time
        size += sizeof(float); // Local time

        foreach (var o in m_ObjectStates.Values)
        {
            size += sizeof(uint); // NetObjectUID
            size += o.GetSize();
        }

        size += Players.GetSize();
        return size;
    }
}
