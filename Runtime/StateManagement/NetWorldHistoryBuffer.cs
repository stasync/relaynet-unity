using System.Collections;
using System.Collections.Generic;
using LROTech.Network.StateManagement.Snapshots;

namespace LROTech.Network.StateManagement
{
    public sealed class NetWorldHistoryBuffer : IReadOnlyCollection<NetWorldSnapshot>
    {
        public int Size { get; private set; }
        public int AverageFrameSize { get; private set; }
        public float SizeMb => Size * 0.000001f;
        public int Count => _historyBuffer.Count;
        public float LengthInSeconds { get; private set; }

        private readonly Queue<NetWorldSnapshot> _historyBuffer = new();

        internal void Enqueue(NetWorldSnapshot snapshot)
        {
            _historyBuffer.Enqueue(snapshot);

            Size += snapshot.GetSize();
            LengthInSeconds += snapshot.DeltaTime;

            RecalculateAverageSize();
        }

        internal void Dequeue()
        {
            var snapshot = _historyBuffer.Dequeue();

            Size -= snapshot.GetSize();
            LengthInSeconds -= snapshot.DeltaTime;

            RecalculateAverageSize();
        }

        internal void Clear()
        {
            _historyBuffer.Clear();

            Size = 0;
            LengthInSeconds = 0f;

            RecalculateAverageSize();
        }

        private void RecalculateAverageSize() =>
            AverageFrameSize = Count == 0 ? 0 : Size / Count;

        public IEnumerator<NetWorldSnapshot> GetEnumerator() =>
            _historyBuffer.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() =>
            _historyBuffer.GetEnumerator();
    }
}