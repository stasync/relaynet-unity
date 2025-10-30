using System;
using Core.Networking;

namespace LROTech.Network.StateManagement
{
    public sealed class NetObjectViewStateData : NetObjectStateData
    {
        public bool ViewInitialized { get; private set; }
        public byte[] UnstructuredData { get; private set; } = Array.Empty<byte>();

        protected override void OnSerialize(NetWriter writer)
        {
            base.OnSerialize(writer);

            writer.WriteBool(ViewInitialized);
            writer.WriteBytesAndSize(UnstructuredData);
        }

        protected override void OnDeserialize(NetReader reader)
        {
            base.OnDeserialize(reader);

            ViewInitialized = reader.ReadBoolean();
            UnstructuredData = reader.ReadBytesAndSize();
        }

        public void SetData(byte[] data, bool initialization = false)
        {
            if (!HasWritePermission)
                throw new InvalidOperationException();

            UnstructuredData = data;

            if (initialization)
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
        public override System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, string>> GetValuesInfo_Editor()
        {
            foreach (var pair in base.GetValuesInfo_Editor())
                yield return pair;

            yield return new System.Collections.Generic.KeyValuePair<string, string>(nameof(ViewInitialized), ViewInitialized.ToString());
            yield return new System.Collections.Generic.KeyValuePair<string, string>(nameof(UnstructuredData), UnstructuredData.Length.ToString());
        }
#endif
    }
}