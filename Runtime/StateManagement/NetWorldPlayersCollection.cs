using System;
using System.Collections.Generic;
using System.Linq;

public class NetWorldPlayersCollection
{
    ClientUID m_LocalPlayerUID;
    ClientUID m_MasterClientUID;

    public IEnumerable<ClientUID> PlayerUIDs => m_PlayersCollection.Keys;
    Dictionary<ClientUID, NetWorldPlayer> m_PlayersCollection;
    public IReadOnlyCollection<ClientUID> RemovedPlayers => m_JustRemovedPlayers;
    HashSet<ClientUID> m_JustRemovedPlayers;

    Queue<Action> m_UpdateQueue;

    public NetWorldPlayer LocalPlayer => Find(m_LocalPlayerUID);
    public NetWorldPlayer MasterPlayer => Find(m_MasterClientUID);
    public NetWorldPlayer Find(ClientUID uid) => m_PlayersCollection.TryGetValue(uid, out var player) ? player : null;
    public bool Contains(ClientUID uid) => m_PlayersCollection.ContainsKey(uid);

    public NetWorldPlayersCollection()
    {
        m_LocalPlayerUID = ClientUID.Invalid;
        m_MasterClientUID = ClientUID.Invalid;

        m_PlayersCollection = new Dictionary<ClientUID, NetWorldPlayer>();
        m_JustRemovedPlayers = new HashSet<ClientUID>();

        m_UpdateQueue = new Queue<Action>();
    }

    public void Register(ClientUID uid, string name, bool isLocal) =>
        ScheduleOnUpdate(() =>
        {
            if (isLocal)
                m_LocalPlayerUID = uid;

            if (m_PlayersCollection.ContainsKey(uid))
                throw new InvalidOperationException();

            m_PlayersCollection.Add(uid, new NetWorldPlayer(uid, name, () => m_LocalPlayerUID, () => m_MasterClientUID));
        });

    public void Unregister(ClientUID uid) =>
        ScheduleOnUpdate(() =>
        {
            if (m_LocalPlayerUID == uid)
                m_LocalPlayerUID = ClientUID.Invalid;

            m_JustRemovedPlayers.Add(uid);
            m_PlayersCollection.Remove(uid);
        });

    void ScheduleOnUpdate(Action a) => m_UpdateQueue.Enqueue(a);

    public void Udpate()
    {
        while (m_UpdateQueue.Count > 0)
            m_UpdateQueue.Dequeue()();

        m_MasterClientUID = PlayerUIDs.Any() ? PlayerUIDs.Min() : ClientUID.Invalid;
    }

    public void FinalizeIteration()
    {
        m_JustRemovedPlayers.Clear();
    }

    public void Clear()
    {
        m_LocalPlayerUID = ClientUID.Invalid;
        m_MasterClientUID = ClientUID.Invalid;

        m_PlayersCollection.Clear();
        m_PlayersCollection = null;

        m_JustRemovedPlayers.Clear();
        m_JustRemovedPlayers = null;

        m_UpdateQueue.Clear();
        m_UpdateQueue = null;
    }

    public NetWorldPlayersCollectionsSnapshot ToSnapshot() => NetWorldPlayersCollectionsSnapshot.FormCollection(this);
}