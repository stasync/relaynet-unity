using System.Collections.Generic;

public struct NetWorldSnapshot
{
    public readonly float DeltaTime;
    public readonly NetWorldUID UID;
    public IReadOnlyDictionary<NetObjectUID, ObjectUpdateSnapshot> Objects => m_ObjectStates;
    readonly Dictionary<NetObjectUID, ObjectUpdateSnapshot> m_ObjectStates;

    public readonly NetWorldPlayersCollectionsSnapshot Players;

    public NetWorldSnapshot(float deltaTime, NetWorld world)
    {
        DeltaTime = deltaTime;
        UID = world.UID;

        m_ObjectStates = new Dictionary<NetObjectUID, ObjectUpdateSnapshot>();
        foreach (var kvp in world.ObjectStates)
            m_ObjectStates.Add(kvp.Key, kvp.Value.ToSnapshot());

        Players = world.Players.ToSnapshot();
    }
    public static NetWorldSnapshot FromWorld(float deltaTime, NetWorld world) => new NetWorldSnapshot(deltaTime, world);
}
