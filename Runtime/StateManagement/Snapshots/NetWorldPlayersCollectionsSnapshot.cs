using System.Collections.Generic;

namespace LROTech.Network.StateManagement
{
    public readonly struct NetWorldPlayersCollectionsSnapshot
    {
        public readonly ClientUid LocalPlayerUID;
        public readonly ClientUid MasterClientUID;
        public readonly IReadOnlyDictionary<ClientUid, NetWorldPlayerSnapshot> PlayersCollection;

        private NetWorldPlayersCollectionsSnapshot(NetWorldPlayersCollection collection)
        {
            LocalPlayerUID = collection.LocalPlayer.Uid;
            MasterClientUID = collection.MasterPlayer.Uid;

            var playersCollection = new Dictionary<ClientUid, NetWorldPlayerSnapshot>();
            foreach (var worldPlayer in collection.All)
                playersCollection.Add(worldPlayer.Uid, worldPlayer.ToSnapshot());

            PlayersCollection = playersCollection;
        }

        public static NetWorldPlayersCollectionsSnapshot FormCollection(NetWorldPlayersCollection collection) =>
            new(collection);

        public int GetSize()
        {
            var result = 0;
            result += ClientUid.Size; // LocalPlayerUid
            result += ClientUid.Size; // MasterClientUid

            foreach (var p in PlayersCollection.Values)
            {
                result += ClientUid.Size; // ClientUid
                result += p.GetSize();
            }

            return result;
        }
    }
}