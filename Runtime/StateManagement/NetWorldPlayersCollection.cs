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
}

public interface IExposedNetWorldPlayersCollection : INetWorldPlayersCollection
{
    IReadOnlyCollection<NetWorldPlayer> All { get; }
    NetWorldPlayer LocalPlayer { get; }
    NetWorldPlayer MasterPlayer { get; }
    NetWorldPlayer Find(ClientUID uid);
}

public sealed class NetWorldPlayersCollection : IExposedNetWorldPlayersCollection, IReadOnlyNetWorldPlayersCollection
{
    #region State
    public ClientUID LocalPlayerUID { get; private set; }
    public ClientUID MasterPlayerUID { get; private set; }

    public IEnumerable<ClientUID> AllUIDs => m_PlayersCollection.Keys;
    public IReadOnlyCollection<NetWorldPlayer> All => m_PlayersCollection.Values;

    private Dictionary<ClientUID, NetWorldPlayer> m_PlayersCollection;
    #endregion

    #region IReadOnlyNetWorldPlayersCollection
    IReadOnlyNetWorldPlayer IReadOnlyNetWorldPlayersCollection.LocalPlayer => LocalPlayer;
    IReadOnlyNetWorldPlayer IReadOnlyNetWorldPlayersCollection.MasterPlayer => MasterPlayer;
    IReadOnlyNetWorldPlayer IReadOnlyNetWorldPlayersCollection.Find(ClientUID uid) => Find(uid);
    #endregion

    public bool LocalPlayerIsMaster => IsValid && MasterPlayerUID.IsValid && MasterPlayerUID.Equals(LocalPlayerUID);
    public NetWorldPlayer LocalPlayer => Find(LocalPlayerUID);
    public NetWorldPlayer MasterPlayer => Find(MasterPlayerUID);
    public NetWorldPlayer Find(ClientUID uid) => IsValid ? m_PlayersCollection.GetValueOrDefault(uid) : null;
    public bool Contains(ClientUID uid) => IsValid && m_PlayersCollection.ContainsKey(uid);

    public IReadOnlyCollection<ClientUID> AddedPlayers => m_AddedPlayers;
    private HashSet<ClientUID> m_AddedPlayers;

    public IReadOnlyCollection<ClientUID> RemovedPlayers => m_RemovedPlayers;
    private HashSet<ClientUID> m_RemovedPlayers;

    private Func<bool> m_Validator;
    private bool IsValid => m_Validator != null && m_Validator();

    public NetWorldPlayersCollection(Func<bool> validator)
    {
        m_Validator = validator;

        LocalPlayerUID = ClientUID.Invalid;
        MasterPlayerUID = ClientUID.Invalid;

        m_PlayersCollection = new Dictionary<ClientUID, NetWorldPlayer>();

        m_AddedPlayers = new HashSet<ClientUID>();
        m_RemovedPlayers = new HashSet<ClientUID>();
    }

    internal void OnBeforeUpdateWorldState(float worldDelta)
    {
        m_AddedPlayers.Clear();
        m_RemovedPlayers.Clear();
    }

    internal NetWorldPlayer RegisterImmediate(ClientUID uid, bool isLocal)
    {
        if (m_PlayersCollection.ContainsKey(uid))
            throw new InvalidOperationException();

        var player = new NetWorldPlayer(uid, name: "Unknown", localPlayerUidProvider: () => LocalPlayerUID, masterPlayerUidProvider: () => MasterPlayerUID);
        m_PlayersCollection.Add(uid, player);
        m_AddedPlayers.Add(uid);

        if (isLocal)
            LocalPlayerUID = uid;

        UpdateMasterUid();
        return player;
    }

    internal void UnregisterImmediate(ClientUID uid)
    {
        if (LocalPlayerUID == uid)
            LocalPlayerUID = ClientUID.Invalid;

        if (m_PlayersCollection.Remove(uid))
            m_RemovedPlayers.Add(uid);

        UpdateMasterUid();
    }

    private void UpdateMasterUid() => 
        MasterPlayerUID = AllUIDs.Any() ? AllUIDs.Min() : ClientUID.Invalid;

    internal void Clear()
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

    public NetWorldPlayersCollectionsSnapshot ToSnapshot() => 
        NetWorldPlayersCollectionsSnapshot.FormCollection(collection: this);
    
    internal void RestoreFromSnapshot(NetWorldPlayersCollectionsSnapshot snapshot)
    {
        // Use invalid id for local and master player while snapshots if only used for world replication.
        // LocalPlayerUID = snapshot.LocalPlayerUID;
        // MasterPlayerUID = snapshot.MasterClientUID;

        LocalPlayerUID = ClientUID.Invalid;
        MasterPlayerUID = ClientUID.Invalid;

        // Remove.
        foreach (var clientUid in m_PlayersCollection.Keys.ToArray())
        {
            if (!snapshot.PlayersCollection.ContainsKey(clientUid))
                m_PlayersCollection.Remove(clientUid);
        }

        // Add or update.
        foreach(var (clientUid, playerSnapshot) in snapshot.PlayersCollection)
        {
            if (m_PlayersCollection.TryGetValue(clientUid, out var player))
                player.RestoreFromSnapshot(playerSnapshot);
            else
                m_PlayersCollection.Add(clientUid, 
                    new NetWorldPlayer(playerSnapshot.UID, playerSnapshot.Name, localPlayerUidProvider: () => LocalPlayerUID, masterPlayerUidProvider: () => MasterPlayerUID));
        }
    }

    public override int GetHashCode()
    {
        var hash = 13;
        hash = hash * 7 + LocalPlayerUID.GetHashCode();
        hash = hash * 7 + MasterPlayerUID.GetHashCode();
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