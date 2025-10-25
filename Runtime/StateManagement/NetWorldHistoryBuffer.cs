using System.Collections;
using System.Collections.Generic;

public interface IReadOnlyHistoryBuffer : IReadOnlyCollection<NetWorldSnapshot>
{
    int Size { get; }
    int AverageFrameSize { get; }
    float SizeMB { get; }
    float LengthInSeconds { get; }
}

public sealed class HistoryBuffer : IReadOnlyHistoryBuffer
{
    public int Size { get; private set; }
    public int AverageFrameSize { get; private set; }
    public float SizeMB => Size * 0.000001f;
    public int Count => m_HistoryBuffer.Count;
    public float LengthInSeconds { get; private set; }

    private readonly Queue<NetWorldSnapshot> m_HistoryBuffer = new();

    public void Enqueue(NetWorldSnapshot snapshot)
    {
        m_HistoryBuffer.Enqueue(snapshot);

        Size += snapshot.GetSize();
        LengthInSeconds += snapshot.DeltaTime;

        RecalculateAverageSize();
    }

    public void Dequeue()
    {
        var snapshot = m_HistoryBuffer.Dequeue();

        Size -= snapshot.GetSize();
        LengthInSeconds -= snapshot.DeltaTime;

        RecalculateAverageSize();
    }

    public void Clear()
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