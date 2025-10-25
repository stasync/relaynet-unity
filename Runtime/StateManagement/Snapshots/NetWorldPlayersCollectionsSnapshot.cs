using System.Collections.Generic;

namespace LROTech.Network.StateManagement.Snapshots
{
    public readonly struct NetWorldPlayersCollectionsSnapshot
    {
        public readonly ClientUid LocalPlayerUID;
        public readonly ClientUid MasterClientUID;
        public readonly IReadOnlyDictionary<ClientUid, NetWorldPlayerSnapshot> PlayersCollection;

        internal NetWorldPlayersCollectionsSnapshot(NetWorldPlayersCollection collection)
        {
            LocalPlayerUID = collection.LocalPlayer.Uid;
            MasterClientUID = collection.MasterPlayer.Uid;

            var playersCollection = new Dictionary<ClientUid, NetWorldPlayerSnapshot>();
            foreach (var worldPlayer in collection.All)
                playersCollection.Add(worldPlayer.Uid, worldPlayer.ToSnapshot());

            PlayersCollection = playersCollection;
        }

        public int GetSize()
        {
            var result = 0;
            result += ClientUid.Size; // LocalPlayerUid
            result += ClientUid.Size; // MasterClientUid

            foreach (var playerSnapshot in PlayersCollection.Values)
            {
                result += ClientUid.Size; // ClientUid
                result += playerSnapshot.GetSize();
            }

            return result;
        }
    }
}