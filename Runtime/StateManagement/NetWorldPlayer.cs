using Core.Networking;
using System;
using System.Collections.Generic;
using Core;
using LROTech.Network.StateManagement.Snapshots;

namespace LROTech.Network.StateManagement
{
    public class NetWorldPlayer
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
        private HashSet<NetObjectUid> m_TagObjectUidCollection;

        public NetObjectUid MainTagObjectUid { get; private set; } = NetObjectUid.Invalid;

        #endregion

        public void RegisterTagObject(NetObjectUid uid, bool main = false)
        {
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
            if (!IsLocal)
                throw new InvalidOperationException($"Trying to unregister tag object without authority, this is not allowed. Player id: {uid}.");

            if (!uid.IsValid)
                throw new InvalidOperationException("Trying to register invalid object Uid as tag object.");

            m_TagObjectUidCollection.Remove(uid);
            if (MainTagObjectUid == uid)
                MainTagObjectUid = NetObjectUid.Invalid;
        }

        public bool IsValid => Uid.IsValid;
        public bool IsLocal => IsValid && Uid.Equals(other: m_LocalPlayerUidProvider());
        public bool IsMaster => IsValid && Uid.Equals(other: m_MasterPlayerUidProvider());

        private readonly Func<ClientUid> m_LocalPlayerUidProvider;
        private readonly Func<ClientUid> m_MasterPlayerUidProvider;
        private readonly NetWriter m_Writer = new();

        internal NetWorldPlayer(ClientUid uid, string name, Func<ClientUid> localPlayerUidProvider, Func<ClientUid> masterPlayerUidProvider)
        {
            Uid = uid;
            m_Name = name;
            m_LocalPlayerUidProvider = localPlayerUidProvider;
            m_MasterPlayerUidProvider = masterPlayerUidProvider;

            Properties = new NetworkProperties(writePermission: () => IsLocal);
            m_TagObjectUidCollection = new HashSet<NetObjectUid>();
        }

        internal void RestoreFromSnapshot(NetWorldPlayerSnapshot snapshot)
        {
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

        public bool HasTagObject(NetObjectUid uid) =>
            uid.IsValid && m_TagObjectUidCollection.Contains(uid);

        public bool HasTagObject(NetObjectState state) =>
            state != null && HasTagObject(state.Uid);

        public NetWorldPlayerSnapshot ToSnapshot() =>
            new(player: this);

        public override string ToString() =>
            $"{Uid}:{Name} l:{IsLocal} m:{IsMaster}";

        internal int GetVersionHash()
        {
            var hash = 13;
            hash = hash * 7 + Uid.GetHashCode();
            hash = hash * 7 + Name.GetHashCode();
            hash = hash * 7 + IsReady.GetHashCode();
            hash = hash * 7 + Platform.GetHashCode();
            hash = hash * 7 + Properties.GetVersionHash();
            return hash;
        }
    }
}