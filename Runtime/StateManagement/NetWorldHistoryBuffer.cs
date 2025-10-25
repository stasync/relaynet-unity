using System.Collections;
using System.Collections.Generic;
using LROTech.Network.StateManagement.Snapshots;

namespace LROTech.Network.StateManagement
{
    public sealed class NetWorldHistoryBuffer : IReadOnlyCollection<NetWorldSnapshot>
    {
        public int Size { get; private set; }
        public int AverageFrameSize { get; private set; }
        public float SizeMB => Size * 0.000001f;
        public int Count => m_HistoryBuffer.Count;
        public float LengthInSeconds { get; private set; }

        private readonly Queue<NetWorldSnapshot> m_HistoryBuffer = new();

        internal void Enqueue(NetWorldSnapshot snapshot)
        {
            m_HistoryBuffer.Enqueue(snapshot);

            Size += snapshot.GetSize();
            LengthInSeconds += snapshot.DeltaTime;

            RecalculateAverageSize();
        }

        internal void Dequeue()
        {
            var snapshot = m_HistoryBuffer.Dequeue();

            Size -= snapshot.GetSize();
            LengthInSeconds -= snapshot.DeltaTime;

            RecalculateAverageSize();
        }

        internal void Clear()
        {
            m_HistoryBuffer.Clear();

            Size = 0;
            LengthInSeconds = 0f;

            RecalculateAverageSize();
        }

        private void RecalculateAverageSize() =>
            AverageFrameSize = Count == 0 ? 0 : Size / Count;

        public IEnumerator<NetWorldSnapshot> GetEnumerator() =>
            m_HistoryBuffer.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() =>
            m_HistoryBuffer.GetEnumerator();
    }
}