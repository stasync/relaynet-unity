using System;
using System.Collections.Generic;
using Keel.Networking;

namespace Keel.RelayNet.StateManagement
{
    public sealed class NetObjectViewStateData : NetObjectStateData
    {
        public bool ViewInitialized { get; private set; }
        public byte[] UnstructuredData { get; private set; } = Array.Empty<byte>();

        protected override void OnSerialize(NetWriter writer)
        {
            base.OnSerialize(writer);

            writer.WriteBoolean(ViewInitialized);
            writer.WriteBytesAndSize(UnstructuredData);
        }

        protected override void OnDeserialize(NetReader reader)
        {
            base.OnDeserialize(reader);

            ViewInitialized = reader.ReadBoolean();
            UnstructuredData = reader.ReadBytesAndSize();
        }

        internal void SetData(byte[] data)
        {
            if (!HasWritePermission)
                throw new InvalidOperationException("No authority to modify the state.");

            UnstructuredData = data;
            ViewInitialized = true;
        }

        internal override int GetVersionHash()
        {
            var hash = base.GetVersionHash();

            hash += ViewInitialized.GetHashCode();
            foreach (var b in UnstructuredData)
                hash = hash * 7 + b.GetHashCode();
            return hash;
        }

#if UNITY_EDITOR
        public override IEnumerable<KeyValuePair<string, string>> GetValuesInfo_Editor()
        {
            foreach (var pair in base.GetValuesInfo_Editor())
                yield return pair;

            yield return new KeyValuePair<string, string>(nameof(ViewInitialized), ViewInitialized.ToString());
            yield return new KeyValuePair<string, string>(nameof(UnstructuredData), UnstructuredData.Length.ToString());
        }
#endif
    }
}