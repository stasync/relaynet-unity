using System;
using Core.Networking;

namespace LROTech.Network.State
{
    public sealed class NetObjectViewStateData : NetObjectStateData
    {
        public bool ViewInitialized { get; private set; }
        public byte[] UnstructuredData { get; private set; }

        public NetObjectViewStateData(Func<bool> writePermission) : base(writePermission)
        {
            ViewInitialized = false;
            UnstructuredData = Array.Empty<byte>();
        }

        protected override void OnSerialize(NetWriter writer)
        {
            base.OnSerialize(writer);

            writer.WriteBool(ViewInitialized);
            writer.WriteBytesAndSize(UnstructuredData);
        }

        protected override void OnDeserialize(NetReader reader, bool firstTime)
        {
            base.OnDeserialize(reader, firstTime);

            ViewInitialized = reader.ReadBoolean();
            UnstructuredData = reader.ReadBytesAndSize();
        }

        public void SetData(byte[] data, bool initialization = false)
        {
            if (!WritePermission())
                throw new InvalidOperationException();

            UnstructuredData = data;

            if (initialization)
                ViewInitialized = true;
        }

        public override int GetHashCode()
        {
            var hash = base.GetHashCode();

            hash += ViewInitialized.GetHashCode();
            foreach (var d in UnstructuredData)
                hash = hash * 7 + d.GetHashCode();
            return hash;
        }

#if UNITY_EDITOR
        public override System.Collections.Generic.IEnumerable<string> GetValuesInfo_Editor()
        {
            foreach (var pair in base.GetValuesInfo_Editor())
                yield return pair;

            yield return $"{nameof(ViewInitialized)}:{ViewInitialized}";
            yield return $"{nameof(UnstructuredData)}:{UnstructuredData.Length}";
        }
#endif
    }
}