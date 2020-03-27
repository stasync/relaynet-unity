using System.Collections;
using System.Collections.Generic;

public class HistoryBiffer : IReadOnlyCollection<NetWorldSnapshot>
{
    Queue<NetWorldSnapshot> m_HistoryBuffer;

    public HistoryBiffer()
    {
        m_HistoryBuffer = new Queue<NetWorldSnapshot>();
    }

    public void Enqueue(NetWorldSnapshot snapshot) => m_HistoryBuffer.Enqueue(snapshot);

    public void Dequeue() => m_HistoryBuffer.Dequeue();

    public int Count => m_HistoryBuffer.Count;

    public void Clear() => m_HistoryBuffer.Clear();

    public IEnumerator<NetWorldSnapshot> GetEnumerator() => m_HistoryBuffer.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => m_HistoryBuffer.GetEnumerator();
}

