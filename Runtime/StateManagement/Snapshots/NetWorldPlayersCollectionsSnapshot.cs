using System.Collections.Generic;
using System.Linq;

public struct NetWorldPlayersCollectionsSnapshot
{
    public readonly ClientUID LocalPlayerUID;
    public readonly ClientUID MasterClientUID;

    public IReadOnlyDictionary<ClientUID, NetWorldPlayerSnapshot> PlayersCollection => m_PlayersCollection;
    Dictionary<ClientUID, NetWorldPlayerSnapshot> m_PlayersCollection;

    public IReadOnlyList<ClientUID> RemovedPlayers => m_RemovedPlayers;
    public List<ClientUID> m_RemovedPlayers;

    public NetWorldPlayersCollectionsSnapshot(NetWorldPlayersCollection collection)
    {
        LocalPlayerUID = collection.LocalPlayer.UID;
        MasterClientUID = collection.MasterPlayer.UID;

        m_PlayersCollection = new Dictionary<ClientUID, NetWorldPlayerSnapshot>();
        foreach (var UID in collection.AllUIDs)
            m_PlayersCollection.Add(UID, collection.Find(UID).ToSnapshot());

        m_RemovedPlayers = collection.RemovedPlayers.ToList();
    }

    public static NetWorldPlayersCollectionsSnapshot FormCollection(NetWorldPlayersCollection collection) => new NetWorldPlayersCollectionsSnapshot(collection);

    public int GetSize()
    {
        var result = 0;
        result += sizeof(uint); // LocalPlayerUID
        result += sizeof(uint); // MasterClientUID

        foreach (var p in m_PlayersCollection.Values)
        {
            result += sizeof(uint); // ClientUID
            result += p.GetSize();
        }

        foreach (var p in m_RemovedPlayers)
            result += sizeof(uint); // ClientUID

        return result;
    }
}
