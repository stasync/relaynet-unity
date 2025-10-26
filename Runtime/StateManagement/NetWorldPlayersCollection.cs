using System;
using System.Collections;
using System.Collections.Generic;
using LROTech.Network.Sessions;
using LROTech.Network.StateManagement.Snapshots;

namespace LROTech.Network.StateManagement
{
    public sealed class NetWorldPlayersCollection : IEnumerable<NetWorldPlayer>
    {
        #region State

        public ClientUid LocalPlayerUid { get; private set; }
        public ClientUid MasterPlayerUid { get; private set; }

        public IReadOnlyCollection<NetWorldPlayer> All => m_PlayersCollection.Values;

        private Dictionary<ClientUid, NetWorldPlayer> m_PlayersCollection;

        #endregion

        public bool LocalPlayerIsMaster => IsValid && MasterPlayerUid.IsValid && MasterPlayerUid.Equals(LocalPlayerUid);
        public NetWorldPlayer LocalPlayer => Find(LocalPlayerUid);
        public NetWorldPlayer MasterPlayer => Find(MasterPlayerUid);

        private Func<bool> m_Validator;
        private bool IsValid => m_Validator != null && m_Validator();

        internal NetWorldPlayersCollection(Func<bool> validator)
        {
            m_Validator = validator;

            LocalPlayerUid = ClientUid.Invalid;
            MasterPlayerUid = ClientUid.Invalid;

            m_PlayersCollection = new Dictionary<ClientUid, NetWorldPlayer>();
        }

        internal void OnBeforeUpdateWorldState(float worldDelta)
        {
        }

        internal NetWorldPlayer RegisterImmediate(ClientUid uid, bool isLocal)
        {
            if (m_PlayersCollection.ContainsKey(uid))
                throw new InvalidOperationException();

            var player = new NetWorldPlayer(uid,
                name: "Unknown",
                localPlayerUidProvider: () => LocalPlayerUid,
                masterPlayerUidProvider: () => MasterPlayerUid);

            m_PlayersCollection.Add(uid, player);
            if (isLocal)
                LocalPlayerUid = uid;

            RecalculateMasterPlayerUid();
            return player;
        }

        internal bool UnregisterImmediate(ClientUid uid)
        {
            if (LocalPlayerUid == uid)
                LocalPlayerUid = ClientUid.Invalid;

            var playerRemoved = m_PlayersCollection.Remove(uid);
            if (playerRemoved)
                RecalculateMasterPlayerUid();

            return playerRemoved;
        }
        
        public NetWorldPlayer Find(ClientUid uid) => 
            IsValid ? m_PlayersCollection.GetValueOrDefault(uid) : null;
        
        public bool Contains(ClientUid uid) => 
            IsValid && m_PlayersCollection.ContainsKey(uid);

        private void RecalculateMasterPlayerUid()
        {
            MasterPlayerUid = ClientUid.Invalid;
            foreach (var worldPlayer in All)
            {
                if (worldPlayer.Uid < MasterPlayerUid || !MasterPlayerUid.IsValid)
                    MasterPlayerUid = worldPlayer.Uid;
            }
        }

        internal void Clear()
        {
            m_Validator = null;

            LocalPlayerUid = ClientUid.Invalid;
            MasterPlayerUid = ClientUid.Invalid;

            m_PlayersCollection.Clear();
            m_PlayersCollection = null;
        }

        public NetWorldPlayersCollectionsSnapshot ToSnapshot() =>
            new(collection: this);

        internal void RestoreFromSnapshot(NetWorldPlayersCollectionsSnapshot snapshot)
        {
            // Use invalid id for local and master player while snapshots if only used for world replication.
            // LocalPlayerUID = snapshot.LocalPlayerUID;
            // MasterPlayerUID = snapshot.MasterClientUID;

            LocalPlayerUid = ClientUid.Invalid;
            MasterPlayerUid = ClientUid.Invalid;

            // Remove.
            foreach (var clientUid in new List<ClientUid>(m_PlayersCollection.Keys))
            {
                if (!snapshot.PlayersCollection.ContainsKey(clientUid))
                    m_PlayersCollection.Remove(clientUid);
            }

            // Add or update.
            foreach (var (clientUid, playerSnapshot) in snapshot.PlayersCollection)
            {
                if (m_PlayersCollection.TryGetValue(clientUid, out var player))
                    player.RestoreFromSnapshot(playerSnapshot);
                else
                    m_PlayersCollection.Add(clientUid, new NetWorldPlayer(
                        playerSnapshot.UID,
                        playerSnapshot.Name,
                        localPlayerUidProvider: () => LocalPlayerUid,
                        masterPlayerUidProvider: () => MasterPlayerUid));
            }
        }

        internal int GetVersionHash()
        {
            var hash = 13;
            hash = hash * 7 + LocalPlayerUid.GetHashCode();
            hash = hash * 7 + MasterPlayerUid.GetHashCode();
            foreach (var kvp in m_PlayersCollection)
            {
                hash += kvp.Key.GetHashCode();
                hash += kvp.Value.GetVersionHash();
            }

            return hash;
        }

        public IEnumerator<NetWorldPlayer> GetEnumerator() => All.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => All.GetEnumerator();
    }
}