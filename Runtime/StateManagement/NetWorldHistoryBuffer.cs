using System.Collections;
using System.Collections.Generic;

public interface IReadOnlyHistoryBuffer : IReadOnlyCollection<NetWorldSnapshot>
{
    int Size { get; }
    int AveregeFrameSize { get; }
    float SizeMB { get; }
}

public class HistoryBiffer : IReadOnlyHistoryBuffer
{
    public int Size { get; private set; } 
    public int AveregeFrameSize { get; private set; }
    public float SizeMB => Size * 0.000001f;

    Queue<NetWorldSnapshot> m_HistoryBuffer;

    public HistoryBiffer()
    {
        m_HistoryBuffer = new Queue<NetWorldSnapshot>();
    }

    public void Enqueue(NetWorldSnapshot snapshot)
    {
        m_HistoryBuffer.Enqueue(snapshot);

        Size += snapshot.GetSize();
        RecalculateAveregeSize();
    }

    public void Dequeue()
    {
        var snapshot = m_HistoryBuffer.Dequeue();

        Size -= snapshot.GetSize();
        RecalculateAveregeSize();
    }

    public int Count => m_HistoryBuffer.Count;

    public void Clear()
    {
        m_HistoryBuffer.Clear();

        Size = 0;
        RecalculateAveregeSize();
    }

    void RecalculateAveregeSize() => AveregeFrameSize = Count == 0 ? 0 : Size / Count;

    public IEnumerator<NetWorldSnapshot> GetEnumerator() => m_HistoryBuffer.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => m_HistoryBuffer.GetEnumerator();
}

