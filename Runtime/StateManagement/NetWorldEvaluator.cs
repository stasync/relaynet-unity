using System;
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
    public int BufferCount => m_HistoryBuffer.Count;

    public NetWorldEvaluator(NetWorldRuntime runtime, bool autoDestroy = true) : base(runtime.ID, $"World {runtime.ID} evaluator", runtime.ID)
    {
        InitializeView(NetWorldViewInitMode.SeparatePhysicsSimulation);

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

    public void MoveToPreviousSnapshot(bool updateImmidiate = false)
    {
        CurrentSnapshotIndex--;
        MoveToSnapshotSafe(CurrentSnapshotIndex, updateImmidiate);
    }

    public void MoveToNextSnapshot(bool updateImmidiate = false)
    {
        CurrentSnapshotIndex++;
        MoveToSnapshotSafe(CurrentSnapshotIndex, updateImmidiate);
    }

    public void MoveToSnapshotSafe(int snapshotIndex, bool updateImmidiate = false) =>
        MoveToSnapshot(UnityEngine.Mathf.Clamp(snapshotIndex, 0, m_HistoryBuffer.Count - 1), updateImmidiate);

    public void MoveToSnapshot(int snapshotIndex, bool updateImmidiate = false)
    {
        if (snapshotIndex < 0 || snapshotIndex >= m_HistoryBuffer.Count)
            throw new IndexOutOfRangeException();

        m_CurrentTime = GetSnapshotRelativeTime(snapshotIndex);

        CurrentSnapshotIndex = snapshotIndex;
        UpdateCurrentSnapshotInfo();

        if(updateImmidiate)
            UpdateWorldState(m_CurrentSnapshot.DeltaTime);
    }

    public void SetPaused(bool paused) => Paused = paused;

    public int FindLastSnapshotIndex(Predicate<NetWorldSnapshot> predicate)
    {
        for (var i = m_HistoryBuffer.Count - 1; i >= 0; i--)
            if (predicate(m_HistoryBuffer[i]))
                return i;
        return -1;
    }

    public int FindLastSnapshotIndexAndPeekNext(Predicate<NetWorldSnapshot> predicate)
    {
        var snapshotIndex = FindLastSnapshotIndex(predicate);
        snapshotIndex++;

        var lastPossibleIndex = m_HistoryBuffer.Count - 1;
        if (snapshotIndex > lastPossibleIndex)
            snapshotIndex = lastPossibleIndex;

        return snapshotIndex;
    }

    public int FindFirstSnapshotIndex(Predicate<NetWorldSnapshot> predicate)
    {
        for (var i = 0; i < m_HistoryBuffer.Count; i++)
            if (predicate(m_HistoryBuffer[i]))
                return i;
        return -1;
    }

    public float GetSnapshotRelativeTime(int snapshotIndex)
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

    public float GetRemainTimeFromShanpshot(int snapshotIndex)
    {
        if(m_HistoryBuffer.Count == 0)
        {
            UnityEngine.Debug.LogError($"[{Name}] [{UID}] history buffer is empty");
            return 0f;
        }

        if (snapshotIndex < 0 || snapshotIndex >= m_HistoryBuffer.Count)
            throw new IndexOutOfRangeException();

        var lastSnapshot = m_HistoryBuffer[m_HistoryBuffer.Count - 1];
        var targetSnapshot = m_HistoryBuffer[snapshotIndex];

        return lastSnapshot.LocalTime - targetSnapshot.LocalTime;
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

        UpdateWorldState(m_CurrentSnapshot.DeltaTime);

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