using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Core;
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
                if (!IsLocal())
                    throw new InvalidOperationException($"Trying to change remote player name, this is not allowed. Player id: {Uid}.");
                _name = value;
            }
        }

        private string _name;

        public bool IsReady
        {
            get => _isReady;
            set
            {
                if (!IsLocal())
                    throw new InvalidOperationException($"Trying to change is ready on remote player, this is not allowed. Player id: {Uid}.");
                _isReady = value;
            }
        }

        private bool _isReady;

        public UserDevice.Platform Platform
        {
            get => _platform;
            set
            {
                if (!IsLocal())
                    throw new InvalidOperationException($"Trying to change platform without authority, this is not allowed. Player id: {Uid}.");
                _platform = value;
            }
        }

        private UserDevice.Platform _platform = UserDevice.Platform.Unknown;

        public int RttMs
        {
            get => _rttMs;
            set
            {
                if (!IsLocal())
                    throw new InvalidOperationException($"Trying to change RTT value without authority, this is not allowed. Player id: {Uid}.");
                _rttMs = value;
            }
        }

        private int _rttMs;

        public readonly NetProperties Properties;

        public IReadOnlyCollection<NetObjectUid> TagObjectUidCollection => _tagObjectUidCollection;
        private readonly HashSet<NetObjectUid> _tagObjectUidCollection = new();

        public NetObjectUid MainTagObjectUid { get; private set; } = NetObjectUid.Invalid;

        public float WorldTime { get; private set; }

        #endregion

        private readonly Func<ClientUid> _masterPlayerUidProvider;
        private readonly Func<ClientUid, bool> _validator;
        private readonly bool _isLocal;

        internal NetWorldPlayer(ClientUid uid, string name, bool isLocal, Func<ClientUid, bool> validator, Func<ClientUid> masterPlayerUidProvider)
        {
            Uid = uid;

            _name = name;
            _isLocal = isLocal;
            _validator = validator;
            _masterPlayerUidProvider = masterPlayerUidProvider;

            Properties = new NetProperties(validator: IsValid, writePermission: IsLocal);
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
            MainTagObjectUid = snapshot.MainTagObject;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsLocal() => 
            IsValid() && _isLocal;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsMaster() => 
            IsValid() && Uid.Equals(other: _masterPlayerUidProvider());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValid() =>
            Uid.IsValid && _validator != null && _validator(Uid);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasTagObject(NetObjectUid uid)
        {
            EnsureValid();
            return uid.IsValid && _tagObjectUidCollection.Contains(uid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            return $"{Uid}:{Name} l:{IsLocal()} m:{IsMaster()}";
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
            hash = hash * 7 + MainTagObjectUid.GetHashCode();
            hash = hash * 7 + WorldTime.GetHashCode();
            return hash;
        }

        public void RegisterTagObject(NetObjectUid uid, bool main = false)
        {
            EnsureValid();

            if (!IsLocal())
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

            if (!IsLocal())
                throw new InvalidOperationException($"Trying to unregister tag object without authority, this is not allowed. Player id: {uid}.");

            if (!uid.IsValid)
                throw new InvalidOperationException("Trying to register invalid object Uid as tag object.");

            _tagObjectUidCollection.Remove(uid);
            if (MainTagObjectUid == uid)
                MainTagObjectUid = NetObjectUid.Invalid;
        }

        internal void PrepareForSerialization(float worldTime)
        {
            if (!IsLocal())
                throw new InvalidOperationException($"No authority to change '{Uid}' player.");

            WorldTime = worldTime;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureValid()
        {
            if (!IsValid())
                throw new InvalidOperationException($"Object of type '{GetType().FullName}' is invalid.");
        }
    }
}