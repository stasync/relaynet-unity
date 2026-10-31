using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Keel.RelayNet.StateManagement.Snapshots;

namespace Keel.RelayNet.StateManagement
{
    public sealed class NetWorldPlayersCollection : IEnumerable<NetWorldPlayer>
    {
        #region State

        public IReadOnlyCollection<NetWorldPlayer> All => _playersCollection.Values;
        private Dictionary<ClientUid, NetWorldPlayer> _playersCollection = new();

        #endregion

        public NetWorldPlayer LocalPlayer { get; private set; }
        public NetWorldPlayer MasterPlayer { get; private set; }

        private Func<bool> _validator;
        private readonly Func<ClientUid> _masterClientUidProvider;

        internal NetWorldPlayersCollection(Func<bool> validator, Func<ClientUid> masterClientUidProvider)
        {
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _masterClientUidProvider = masterClientUidProvider ?? throw new ArgumentNullException(nameof(masterClientUidProvider));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValid() =>
            _validator != null && _validator();

        internal void OnBeforeUpdateWorldState(float worldDelta)
        {
        }

        internal NetWorldPlayer Register(ClientUid uid, bool isLocal)
        {
            if (_playersCollection.ContainsKey(uid))
                throw new InvalidOperationException();

            var newPlayer = new NetWorldPlayer(uid, name: "Unknown", isLocal, masterPlayerUidProvider: () => MasterPlayer?.Uid ?? ClientUid.Invalid);

            _playersCollection.Add(uid, newPlayer);
            if (isLocal)
                LocalPlayer = newPlayer;

            MasterPlayer = Get(uid: _masterClientUidProvider());
            return newPlayer;
        }

        internal bool Unregister(ClientUid uid, out NetWorldPlayerSnapshot snapshot)
        {
            if (LocalPlayer != null && LocalPlayer.Uid == uid)
                LocalPlayer = null;

            snapshot = default;

            // NOTE: we can't use 'Remove' to take a player for snapshot, as player will become invalid once it removed from the dictionary.
            if (_playersCollection.TryGetValue(uid, out var player))
            {
                snapshot = player.ToSnapshot();
                player.Invalidate();
            }

            var playerRemoved = _playersCollection.Remove(uid);
            MasterPlayer = Get(uid: _masterClientUidProvider());
            return playerRemoved;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public NetWorldPlayer Get(ClientUid uid)
        {
            EnsureValid();
            return _playersCollection.GetValueOrDefault(uid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(ClientUid uid)
        {
            EnsureValid();
            return _playersCollection.ContainsKey(uid);
        }

        internal void Invalidate()
        {
            _validator = null;

            LocalPlayer = null;
            MasterPlayer = null;

            foreach (var player in _playersCollection.Values)
                player.Invalidate();

            _playersCollection.Clear();
            _playersCollection = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public NetWorldPlayersCollectionsSnapshot ToSnapshot()
        {
            EnsureValid();
            return new NetWorldPlayersCollectionsSnapshot(collection: this);
        }

        internal void RestoreFromSnapshot(NetWorldPlayersCollectionsSnapshot snapshot)
        {
            // Use invalid references for local and master - player collection snapshot only used for the world history buffer.
            LocalPlayer = null;
            MasterPlayer = null;

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
                        new NetWorldPlayer(playerSnapshot.Uid, playerSnapshot.Name, isLocal: false, masterPlayerUidProvider: () => MasterPlayer?.Uid ?? ClientUid.Invalid));
            }
        }

        internal int GetVersionHash()
        {
            var localPlayerUid = LocalPlayer?.Uid ?? ClientUid.Invalid;
            var masterPlayerUid = MasterPlayer?.Uid ?? ClientUid.Invalid;

            var hash = 13;
            hash = hash * 7 + localPlayerUid.GetHashCode();
            hash = hash * 7 + masterPlayerUid.GetHashCode();
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