using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LROTech.Network.StateManagement.Snapshots;

namespace LROTech.Network.StateManagement
{
    public sealed class NetWorldPlayersCollection : IEnumerable<NetWorldPlayer>
    {
        #region State

        private ClientUid _localPlayerUid;
        private ClientUid _masterPlayerUid;

        public IReadOnlyCollection<NetWorldPlayer> All => _playersCollection.Values;
        private Dictionary<ClientUid, NetWorldPlayer> _playersCollection = new();

        #endregion

        internal bool LocalPlayerIsMaster => _masterPlayerUid.IsValid && _masterPlayerUid.Equals(_localPlayerUid);
        public NetWorldPlayer LocalPlayer => Find(_localPlayerUid);
        public NetWorldPlayer MasterPlayer => Find(_masterPlayerUid);

        private Func<bool> _validator;
        private readonly Func<ClientUid> _masterClientProvider;

        internal NetWorldPlayersCollection(Func<bool> validator, Func<ClientUid> masterClientProvider)
        {
            _validator = validator;
            _masterClientProvider = masterClientProvider;

            _localPlayerUid = ClientUid.Invalid;
            _masterPlayerUid = ClientUid.Invalid;
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
                localPlayerUidProvider: () => _localPlayerUid,
                masterPlayerUidProvider: () => _masterPlayerUid);

            _playersCollection.Add(uid, player);
            if (isLocal)
                _localPlayerUid = uid;

            _masterPlayerUid = _masterClientProvider();
            return player;
        }

        internal bool UnregisterImmediate(ClientUid uid)
        {
            if (_localPlayerUid == uid)
                _localPlayerUid = ClientUid.Invalid;

            var playerRemoved = _playersCollection.Remove(uid);
            if (playerRemoved)
                _masterPlayerUid = _masterClientProvider();

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
        
        internal void Clear()
        {
            _validator = null;

            _localPlayerUid = ClientUid.Invalid;
            _masterPlayerUid = ClientUid.Invalid;

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

            _localPlayerUid = ClientUid.Invalid;
            _masterPlayerUid = ClientUid.Invalid;

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
                            localPlayerUidProvider: () => _localPlayerUid,
                            masterPlayerUidProvider: () => _masterPlayerUid));
            }
        }

        internal int GetVersionHash()
        {
            var hash = 13;
            hash = hash * 7 + _localPlayerUid.GetHashCode();
            hash = hash * 7 + _masterPlayerUid.GetHashCode();
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