using System.Collections.Generic;

public class NetWorldEvaluator : NetWorld
{
    Queue<NetWorldSnapshot> m_HistoryBuffer;
    NetWorldSnapshot m_CurrentSnapshot;
    float m_CurrentTime;
    float m_NextSnapshotTime;
    bool m_Finished;
    bool m_AutoDestroy;

    public NetWorldEvaluator(NetWorldRuntime runtime, bool autoDestroy = true) : base(runtime.UID, $"World {runtime.UID} evaluator", NetWorldViewInitMode.SeparateScene)
    {
        m_HistoryBuffer = new Queue<NetWorldSnapshot>(runtime.HistroyBuffer);
        m_CurrentTime = 0f;
        m_AutoDestroy = autoDestroy;

        UpdateCurrentSnapshotInfo();
    }

    void NextSnapshot()
    {
        if (m_HistoryBuffer.Count > 0)
            UpdateCurrentSnapshotInfo();
        else 
            m_Finished = true;
    }

    void UpdateCurrentSnapshotInfo()
    {
        m_CurrentSnapshot = m_HistoryBuffer.Dequeue();
        m_NextSnapshotTime = m_CurrentTime + m_CurrentSnapshot.DeltaTime;
    }

    protected override void UpdateImplementation()
    {
        if (m_Finished)
        {
            if (m_AutoDestroy)
                Destroy();

            return;
        }

        m_CurrentTime += UnityEngine.Time.fixedUnscaledDeltaTime;
        if (m_CurrentTime < m_NextSnapshotTime)
            return;

        Evaluate(m_CurrentSnapshot.DeltaTime);
        NextSnapshot();
    }

    protected override void OnUpdateWorldState(float worldDelta)
    {
        base.OnUpdateWorldState(worldDelta);
        RestoreFromSnapshot(m_CurrentSnapshot);
    }
}