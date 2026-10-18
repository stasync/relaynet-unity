using Keel.Networking;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Keel.RelayNet.StateManagement.Snapshots;
using Keel.RelayNet.StateManagement.Values.Serialization;

namespace Keel.RelayNet.StateManagement
{
    public sealed class NetProperties
    {
        private readonly Dictionary<uint, byte> _byteData = new();
        private readonly Dictionary<uint, uint> _uintData = new();
        private readonly Dictionary<uint, int> _intData = new();
        private readonly Dictionary<uint, float> _floatData = new();
        private readonly Dictionary<uint, bool> _boolData = new();
        private readonly Dictionary<uint, string> _stringData = new();
        private readonly Dictionary<uint, UnityEngine.Vector3> _vector3Data = new();

        private readonly NetWriter _writer = new();
        private readonly NetReader _reader = new();

        private Func<bool> _validator;
        private Func<bool> _writePermission;

        internal NetProperties(Func<bool> validator, Func<bool> writePermission)
        {
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _writePermission = writePermission ?? throw new ArgumentNullException(nameof(writePermission));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValid() =>
            _validator != null && _validator();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureValid()
        {
            if (!IsValid())
                throw new InvalidOperationException($"Object of type '{GetType().FullName}' is invalid.");
        }

        internal void Invalidate()
        {
            Clear();

            _validator = null;
            _writePermission = null;
        }

        internal NetworkPropertiesSnapshot ToSnapshot()
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
            WriteUnmanagedDictionary(writer, _uintData);
            WriteUnmanagedDictionary(writer, _intData);
            WriteUnmanagedDictionary(writer, _floatData);
            WriteUnmanagedDictionary(writer, _boolData);
            WriteUnmanagedDictionary(writer, _vector3Data);

            // String is a special case here, as it's managed.
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
            ReadUnmanagedDictionary(reader, _uintData);
            ReadUnmanagedDictionary(reader, _intData);
            ReadUnmanagedDictionary(reader, _floatData);
            ReadUnmanagedDictionary(reader, _boolData);
            ReadUnmanagedDictionary(reader, _vector3Data);

            // String is a special case here, as it's managed.
            {
                _stringData.Clear();
                var count = reader.ReadPackedUInt32();
                for (var i = 0; i < count; i++)
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
            hash += GetDictionaryHashCode(_uintData);
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
            for (var i = 0; i < count; i++)
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

        #region PUBLIC_API

        public void SetByte(uint id, byte value)
        {
            EnsureValid();
            if (!_writePermission())
                throw new InvalidOperationException("No authority");

            _byteData[id] = value;
        }

        public byte GetByte(uint id)
        {
            EnsureValid();
            return _byteData.GetValueOrDefault(id);
        }

        public void SetUInt(uint id, uint value)
        {
            EnsureValid();
            if (!_writePermission())
                throw new InvalidOperationException("No authority");

            _uintData[id] = value;
        }

        public uint GetUInt(uint id)
        {
            EnsureValid();
            return _uintData.GetValueOrDefault(id);
        }

        public void SetInt(uint id, int value)
        {
            EnsureValid();
            if (!_writePermission())
                throw new InvalidOperationException("No authority");

            _intData[id] = value;
        }

        public int GetInt(uint id)
        {
            EnsureValid();
            return _intData.GetValueOrDefault(id);
        }

        public void SetFloat(uint id, float value)
        {
            EnsureValid();
            if (!_writePermission())
                throw new InvalidOperationException("No authority");

            _floatData[id] = value;
        }

        public float GetFloat(uint id)
        {
            EnsureValid();
            return _floatData.GetValueOrDefault(id);
        }

        public void SetBool(uint id, bool value)
        {
            EnsureValid();
            if (!_writePermission())
                throw new InvalidOperationException("No authority");

            _boolData[id] = value;
        }

        public bool GetBool(uint id)
        {
            EnsureValid();
            return _boolData.GetValueOrDefault(id);
        }

        public void SetVector(uint id, UnityEngine.Vector3 value)
        {
            EnsureValid();
            if (!_writePermission())
                throw new InvalidOperationException("No authority");

            _vector3Data[id] = value;
        }

        public UnityEngine.Vector3 GetVector(uint id)
        {
            EnsureValid();
            return _vector3Data.GetValueOrDefault(id);
        }

        public void SetString(uint id, string value)
        {
            EnsureValid();
            if (!_writePermission())
                throw new InvalidOperationException("No authority");

            _stringData[id] = value;
        }

        public string GetString(uint id)
        {
            EnsureValid();
            return _stringData.GetValueOrDefault(id);
        }

        public void Clear()
        {
            _byteData.Clear();
            _uintData.Clear();
            _intData.Clear();
            _floatData.Clear();
            _boolData.Clear();
            _vector3Data.Clear();
            _stringData.Clear();
        }

        #endregion
    }
}