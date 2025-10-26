using Core.Networking;
using System;
using System.Collections.Generic;
using LROTech.Network.StateManagement.Snapshots;

namespace LROTech.Network.StateManagement
{
    public sealed class NetworkProperties
    {
        private readonly Dictionary<uint, byte> m_ByteData = new();
        private readonly Dictionary<uint, uint> m_UIntData = new();
        private readonly Dictionary<uint, int> m_IntData = new();
        private readonly Dictionary<uint, float> m_FloatData = new();
        private readonly Dictionary<uint, bool> m_BoolData = new();
        private readonly Dictionary<uint, string> m_StringData = new();
        private readonly Dictionary<uint, UnityEngine.Vector3> m_Vector3Data = new();

        private readonly Func<bool> m_WritePermission;
        private readonly NetWriter m_Writer = new();
        private readonly NetReader m_Reader = new();

        internal NetworkProperties(Func<bool> writePermission) =>
            m_WritePermission = writePermission ?? throw new ArgumentNullException(nameof(writePermission));

        public NetworkPropertiesSnapshot ToSnapshot()
        {
            m_Writer.SeekZero();
            Serialize(m_Writer);
            return new NetworkPropertiesSnapshot(m_Writer.ToArray());
        }

        internal void Clean()
        {
            m_ByteData.Clear();
            m_UIntData.Clear();
            m_IntData.Clear();
            m_FloatData.Clear();
            m_BoolData.Clear();
            m_StringData.Clear();
            m_Vector3Data.Clear();
        }

        internal void RestoreFromSnapshot(NetworkPropertiesSnapshot snapshot)
        {
            m_Reader.Replace(snapshot.Data);
            Deserialize(m_Reader, firstTime: false);
        }
        
        internal void Serialize(NetWriter writer)
        {
            writer.WriteDictionary(m_ByteData, (k, w) => w.WritePackedUInt32(k), (v, w) => w.WriteByte(v));
            writer.WriteDictionary(m_UIntData, (k, w) => w.WritePackedUInt32(k), (v, w) => w.WritePackedUInt32(v));
            writer.WriteDictionary(m_IntData, (k, w) => w.WritePackedUInt32(k), (v, w) => w.WriteInt32(v));
            writer.WriteDictionary(m_FloatData, (k, w) => w.WritePackedUInt32(k), (v, w) => w.WriteSingle(v));
            writer.WriteDictionary(m_BoolData, (k, w) => w.WritePackedUInt32(k), (v, w) => w.WriteBool(v));
            writer.WriteDictionary(m_StringData, (k, w) => w.WritePackedUInt32(k), (v, w) => w.WriteString(v));
            writer.WriteDictionary(m_Vector3Data, (k, w) => w.WritePackedUInt32(k), (v, w) => w.WriteVector3(v));
        }

        internal void Deserialize(NetReader reader, bool firstTime)
        {
            reader.ReadDictionaryTo(m_ByteData, r => r.ReadPackedUInt32(), r => r.ReadByte());
            reader.ReadDictionaryTo(m_UIntData, r => r.ReadPackedUInt32(), r => r.ReadPackedUInt32());
            reader.ReadDictionaryTo(m_IntData, r => r.ReadPackedUInt32(), r => r.ReadInt32());
            reader.ReadDictionaryTo(m_FloatData, r => r.ReadPackedUInt32(), r => r.ReadSingle());
            reader.ReadDictionaryTo(m_BoolData, r => r.ReadPackedUInt32(), r => r.ReadBoolean());
            reader.ReadDictionaryTo(m_StringData, r => r.ReadPackedUInt32(), r => r.ReadString());
            reader.ReadDictionaryTo(m_Vector3Data, r => r.ReadPackedUInt32(), r => r.ReadVector3());
        }

        internal int GetVersionHash()
        {
            var hash = 0;
            hash += GetDictionaryHashCode(m_ByteData);
            hash += GetDictionaryHashCode(m_UIntData);
            hash += GetDictionaryHashCode(m_IntData);
            hash += GetDictionaryHashCode(m_FloatData);
            hash += GetDictionaryHashCode(m_StringData);
            hash += GetDictionaryHashCode(m_BoolData);
            hash += GetDictionaryHashCode(m_Vector3Data);

            return hash;
        }

        private static int GetDictionaryHashCode<TKey, TValue>(Dictionary<TKey, TValue> dictionary)
        {
            var hash = 13;
            foreach (var kvp in dictionary)
            {
                hash = hash * 7 + kvp.Key.GetHashCode();
                hash = hash * 7 + kvp.Value.GetHashCode();
            }

            return hash;
        }

        #region CUSTOM_PROPERTIES_API

        public void SetByte(uint id, byte value)
        {
            if (!m_WritePermission())
                throw new InvalidOperationException("No authority");

            m_ByteData[id] = value;
        }

        public byte GetByte(uint id) =>
            m_ByteData.GetValueOrDefault(id);

        public void SetUInt(uint id, uint value)
        {
            if (!m_WritePermission())
                throw new InvalidOperationException("No authority");

            m_UIntData[id] = value;
        }

        public uint GetUInt(uint id) =>
            m_UIntData.GetValueOrDefault(id);

        public void SetInt(uint id, int value)
        {
            if (!m_WritePermission())
                throw new InvalidOperationException("No authority");

            m_IntData[id] = value;
        }

        public int GetInt(uint id) =>
            m_IntData.GetValueOrDefault(id);

        public void SetString(uint id, string value)
        {
            if (!m_WritePermission())
                throw new InvalidOperationException("No authority");

            m_StringData[id] = value;
        }

        public string GetString(uint id) =>
            m_StringData.GetValueOrDefault(id);

        public void SetFloat(uint id, float value)
        {
            if (!m_WritePermission())
                throw new InvalidOperationException("No authority");

            m_FloatData[id] = value;
        }

        public float GetFloat(uint id) =>
            m_FloatData.GetValueOrDefault(id);

        public void SetBool(uint id, bool value)
        {
            if (!m_WritePermission())
                throw new InvalidOperationException("No authority");

            m_BoolData[id] = value;
        }

        public bool GetBool(uint id) =>
            m_BoolData.GetValueOrDefault(id);

        public void SetVector(uint id, UnityEngine.Vector3 value)
        {
            if (!m_WritePermission())
                throw new InvalidOperationException("No authority");

            m_Vector3Data[id] = value;
        }

        public UnityEngine.Vector3 GetVector(uint id) =>
            m_Vector3Data.GetValueOrDefault(id);

        #endregion
    }
}