using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Keel.RelayNet.GameObjects;
using Keel.RelayNet.StateManagement;
using UnityEngine;

namespace Keel.RelayNet.Editor
{
    /// <summary>
    /// A world or one of its objects as JSON, for the debug window's copy buttons. It's a dump to read and diff, not
    /// a format to load back: the values of an object's data are the strings the inspector shows.
    /// </summary>
    internal static class NetDebugJson
    {
        public static string World(NetWorld world)
        {
            var writer = new Writer();
            writer.BeginObject();
            writer.Value("name", world.Name);
            writer.Value("uid", (int)world.Uid);
            writer.Value("time", world.Time);
            writer.Value("localTime", world.LocalTime);
            writer.Value("hash", world.GetVersionHash());
            WriteProperties(writer, "properties", world.Properties);

            writer.BeginArray("players");
            foreach (var player in world.Players.All)
                WritePlayer(writer, player);
            writer.EndArray();

            writer.BeginArray("objects");
            foreach (var state in world.ObjectStates.Values)
                WriteObject(writer, state);
            writer.EndArray();

            writer.EndObject();
            return writer.ToString();
        }

        public static string Object(NetObjectState state)
        {
            var writer = new Writer();
            WriteObject(writer, state);
            return writer.ToString();
        }

        private static void WritePlayer(Writer writer, NetWorldPlayer player)
        {
            writer.BeginObject();
            writer.Value("uid", (uint)player.Uid);
            writer.Value("name", player.Name);
            writer.Value("local", player.IsLocal());
            writer.Value("master", player.IsMaster());
            writer.Value("ready", player.IsReady);
            writer.Value("platform", player.Platform);
            writer.Value("rttMs", player.RttMs);
            writer.Value("worldTime", player.WorldTime);
            writer.Value("mainTagObject", player.MainTagObjectUid.IsValid ? (uint?)player.MainTagObjectUid : null);

            writer.BeginArray("tagObjects");
            foreach (var uid in player.TagObjectUidCollection)
                writer.Value(null, (uint)uid);
            writer.EndArray();

            WriteProperties(writer, "properties", player.Properties);
            writer.EndObject();
        }

        private static void WriteObject(Writer writer, NetObjectState state)
        {
            writer.BeginObject();
            writer.Value("uid", (uint)state.Uid);
            writer.Value("prefabUid", state.PrefabUid);
            writer.Value("prefab", NetPrefabDatabase.TryGetPrefab(state.PrefabUid, out var prefab) && prefab != null ? prefab.name : null);
            writer.Value("shared", state.IsShared);
            writer.Value("ownerUid", state.OwnerUid.IsValid ? (uint?)state.OwnerUid : null);
            writer.Value("owner", state.Owner?.Name);
            writer.Value("ownedByLocalPlayer", state.IsOwnedByLocalPlayer);
            writer.Value("changeType", state.StateChangeType);
            writer.Value("worldTime", state.WorldTime);

            writer.BeginArray("rpcs");
            foreach (var rpc in state.RpcBuffer.TemporalBuffer)
            {
                var (delegateTargetType, methodInfo) = NetObjectRpcUtils.FindDelegateInfoByHashSlow(rpc.Hash);

                writer.BeginObject();
                writer.Value("uid", rpc.Uid);
                writer.Value("hash", rpc.Hash);
                writer.Value("handler", delegateTargetType == null ? null : $"{delegateTargetType.FullName}.{methodInfo.Name}");
                writer.Value("payloadSize", rpc.Payload.Length);
                writer.EndObject();
            }

            writer.EndArray();

            writer.BeginArray("data");
            foreach (var data in state.DataCollection)
            {
                writer.BeginObject();
                writer.Value("type", data.GetType().FullName);
                writer.Value("typeHash", data.TypeHashCode);
                writer.Value("size", data.GetSerializedSize_Editor());

                writer.BeginObject("values");
                foreach (var (fieldName, fieldValue) in data.GetValuesInfo_Editor())
                    writer.Value(fieldName, fieldValue);
                writer.EndObject();

                writer.EndObject();
            }

            writer.EndArray();
            writer.EndObject();
        }

