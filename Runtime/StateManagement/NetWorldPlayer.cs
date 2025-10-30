using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Core;
using LROTech.Network.Sessions;
using LROTech.Network.StateManagement.Snapshots;

namespace LROTech.Network.StateManagement
{
    public sealed class NetWorldPlayer
    {
        public ClientUid Uid { get; private set; }

        #region Values

        public string Name
        {
            get => _name;
            set
            {
                if (!IsLocal)
                    throw new Exception($"Trying to change remote player name, this is not allowed. Player id: {Uid}.");
                _name = value;
            }
        }

        private string _name;

        public bool IsReady
        {
            get => _isReady;
            set
            {
                if (!IsLocal)
                    throw new Exception($"Trying to change is ready on remote player, this is not allowed. Player id: {Uid}.");
                _isReady = value;
            }
        }

        private bool _isReady;

        public UserDevice.Platform Platform
        {
            get => _platform;
            set
            {
                if (!IsLocal)
                    throw new Exception($"Trying to change platform without authority, this is not allowed. Player id: {Uid}.");
                _platform = value;
            }
        }

        private UserDevice.Platform _platform = UserDevice.Platform.Unknown;

        public int RttMs
        {
            get => _rttMs;
            set
            {
                if (!IsLocal)
                    throw new Exception($"Trying to change RTT value without authority, this is not allowed. Player id: {Uid}.");
                _rttMs = value;
            }
        }

        private int _rttMs;

        public readonly NetworkProperties Properties;

        public IReadOnlyCollection<NetObjectUid> TagObjectUidCollection => _tagObjectUidCollection;
        private readonly HashSet<NetObjectUid> _tagObjectUidCollection = new();

        public NetObjectUid MainTagObjectUid { get; private set; } = NetObjectUid.Invalid;

        #endregion

        public bool IsLocal => IsValid() && Uid.Equals(other: _localPlayerUidProvider());
        public bool IsMaster => IsValid() && Uid.Equals(other: _masterPlayerUidProvider());

        private readonly Func<ClientUid> _localPlayerUidProvider;
        private readonly Func<ClientUid> _masterPlayerUidProvider;
        private readonly Func<ClientUid, bool> _validator;

        internal NetWorldPlayer(Func<ClientUid, bool> validator, ClientUid uid, string name, Func<ClientUid> localPlayerUidProvider, Func<ClientUid> masterPlayerUidProvider)
        {
            _validator = validator;
            Uid = uid;
            _name = name;
            _localPlayerUidProvider = localPlayerUidProvider;
            _masterPlayerUidProvider = masterPlayerUidProvider;

            Properties = new NetworkProperties(writePermission: () => IsLocal);
        }

        internal void RestoreFromSnapshot(NetWorldPlayerSnapshot snapshot)
        {
            EnsureValid();
            
            Uid = snapshot.Uid;
            _name = snapshot.Name;
            _isReady = snapshot.IsInGameScene;
            _platform = snapshot.Platform;
            _rttMs = snapshot.RTTMs;

            _tagObjectUidCollection.Clear();
            foreach (var tagObject in snapshot.TagObjects)
                _tagObjectUidCollection.Add(tagObject);

            Properties.RestoreFromSnapshot(snapshot.Properties);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValid() => 
            Uid.IsValid && _validator != null && _validator(Uid);

        public bool HasTagObject(NetObjectUid uid)
        {
            EnsureValid();
            return uid.IsValid && _tagObjectUidCollection.Contains(uid);
        }
           
        public bool HasTagObject(NetObjectState state)
        {
            EnsureValid();
            return state != null && HasTagObject(state.Uid);
        }

        public NetWorldPlayerSnapshot ToSnapshot()
        {
            EnsureValid();
            return new NetWorldPlayerSnapshot(player: this);
        }

        public override string ToString()
        {
            EnsureValid();
            return $"{Uid}:{Name} l:{IsLocal} m:{IsMaster}";
        }

        internal int GetVersionHash()
        {
            EnsureValid();
            
            var hash = 13;
            hash = hash * 7 + Uid.GetHashCode();
            hash = hash * 7 + Name.GetHashCode();
            hash = hash * 7 + IsReady.GetHashCode();
            hash = hash * 7 + Platform.GetHashCode();
            hash = hash * 7 + Properties.GetVersionHash();
            return hash;
        }

        public void RegisterTagObject(NetObjectUid uid, bool main = false)
        {
            EnsureValid();
            
            if (!IsLocal)
                throw new InvalidOperationException($"Trying to register tag object without authority, this is not allowed. Player id: {uid}.");

            if (!uid.IsValid)
                throw new InvalidOperationException("Trying to register invalid object Uid as tag object.");

            _tagObjectUidCollection.Add(uid);
            if (main)
                MainTagObjectUid = uid;
        }

        public void UnregisterTagObject(NetObjectUid uid)
        {
            EnsureValid();
            
            if (!IsLocal)
                throw new InvalidOperationException($"Trying to unregister tag object without authority, this is not allowed. Player id: {uid}.");

            if (!uid.IsValid)
                throw new InvalidOperationException("Trying to register invalid object Uid as tag object.");

            _tagObjectUidCollection.Remove(uid);
            if (MainTagObjectUid == uid)
                MainTagObjectUid = NetObjectUid.Invalid;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureValid()
        {
            if (!IsValid())
                throw new InvalidOperationException($"Object of type '{GetType().FullName}' is invalid.");
        }
    }
}