using Core.Networking;
using System;
using System.Collections.Generic;

namespace LROTech.Network.State
{
    public class NetWorldPlayer : IComparable<NetWorldPlayer>, IEquatable<NetWorldPlayer>
    {
        public ClientUID Uid { get; private set; }

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

        public UserDeviceType.Platform Platform
        {
            get => m_Platform;
            set
            {
                if (!IsLocal)
                    throw new Exception($"Trying to change platform without authority, this is not allowed. Player id: {Uid}.");
                m_Platform = value;
            }
        }

        private UserDeviceType.Platform m_Platform = UserDeviceType.Platform.Unknown;

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

        public IProperties Properties => m_Properties;
        private readonly Properties m_Properties;

        public IReadOnlyCollection<NetObjectUID> TagObjectUidCollection => m_TagObjectUidCollection;
        private HashSet<NetObjectUID> m_TagObjectUidCollection;

        public NetObjectUID MainTagObjectUid { get; private set; } = NetObjectUID.Invalid;

        #endregion

        public void RegisterTagObject(NetObjectUID uid, bool main = false)
        {
            if (!IsLocal)
                throw new InvalidOperationException($"Trying to register tag object without authority, this is not allowed. Player id: {uid}.");

            if (!uid.IsValid)
                throw new InvalidOperationException("Trying to register invalid object Uid as tag object.");

            m_TagObjectUidCollection.Add(uid);
            if (main)
                MainTagObjectUid = uid;
        }

        public void UnregisterTagObject(NetObjectUID uid)
        {
            if (!IsLocal)
                throw new InvalidOperationException($"Trying to unregister tag object without authority, this is not allowed. Player id: {uid}.");

            if (!uid.IsValid)
                throw new InvalidOperationException("Trying to register invalid object Uid as tag object.");

            m_TagObjectUidCollection.Remove(uid);
            if (MainTagObjectUid == uid)
                MainTagObjectUid = NetObjectUID.Invalid;
        }

        public bool IsValid => Uid.IsValid;
        public bool IsLocal => IsValid && Uid.Equals(other: m_LocalPlayerUidProvider());
        public bool IsMaster => IsValid && Uid.Equals(other: m_MasterPlayerUidProvider());

        private readonly Func<ClientUID> m_LocalPlayerUidProvider;
        private readonly Func<ClientUID> m_MasterPlayerUidProvider;
        private readonly NetWriter m_Writer = new();

        internal NetWorldPlayer(ClientUID uid, string name, Func<ClientUID> localPlayerUidProvider, Func<ClientUID> masterPlayerUidProvider)
        {
            Uid = uid;
            m_Name = name;
            m_LocalPlayerUidProvider = localPlayerUidProvider;
            m_MasterPlayerUidProvider = masterPlayerUidProvider;

            m_Properties = new Properties(writePermission: () => IsLocal);
            m_TagObjectUidCollection = new HashSet<NetObjectUID>();
        }

        internal void RestoreFromSnapshot(NetWorldPlayerSnapshot snapshot)
        {
            Uid = snapshot.UID;
            m_Name = snapshot.Name;
            m_IsReady = snapshot.IsInGameScene;
            m_Platform = snapshot.Platform;
            m_RTTMS = snapshot.RTTMs;
            m_TagObjectUidCollection = snapshot.TagObjectUIDs;

            m_Properties.SetDataFromSnapshot(snapshot.Properties);
        }

        public bool HasTagObject(NetObjectUID uid) => uid.IsValid && m_TagObjectUidCollection.Contains(uid);
        public bool HasTagObject(NetObjectState state) => state != null && HasTagObject(state.UID);

        public NetWorldPlayerSnapshot ToSnapshot() =>
            NetWorldPlayerSnapshot.FormPlayer(this);

        public override string ToString() => $"{Uid}:{Name} l:{IsLocal} m:{IsMaster}";
        public int CompareTo(NetWorldPlayer other) => Uid.CompareTo(other.Uid);
        public bool Equals(NetWorldPlayer other) => other != null && Uid.Equals(other.Uid);

        public byte[] Serialize()
        {
            m_Writer.SeekZero();
            ToSnapshot().Serialize(m_Writer);
            return m_Writer.ToArray();
        }

        public override int GetHashCode() =>
            Uid.GetHashCode();
    }
}