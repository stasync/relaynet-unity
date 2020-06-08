using System.Collections.Generic;

public class NetWorldEvaluator : NetWorld
{
    List<NetWorldSnapshot> m_HistoryBuffer;
    NetWorldSnapshot m_CurrentSnapshot;

    float m_CurrentTime;
    float m_NextSnapshotTime;
    bool m_AutoDestroy;

    public int CurrentSnapshotIndex { get; private set; }
    public bool Paused { get; private set; }
    public int BufferSize => m_HistoryBuffer.Count;

    public NetWorldEvaluator(NetWorldRuntime runtime, bool autoDestroy = true) : base(runtime.UID, $"World {runtime.UID} evaluator")
    {
        InitializeView(NetWorldViewInitMode.SeparateScene);

        m_HistoryBuffer = new List<NetWorldSnapshot>(runtime.HistroyBuffer);
        m_CurrentTime = 0f;
        CurrentSnapshotIndex = 0;
        m_AutoDestroy = autoDestroy;

        Paused = false;

        UpdateCurrentSnapshotInfo();
    }

    void PeekNextSnapshot()
    {
        CurrentSnapshotIndex++;
        if(CurrentSnapshotIndex >= m_HistoryBuffer.Count)
            CurrentSnapshotIndex = m_HistoryBuffer.Count - 1;

        UpdateCurrentSnapshotInfo();
    }

    public void MoveToPreviousSnapshot()
    {
        CurrentSnapshotIndex--;
        MoveToSnapshot(CurrentSnapshotIndex);
    }

    public void MoveToNextSnapshot()
    {
        CurrentSnapshotIndex++;
        MoveToSnapshot(CurrentSnapshotIndex);
    }

    public void MoveToSnapshot(int snapshotIndex)
    {
        CurrentSnapshotIndex = UnityEngine.Mathf.Clamp(snapshotIndex, 0, m_HistoryBuffer.Count - 1);

        UpdateCurrentSnapshotInfo();
        GetSnapshotTime(CurrentSnapshotIndex);

        Evaluate(m_CurrentSnapshot.DeltaTime);
    }

    public void SetPaused(bool paused) => Paused = paused;

    float GetSnapshotTime(int snapshotIndex)
    {
        var result = 0f;
        for(var i = 0; i < m_HistoryBuffer.Count; i++)
        {
            var s = m_HistoryBuffer[i];
            result += s.DeltaTime;
            if (i.Equals(snapshotIndex))
                return result;
        }

        UnityEngine.Debug.LogError($"Unabled to calculate time for snapshot with index {snapshotIndex}");
        return result;
    }

    void UpdateCurrentSnapshotInfo()
    {
        if (CurrentSnapshotIndex >= m_HistoryBuffer.Count || CurrentSnapshotIndex < 0)
            return;

        m_CurrentSnapshot = m_HistoryBuffer[CurrentSnapshotIndex];
        m_NextSnapshotTime = m_CurrentTime + m_CurrentSnapshot.DeltaTime;
    }

    protected override void UpdateImplementation(float deltaTime)
    {
        if (Paused)
            return;

        m_CurrentTime += deltaTime;
        if (m_CurrentTime < m_NextSnapshotTime)
            return;

        Evaluate(m_CurrentSnapshot.DeltaTime);

        //Do not pick next snapshot if this is a last snapshot;
        if (CurrentSnapshotIndex.Equals(m_HistoryBuffer.Count - 1))
        {
            if (m_AutoDestroy)
                Destroy();
        }
        else
            PeekNextSnapshot();
    }

    protected override void OnUpdateWorldState(float worldDelta)
    {
        base.OnUpdateWorldState(worldDelta);
        RestoreFromSnapshot(m_CurrentSnapshot);
    }
}