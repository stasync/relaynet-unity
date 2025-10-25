using System;
using System.Collections.Generic;

namespace LROTech.Network.StateManagment
{
    public sealed class NetWorldEvaluator : NetWorld
    {
        public int CurrentSnapshotIndex { get; private set; }
        public bool Paused { get; private set; }
        public int BufferCount => m_HistoryBuffer.Count;

        private readonly List<NetWorldSnapshot> m_HistoryBuffer;

        private NetWorldSnapshot m_CurrentSnapshot;
        private float m_CurrentTime;
        private float m_NextSnapshotTime;
        private readonly bool m_AutoDestroy;

        public NetWorldEvaluator(NetWorldRuntime runtime, bool autoDestroy = true) : base(uid: runtime.ID, worldName: $"World {runtime.ID} evaluator",
            runtime.ID)
        {
            InitializeView(NetWorldViewInitMode.SeparatePhysicsSimulation);

            m_HistoryBuffer = new List<NetWorldSnapshot>(runtime.HistoryBuffer);
            m_CurrentTime = 0f;
            CurrentSnapshotIndex = 0;
            m_AutoDestroy = autoDestroy;

            Paused = false;

            UpdateCurrentSnapshotInfo();
        }

        private void PeekNextSnapshot()
        {
            CurrentSnapshotIndex++;
            if (CurrentSnapshotIndex >= m_HistoryBuffer.Count)
                CurrentSnapshotIndex = m_HistoryBuffer.Count - 1;

            UpdateCurrentSnapshotInfo();
        }

        public void MoveToPreviousSnapshot(bool updateImmediate = false)
        {
            CurrentSnapshotIndex--;
            MoveToSnapshotSafe(CurrentSnapshotIndex, updateImmediate);
        }

        public void MoveToNextSnapshot(bool updateImmediate = false)
        {
            CurrentSnapshotIndex++;
            MoveToSnapshotSafe(CurrentSnapshotIndex, updateImmediate);
        }

        public void MoveToSnapshotSafe(int snapshotIndex, bool updateImmediate = false) =>
            MoveToSnapshot(UnityEngine.Mathf.Clamp(snapshotIndex, min: 0, max: m_HistoryBuffer.Count - 1), updateImmediate);

        public void MoveToSnapshot(int snapshotIndex, bool updateImmediate = false)
        {
            if (snapshotIndex < 0 || snapshotIndex >= m_HistoryBuffer.Count)
                throw new IndexOutOfRangeException();

            m_CurrentTime = GetSnapshotRelativeTime(snapshotIndex);

            CurrentSnapshotIndex = snapshotIndex;
            UpdateCurrentSnapshotInfo();

            if (updateImmediate)
                UpdateWorldState(m_CurrentSnapshot.DeltaTime);
        }

        public void SetPaused(bool paused) =>
            Paused = paused;

        public int FindLastSnapshotIndex(Predicate<NetWorldSnapshot> predicate)
        {
            for (var i = m_HistoryBuffer.Count - 1; i >= 0; i--)
            {
                if (predicate(m_HistoryBuffer[i]))
                    return i;
            }

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
            {
                if (predicate(m_HistoryBuffer[i]))
                    return i;
            }

            return -1;
        }

        public float GetSnapshotRelativeTime(int snapshotIndex)
        {
            if (m_HistoryBuffer.Count == 0)
            {
                UnityEngine.Debug.LogError($"[{Name}] [{UID}] history buffer is empty");
                return 0f;
            }

            if (snapshotIndex < 0 || snapshotIndex >= m_HistoryBuffer.Count)
                throw new IndexOutOfRangeException();

            return m_HistoryBuffer[snapshotIndex].LocalTime - m_HistoryBuffer[0].LocalTime;
        }

        public float GetRemainTimeFromSnapshot(int snapshotIndex)
        {
            if (m_HistoryBuffer.Count == 0)
            {
                UnityEngine.Debug.LogError($"[{Name}] [{UID}] history buffer is empty");
                return 0f;
            }

            if (snapshotIndex < 0 || snapshotIndex >= m_HistoryBuffer.Count)
                throw new IndexOutOfRangeException();

            return m_HistoryBuffer[^1].LocalTime - m_HistoryBuffer[snapshotIndex].LocalTime;
        }

        private void UpdateCurrentSnapshotInfo()
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
            if (CurrentSnapshotIndex == m_HistoryBuffer.Count - 1)
            {
                if (m_AutoDestroy)
                    Dispose();
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
}