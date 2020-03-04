using System;
using System.Collections.Generic;
using System.Linq;

public class NetWorldPlayersCollection
{
    #region State
    ClientUID m_LocalPlayerUID;
    ClientUID m_MasterClientUID;

    public IEnumerable<ClientUID> PlayerUIDs => m_PlayersCollection.Keys;
    Dictionary<ClientUID, NetWorldPlayer> m_PlayersCollection;
    #endregion

    Queue<Action> m_UpdateQueue;

    public NetWorldPlayer LocalPlayer => Find(m_LocalPlayerUID);
    public NetWorldPlayer MasterPlayer => Find(m_MasterClientUID);
    public NetWorldPlayer Find(ClientUID uid) => m_PlayersCollection.TryGetValue(uid, out var player) ? player : null;
    public bool Contains(ClientUID uid) => m_PlayersCollection.ContainsKey(uid);

    public IReadOnlyCollection<ClientUID> AddedPlayers => m_AddedPlayers;
    HashSet<ClientUID> m_AddedPlayers;

    public IReadOnlyCollection<ClientUID> RemovedPlayers => m_RemovedPlayers;
    HashSet<ClientUID> m_RemovedPlayers;

    public NetWorldPlayersCollection()
    {
        m_LocalPlayerUID = ClientUID.Invalid;
        m_MasterClientUID = ClientUID.Invalid;

        m_PlayersCollection = new Dictionary<ClientUID, NetWorldPlayer>();
        m_UpdateQueue = new Queue<Action>();

        m_AddedPlayers = new HashSet<ClientUID>();
        m_RemovedPlayers = new HashSet<ClientUID>();
    }

    public void Register(ClientUID uid, string name, bool isLocal) =>
        ScheduleOnUpdate(() =>
        {
            if (isLocal)
                m_LocalPlayerUID = uid;

            if (m_PlayersCollection.ContainsKey(uid))
                throw new InvalidOperationException();

            m_PlayersCollection.Add(uid, new NetWorldPlayer(uid, name, () => m_LocalPlayerUID, () => m_MasterClientUID));
            m_AddedPlayers.Add(uid);
        });

    public void Unregister(ClientUID uid) =>
        ScheduleOnUpdate(() =>
        {
            if (m_LocalPlayerUID == uid)
                m_LocalPlayerUID = ClientUID.Invalid;

            if (m_PlayersCollection.Remove(uid))
                m_RemovedPlayers.Remove(uid);
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
        m_AddedPlayers.Clear();
        m_RemovedPlayers.Clear();
    }

    public void Clear()
    {
        m_LocalPlayerUID = ClientUID.Invalid;
        m_MasterClientUID = ClientUID.Invalid;

        m_PlayersCollection.Clear();
        m_PlayersCollection = null;

        m_AddedPlayers.Clear();
        m_AddedPlayers = null;

        m_RemovedPlayers.Clear();
        m_RemovedPlayers = null;

        m_UpdateQueue.Clear();
        m_UpdateQueue = null;
    }

    public NetWorldPlayersCollectionsSnapshot ToSnapshot() => NetWorldPlayersCollectionsSnapshot.FormCollection(this);
    public void RestoreFromSnapshot(NetWorldPlayersCollectionsSnapshot snapshot)
    {
        m_LocalPlayerUID = snapshot.LocalPlayerUID;
        m_MasterClientUID = snapshot.MasterClientUID;

        //Remove;
        foreach(var p in m_PlayersCollection.Keys.ToArray())
        {
            if (!snapshot.PlayersCollection.ContainsKey(p))
                m_PlayersCollection.Remove(p);
        }

        //Add or update;
        foreach(var p in snapshot.PlayersCollection)
        {
            if (m_PlayersCollection.TryGetValue(p.Key, out var player))
                player.RestoreFromSnapshot(p.Value);
            else
                m_PlayersCollection.Add(p.Key, new NetWorldPlayer(p.Value.UID, p.Value.Name, () => m_LocalPlayerUID, () => m_MasterClientUID));
                
        }
    }
}