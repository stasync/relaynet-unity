using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public interface INetWorldPlayersCollection : IEnumerable<NetWorldPlayer>
{
    IEnumerable<ClientUID> AllUIDs { get; }
    ClientUID LocalPlayerUID { get; }
    ClientUID MasterPlayerUID { get; }
    bool Contains(ClientUID uid);
    int GetHashCode();
    NetWorldPlayersCollectionsSnapshot ToSnapshot();
}

public interface IReadOnlyNetWorldPlayersCollection : INetWorldPlayersCollection
{
    IReadOnlyNetWorldPlayer LocalPlayer { get; }
    IReadOnlyNetWorldPlayer MasterPlayer { get; }
    IReadOnlyNetWorldPlayer Find(ClientUID uid);
    void ForEachPlayer(Action<ClientUID, IReadOnlyNetWorldPlayer> processor);
}

public interface IExposedyNetWorldPlayersCollection : INetWorldPlayersCollection
{
    IReadOnlyCollection<NetWorldPlayer> All { get; }
    NetWorldPlayer LocalPlayer { get; }
    NetWorldPlayer MasterPlayer { get; }
    NetWorldPlayer Find(ClientUID uid);
    void ForEachPlayer(Action<ClientUID, NetWorldPlayer> processor);
}

public class NetWorldPlayersCollection : IExposedyNetWorldPlayersCollection, IReadOnlyNetWorldPlayersCollection
{
    #region State
    public ClientUID LocalPlayerUID { get; private set; }
    public ClientUID MasterPlayerUID { get; private set; }

    public IEnumerable<ClientUID> AllUIDs => m_PlayersCollection.Keys;
    public IReadOnlyCollection<NetWorldPlayer> All => m_PlayersCollection.Values;

    Dictionary<ClientUID, NetWorldPlayer> m_PlayersCollection;
    #endregion

    #region IReadOnlyNetWorldPlayersCollection
    IReadOnlyNetWorldPlayer IReadOnlyNetWorldPlayersCollection.LocalPlayer => LocalPlayer;
    IReadOnlyNetWorldPlayer IReadOnlyNetWorldPlayersCollection.MasterPlayer => MasterPlayer;
    IReadOnlyNetWorldPlayer IReadOnlyNetWorldPlayersCollection.Find(ClientUID uid) => Find(uid);
    void IReadOnlyNetWorldPlayersCollection.ForEachPlayer(Action<ClientUID, IReadOnlyNetWorldPlayer> processor) => ForEachPlayer(processor);
    #endregion

    public bool LocalPlayerIsMaster => IsValid && MasterPlayerUID.IsValid && MasterPlayerUID.Equals(LocalPlayerUID);
    public NetWorldPlayer LocalPlayer => Find(LocalPlayerUID);
    public NetWorldPlayer MasterPlayer => Find(MasterPlayerUID);
    public NetWorldPlayer Find(ClientUID uid) => IsValid ? (m_PlayersCollection.TryGetValue(uid, out var player) ? player : null) : null;
    public bool Contains(ClientUID uid) => IsValid && m_PlayersCollection.ContainsKey(uid);

    public IReadOnlyCollection<ClientUID> AddedPlayers => m_AddedPlayers;
    HashSet<ClientUID> m_AddedPlayers;

    public IReadOnlyCollection<ClientUID> RemovedPlayers => m_RemovedPlayers;
    HashSet<ClientUID> m_RemovedPlayers;

    Func<bool> m_Validator;
    bool IsValid => m_Validator != null && m_Validator();

    public NetWorldPlayersCollection(Func<bool> validator)
    {
        m_Validator = validator;

        LocalPlayerUID = ClientUID.Invalid;
        MasterPlayerUID = ClientUID.Invalid;

        m_PlayersCollection = new Dictionary<ClientUID, NetWorldPlayer>();

        m_AddedPlayers = new HashSet<ClientUID>();
        m_RemovedPlayers = new HashSet<ClientUID>();
    }

    public void ForEachPlayer(Action<ClientUID, NetWorldPlayer> processor)
    {
        foreach (var UID in m_PlayersCollection.Keys.ToArray())
            processor(UID, m_PlayersCollection[UID]);
    }

    public void OnBeforeUpdateWorldState(float worldDelta)
    {
        m_AddedPlayers.Clear();
        m_RemovedPlayers.Clear();
    }

    public NetWorldPlayer RegisterImmidiate(ClientUID uid, bool isLocal)
    {
        if (m_PlayersCollection.ContainsKey(uid))
            throw new InvalidOperationException();

        var player = new NetWorldPlayer(uid, "Unknown", () => LocalPlayerUID, () => MasterPlayerUID);
        m_PlayersCollection.Add(uid, player);
        m_AddedPlayers.Add(uid);

        if (isLocal)
            LocalPlayerUID = uid;

        UpdateMasterUID();
        return player;
    }

    public void UnregisterImmidiate(ClientUID uid)
    {
        if (LocalPlayerUID == uid)
            LocalPlayerUID = ClientUID.Invalid;

        if (m_PlayersCollection.Remove(uid))
            m_RemovedPlayers.Add(uid);

        UpdateMasterUID();
    }

    void UpdateMasterUID() => MasterPlayerUID = AllUIDs.Any() ? AllUIDs.Min() : ClientUID.Invalid;

    public void Clear()
    {
        m_Validator = null;

        LocalPlayerUID = ClientUID.Invalid;
        MasterPlayerUID = ClientUID.Invalid;

        m_PlayersCollection.Clear();
        m_PlayersCollection = null;

        m_AddedPlayers.Clear();
        m_AddedPlayers = null;

        m_RemovedPlayers.Clear();
        m_RemovedPlayers = null;
    }

    public NetWorldPlayersCollectionsSnapshot ToSnapshot() => NetWorldPlayersCollectionsSnapshot.FormCollection(this);
    public void RestoreFromSnapshot(NetWorldPlayersCollectionsSnapshot snapshot)
    {
        //Use invalid id for local and master player while snapshots if only used for world replication.
        //LocalPlayerUID = snapshot.LocalPlayerUID;
        //MasterPlayerUID = snapshot.MasterClientUID;

        LocalPlayerUID = ClientUID.Invalid;
        MasterPlayerUID = ClientUID.Invalid;

        //Remove;
        foreach (var p in m_PlayersCollection.Keys.ToArray())
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
                m_PlayersCollection.Add(p.Key, new NetWorldPlayer(p.Value.UID, p.Value.Name, () => LocalPlayerUID, () => MasterPlayerUID));
        }
    }

    public override int GetHashCode()
    {
        var hash = 13;
        hash = (hash * 7) + LocalPlayerUID.GetHashCode();
        hash = (hash * 7) + MasterPlayerUID.GetHashCode();
        foreach (var kvp in m_PlayersCollection)
        {
            hash += kvp.Key.GetHashCode();
            hash += kvp.Value.GetHashCode();
        }
        return hash;
    }

    public IEnumerator<NetWorldPlayer> GetEnumerator() => All.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => All.GetEnumerator();
}