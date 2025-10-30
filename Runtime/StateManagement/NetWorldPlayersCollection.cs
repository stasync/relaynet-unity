using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LROTech.Network.Sessions;
using LROTech.Network.StateManagement.Snapshots;

namespace LROTech.Network.StateManagement
{
    public sealed class NetWorldPlayersCollection : IEnumerable<NetWorldPlayer>
    {
        #region State

        public ClientUid LocalPlayerUid { get; private set; }
        public ClientUid MasterPlayerUid { get; private set; }

        public IReadOnlyCollection<NetWorldPlayer> All => _playersCollection.Values;
        private Dictionary<ClientUid, NetWorldPlayer> _playersCollection = new();

        #endregion

        public bool LocalPlayerIsMaster => MasterPlayerUid.IsValid && MasterPlayerUid.Equals(LocalPlayerUid);
        public NetWorldPlayer LocalPlayer => Find(LocalPlayerUid);
        public NetWorldPlayer MasterPlayer => Find(MasterPlayerUid);

        private Func<bool> _validator;

        internal NetWorldPlayersCollection(Func<bool> validator)
        {
            _validator = validator;

            LocalPlayerUid = ClientUid.Invalid;
            MasterPlayerUid = ClientUid.Invalid;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValid() => 
            _validator != null && _validator();

        internal void OnBeforeUpdateWorldState(float worldDelta)
        {
        }

        internal NetWorldPlayer RegisterImmediate(ClientUid uid, bool isLocal)
        {
            if (_playersCollection.ContainsKey(uid))
                throw new InvalidOperationException();

            var player = new NetWorldPlayer(
                validator: _playersCollection.ContainsKey, 
                uid,
                name: "Unknown",
                localPlayerUidProvider: () => LocalPlayerUid,
                masterPlayerUidProvider: () => MasterPlayerUid);

            _playersCollection.Add(uid, player);
            if (isLocal)
                LocalPlayerUid = uid;

            RecalculateMasterPlayerUid();
            return player;
        }

        internal bool UnregisterImmediate(ClientUid uid)
        {
            if (LocalPlayerUid == uid)
                LocalPlayerUid = ClientUid.Invalid;

            var playerRemoved = _playersCollection.Remove(uid);
            if (playerRemoved)
                RecalculateMasterPlayerUid();

            return playerRemoved;
        }

        public NetWorldPlayer Find(ClientUid uid)
        {
            EnsureValid();
            return _playersCollection.GetValueOrDefault(uid);
        }

        public bool Contains(ClientUid uid)
        {
            EnsureValid();
            return _playersCollection.ContainsKey(uid);
        }

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
            _validator = null;

            LocalPlayerUid = ClientUid.Invalid;
            MasterPlayerUid = ClientUid.Invalid;

            _playersCollection.Clear();
            _playersCollection = null;
        }

        public NetWorldPlayersCollectionsSnapshot ToSnapshot()
        {
            EnsureValid();
            return new NetWorldPlayersCollectionsSnapshot(collection: this);
        }

        internal void RestoreFromSnapshot(NetWorldPlayersCollectionsSnapshot snapshot)
        {
            // Use invalid id for local and master player while snapshots if only used for world replication.
            // LocalPlayerUID = snapshot.LocalPlayerUID;
            // MasterPlayerUID = snapshot.MasterClientUID;

            LocalPlayerUid = ClientUid.Invalid;
            MasterPlayerUid = ClientUid.Invalid;

            // Remove.
            foreach (var clientUid in new List<ClientUid>(_playersCollection.Keys))
            {
                if (!snapshot.PlayersCollection.ContainsKey(clientUid))
                    _playersCollection.Remove(clientUid);
            }

            // Add or update.
            foreach (var (clientUid, playerSnapshot) in snapshot.PlayersCollection)
            {
                if (_playersCollection.TryGetValue(clientUid, out var player))
                    player.RestoreFromSnapshot(playerSnapshot);
                else
                    _playersCollection.Add(clientUid, 
                        new NetWorldPlayer(
                            validator: _playersCollection.ContainsKey,
                            playerSnapshot.Uid,
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
            foreach (var kvp in _playersCollection)
            {
                hash += kvp.Key.GetHashCode();
                hash += kvp.Value.GetVersionHash();
            }

            return hash;
        }

        public IEnumerator<NetWorldPlayer> GetEnumerator()
        {
            EnsureValid();
            return All.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            EnsureValid();
            return All.GetEnumerator();
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureValid()
        {
            if (!IsValid())
                throw new InvalidOperationException($"Object of type '{GetType().FullName}' is invalid.");
        }
    }
}