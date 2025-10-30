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
            get => m_Name;
            set
            {
                if (!IsLocal)
                    throw new Exception($"Trying to change remote player name, this is not allowed. Player id: {Uid}.");
                m_Name = value;
            }
        }

        private string m_Name;

        public bool IsReady
        {
            get => m_IsReady;
            set
            {
                if (!IsLocal)
                    throw new Exception($"Trying to change is ready on remote player, this is not allowed. Player id: {Uid}.");
                m_IsReady = value;
            }
        }

        private bool m_IsReady;

        public UserDevice.Platform Platform
        {
            get => m_Platform;
            set
            {
                if (!IsLocal)
                    throw new Exception($"Trying to change platform without authority, this is not allowed. Player id: {Uid}.");
                m_Platform = value;
            }
        }

        private UserDevice.Platform m_Platform = UserDevice.Platform.Unknown;

        public int RTTMs
        {
            get => m_RTTMS;
            set
            {
                if (!IsLocal)
                    throw new Exception($"Trying to change RTT value without authority, this is not allowed. Player id: {Uid}.");
                m_RTTMS = value;
            }
        }

        private int m_RTTMS;

        public readonly NetworkProperties Properties;

        public IReadOnlyCollection<NetObjectUid> TagObjectUidCollection => m_TagObjectUidCollection;
        private readonly HashSet<NetObjectUid> m_TagObjectUidCollection = new();

        public NetObjectUid MainTagObjectUid { get; private set; } = NetObjectUid.Invalid;

        #endregion

        public bool IsLocal => IsValid() && Uid.Equals(other: m_LocalPlayerUidProvider());
        public bool IsMaster => IsValid() && Uid.Equals(other: m_MasterPlayerUidProvider());

        private readonly Func<ClientUid> m_LocalPlayerUidProvider;
        private readonly Func<ClientUid> m_MasterPlayerUidProvider;
        private readonly Func<ClientUid, bool> m_Validator;

        internal NetWorldPlayer(Func<ClientUid, bool> validator, ClientUid uid, string name, Func<ClientUid> localPlayerUidProvider, Func<ClientUid> masterPlayerUidProvider)
        {
            m_Validator = validator;
            Uid = uid;
            m_Name = name;
            m_LocalPlayerUidProvider = localPlayerUidProvider;
            m_MasterPlayerUidProvider = masterPlayerUidProvider;

            Properties = new NetworkProperties(writePermission: () => IsLocal);
        }

        internal void RestoreFromSnapshot(NetWorldPlayerSnapshot snapshot)
        {
            EnsureValid();
            
            Uid = snapshot.UID;
            m_Name = snapshot.Name;
            m_IsReady = snapshot.IsInGameScene;
            m_Platform = snapshot.Platform;
            m_RTTMS = snapshot.RTTMs;

            m_TagObjectUidCollection.Clear();
            foreach (var tagObject in snapshot.TagObjectUIDs)
                m_TagObjectUidCollection.Add(tagObject);

            Properties.RestoreFromSnapshot(snapshot.Properties);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValid() => 
            Uid.IsValid && m_Validator != null && m_Validator(Uid);

        public bool HasTagObject(NetObjectUid uid)
        {
            EnsureValid();
            return uid.IsValid && m_TagObjectUidCollection.Contains(uid);
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

            m_TagObjectUidCollection.Add(uid);
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

            m_TagObjectUidCollection.Remove(uid);
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