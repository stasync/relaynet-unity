using System.Collections.Generic;

public readonly struct NetWorldPlayersCollectionsSnapshot
{
    public readonly ClientUID LocalPlayerUID;
    public readonly ClientUID MasterClientUID;

    public IReadOnlyDictionary<ClientUID, NetWorldPlayerSnapshot> PlayersCollection => m_PlayersCollection;
    readonly Dictionary<ClientUID, NetWorldPlayerSnapshot> m_PlayersCollection;

    private NetWorldPlayersCollectionsSnapshot(NetWorldPlayersCollection collection)
    {
        LocalPlayerUID = collection.LocalPlayer.Uid;
        MasterClientUID = collection.MasterPlayer.Uid;

        m_PlayersCollection = new Dictionary<ClientUID, NetWorldPlayerSnapshot>();
        foreach (var worldPlayer in collection.All)
            m_PlayersCollection.Add(worldPlayer.Uid, worldPlayer.ToSnapshot());
    }

    public static NetWorldPlayersCollectionsSnapshot FormCollection(NetWorldPlayersCollection collection) =>
        new(collection);

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

        return result;
    }
}