        /// <summary>
        /// The properties keep a dictionary per value type, private like the debug window's Players tab reads them.
        /// </summary>
        private static void WriteProperties(Writer writer, string name, NetProperties properties)
        {
            writer.BeginObject(name);
            foreach (var fieldInfo in typeof(NetProperties).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (fieldInfo.GetValue(properties) is not IDictionary { Count: > 0 } dictionary)
                    continue;

                writer.BeginObject(fieldInfo.Name.TrimStart('_'));
                foreach (DictionaryEntry entry in dictionary)
                    writer.Value(entry.Key.ToString(), entry.Value);
                writer.EndObject();
            }

            writer.EndObject();
        }

        /// <summary>
        /// Indented JSON, two spaces a level. Numbers are written in the invariant culture, and a float that JSON
        /// can't hold (NaN, infinity) is written as a string.
        /// </summary>
        private sealed class Writer
        {
            private readonly StringBuilder _builder = new();

            // One entry per open object or array: whether it has an item already, which the next one follows with a comma.
            private readonly Stack<bool> _hasItems = new();

            public void BeginObject(string name = null)
            {
                BeginItem(name);
                _builder.Append('{');
                _hasItems.Push(false);
            }

            public void EndObject() =>
                End('}');

            public void BeginArray(string name = null)
            {
                BeginItem(name);
                _builder.Append('[');
                _hasItems.Push(false);
            }

            public void EndArray() =>
                End(']');

            /// <param name="name">Null for an item of an array.</param>
            public void Value(string name, object value)
            {
                BeginItem(name);
                switch (value)
                {
                    case null:
                        _builder.Append("null");
                        break;

                    case bool boolValue:
                        _builder.Append(boolValue ? "true" : "false");
                        break;

                    case float floatValue:
                        WriteNumber(floatValue);
                        break;

                    case Vector3 vector:
                        _builder.Append('[');
                        WriteNumber(vector.x);
                        _builder.Append(", ");
                        WriteNumber(vector.y);
                        _builder.Append(", ");
                        WriteNumber(vector.z);
                        _builder.Append(']');
                        break;

                    case byte or sbyte or short or ushort or int or uint or long or ulong:
                        _builder.Append(((System.IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                        break;

                    // Strings and enums, and anything else by what it prints.
                    default:
                        WriteString(value.ToString());
                        break;
                }
            }

            public override string ToString() =>
                _builder.ToString();

            private void BeginItem(string name)
            {
                if (_hasItems.Count > 0)
                {
                    if (_hasItems.Pop())
                        _builder.Append(',');

                    _hasItems.Push(true);
                    NewLine();
                }

                if (name == null)
                    return;

                WriteString(name);
                _builder.Append(": ");
            }

            private void End(char bracket)
            {
                if (_hasItems.Pop())
                    NewLine();

                _builder.Append(bracket);
            }

            private void NewLine() =>
                _builder.Append('\n').Append(' ', _hasItems.Count * 2);

            private void WriteNumber(float value)
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                    WriteString(value.ToString(CultureInfo.InvariantCulture));
                else
                    _builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
            }

            private void WriteString(string value)
            {
                _builder.Append('"');
                foreach (var character in value)
                {
                    switch (character)
                    {
                        case '"':
                            _builder.Append("\\\"");
                            break;

                        case '\\':
                            _builder.Append("\\\\");
                            break;

                        case '\n':
                            _builder.Append("\\n");
                            break;

                        case '\r':
                            _builder.Append("\\r");
                            break;

                        case '\t':
                            _builder.Append("\\t");
                            break;

                        default:
                            if (character < ' ')
                                _builder.Append("\\u").Append(((int)character).ToString("x4"));
                            else
                                _builder.Append(character);
                            break;
                    }
                }

                _builder.Append('"');
            }
        }
    }
}
