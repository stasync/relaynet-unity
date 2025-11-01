using Core.Networking;
using System;
using System.Collections.Generic;
using LROTech.Network.StateManagement.Snapshots;
using LROTech.Network.StateManagement.Values.Serialization;

namespace LROTech.Network.StateManagement
{
    public sealed class NetworkProperties
    {
        private readonly Dictionary<uint, byte> _byteData = new();
        private readonly Dictionary<uint, uint> _uIntData = new();
        private readonly Dictionary<uint, int> _intData = new();
        private readonly Dictionary<uint, float> _floatData = new();
        private readonly Dictionary<uint, bool> _boolData = new();
        private readonly Dictionary<uint, string> _stringData = new();
        private readonly Dictionary<uint, UnityEngine.Vector3> _vector3Data = new();

        private readonly Func<bool> _writePermission;
        private readonly NetWriter _writer = new();
        private readonly NetReader _reader = new();

        internal NetworkProperties(Func<bool> writePermission) =>
            _writePermission = writePermission ?? throw new ArgumentNullException(nameof(writePermission));

        internal void Clean()
        {
            _byteData.Clear();
            _uIntData.Clear();
            _intData.Clear();
            _floatData.Clear();
            _boolData.Clear();
            _vector3Data.Clear();
            _stringData.Clear();
        }
        
        public NetworkPropertiesSnapshot ToSnapshot()
        {
            _writer.SeekZero();
            Serialize(_writer);
            return new NetworkPropertiesSnapshot(_writer.ToArray());
        }

        internal void RestoreFromSnapshot(NetworkPropertiesSnapshot snapshot)
        {
            _reader.Replace(snapshot.Data);
            Deserialize(_reader);
        }
        
        internal void Serialize(NetWriter writer)
        {
            WriteUnmanagedDictionary(writer, _byteData);
            WriteUnmanagedDictionary(writer, _uIntData);
            WriteUnmanagedDictionary(writer, _intData);
            WriteUnmanagedDictionary(writer, _floatData);
            WriteUnmanagedDictionary(writer, _boolData);
            WriteUnmanagedDictionary(writer, _vector3Data);

            // String is a special case here, as its unmanaged.
            {
                writer.WritePackedUInt32((uint)_stringData.Count);
                foreach (var kvp in _stringData)
                {
                    NetworkValueSerializationAPI<uint>.Serializer.Serialize(writer, kvp.Key);
                    writer.WriteString(kvp.Value);
                }
            }
        }

        internal void Deserialize(NetReader reader)
        {
            ReadUnmanagedDictionary(reader, _byteData);
            ReadUnmanagedDictionary(reader, _uIntData);
            ReadUnmanagedDictionary(reader, _intData);
            ReadUnmanagedDictionary(reader, _floatData);
            ReadUnmanagedDictionary(reader, _boolData);
            ReadUnmanagedDictionary(reader, _vector3Data);

            // String is a special case here, as its unmanaged.
            {
                _stringData.Clear();
                var count = reader.ReadPackedUInt32();
                for(var i = 0; i < count; i++)
                {
                    var key = NetworkValueSerializationAPI<uint>.Serializer.Deserialize(reader);
                    var value = reader.ReadString();
                    _stringData.Add(key, value);
                }
            }
        }

        internal int GetVersionHash()
        {
            var hash = 0;
            hash += GetDictionaryHashCode(_byteData);
            hash += GetDictionaryHashCode(_uIntData);
            hash += GetDictionaryHashCode(_intData);
            hash += GetDictionaryHashCode(_floatData);
            hash += GetDictionaryHashCode(_boolData);
            hash += GetDictionaryHashCode(_vector3Data);
            hash += GetDictionaryHashCode(_stringData);

            return hash;
        }
        
        private static void WriteUnmanagedDictionary<TValue>(NetWriter writer, Dictionary<uint, TValue> dictionary) where TValue : unmanaged
        {
            writer.WritePackedUInt32((uint)dictionary.Count);
            foreach (var kvp in dictionary)
            {
                NetworkValueSerializationAPI<uint>.Serializer.Serialize(writer, kvp.Key);
                NetworkValueSerializationAPI<TValue>.Serializer.Serialize(writer, kvp.Value);
            }
        }
        
        private static void ReadUnmanagedDictionary<TValue>(NetReader reader, Dictionary<uint, TValue> dictionary) where TValue : unmanaged
        {
            dictionary.Clear();
            var count = reader.ReadPackedUInt32();
            for(var i = 0; i < count; i++)
            {
                var key = NetworkValueSerializationAPI<uint>.Serializer.Deserialize(reader);
                var value = NetworkValueSerializationAPI<TValue>.Serializer.Deserialize(reader);
                dictionary.Add(key, value);
            }
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

        #region CUSTOPROPERTIES_API

        public void SetByte(uint id, byte value)
        {
            if (!_writePermission())
                throw new InvalidOperationException("No authority");

            _byteData[id] = value;
        }

        public byte GetByte(uint id) =>
            _byteData.GetValueOrDefault(id);

        public void SetUInt(uint id, uint value)
        {
            if (!_writePermission())
                throw new InvalidOperationException("No authority");

            _uIntData[id] = value;
        }

        public uint GetUInt(uint id) =>
            _uIntData.GetValueOrDefault(id);

        public void SetInt(uint id, int value)
        {
            if (!_writePermission())
                throw new InvalidOperationException("No authority");

            _intData[id] = value;
        }

        public int GetInt(uint id) =>
            _intData.GetValueOrDefault(id);

        public void SetFloat(uint id, float value)
        {
            if (!_writePermission())
                throw new InvalidOperationException("No authority");

            _floatData[id] = value;
        }

        public float GetFloat(uint id) =>
            _floatData.GetValueOrDefault(id);

        public void SetBool(uint id, bool value)
        {
            if (!_writePermission())
                throw new InvalidOperationException("No authority");

            _boolData[id] = value;
        }

        public bool GetBool(uint id) =>
            _boolData.GetValueOrDefault(id);

        public void SetVector(uint id, UnityEngine.Vector3 value)
        {
            if (!_writePermission())
                throw new InvalidOperationException("No authority");

            _vector3Data[id] = value;
        }

        public UnityEngine.Vector3 GetVector(uint id) =>
            _vector3Data.GetValueOrDefault(id);
        
        public void SetString(uint id, string value)
        {
            if (!_writePermission())
                throw new InvalidOperationException("No authority");

            _stringData[id] = value;
        }

        public string GetString(uint id) =>
            _stringData.GetValueOrDefault(id);

        #endregion
    }
}