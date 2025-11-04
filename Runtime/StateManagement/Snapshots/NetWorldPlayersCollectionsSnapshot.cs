using System.Collections.Generic;

namespace LROTech.Network.StateManagement.Snapshots
{
    public readonly struct NetWorldPlayersCollectionsSnapshot
    {
        public readonly ClientUid LocalPlayerUid;
        public readonly ClientUid MasterClientUid;
        public readonly IReadOnlyDictionary<ClientUid, NetWorldPlayerSnapshot> PlayersCollection;

        internal NetWorldPlayersCollectionsSnapshot(NetWorldPlayersCollection collection)
        {
            LocalPlayerUid = collection.LocalPlayer.Uid;
            MasterClientUid = collection.MasterPlayer.Uid;

            var playersCollection = new Dictionary<ClientUid, NetWorldPlayerSnapshot>(collection.All.Count);
            foreach (var worldPlayer in collection.All)
                playersCollection.Add(worldPlayer.Uid, worldPlayer.ToSnapshot());

            PlayersCollection = playersCollection;
        }

        public int GetSize()
        {
            var result = 0;
            result += ClientUid.SIZE; // LocalPlayerUid
            result += ClientUid.SIZE; // MasterClientUid

            foreach (var playerSnapshot in PlayersCollection.Values)
            {
                result += ClientUid.SIZE; // ClientUid
                result += playerSnapshot.GetSize();
            }

            return result;
        }
    }
}