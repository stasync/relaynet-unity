using System.Collections.Generic;
using System.Linq;

public readonly struct NetWorldPlayersCollectionsSnapshot
{
    public readonly ClientUID LocalPlayerUID;
    public readonly ClientUID MasterClientUID;

    public IReadOnlyDictionary<ClientUID, NetWorldPlayerSnapshot> PlayersCollection => m_PlayersCollection;
    readonly Dictionary<ClientUID, NetWorldPlayerSnapshot> m_PlayersCollection;

    public IReadOnlyList<ClientUID> RemovedPlayers => m_RemovedPlayers;
    readonly List<ClientUID> m_RemovedPlayers;

    private NetWorldPlayersCollectionsSnapshot(NetWorldPlayersCollection collection)
    {
        LocalPlayerUID = collection.LocalPlayer.Uid;
        MasterClientUID = collection.MasterPlayer.Uid;

        m_PlayersCollection = new Dictionary<ClientUID, NetWorldPlayerSnapshot>();
        foreach (var worldPlayer in collection.All)
            m_PlayersCollection.Add(worldPlayer.Uid, worldPlayer.ToSnapshot());

        m_RemovedPlayers = collection.RemovedPlayers.ToList();
    }

    public static NetWorldPlayersCollectionsSnapshot FormCollection(NetWorldPlayersCollection collection) => new(collection);

    public int GetSize()
    {
        var result = 0;
        result += ClientUID.Size; // LocalPlayerUID
        result += ClientUID.Size; // MasterClientUID

        foreach (var p in m_PlayersCollection.Values)
        {
            result += ClientUID.Size; // ClientUID
            result += p.GetSize();
        }

        foreach (var p in m_RemovedPlayers)
            result += ClientUID.Size; // ClientUID

        return result;
    }
}
