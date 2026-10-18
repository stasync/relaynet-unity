using System;
using System.Collections.Generic;
using Keel.RelayNet.StateManagement.Snapshots;

namespace Keel.RelayNet.StateManagement
{
    public sealed class NetWorldReplica : NetWorld
    {
        public int CurrentSnapshotIndex { get; private set; }
        public bool Paused { get; private set; }
        public int BufferCount => _historyBuffer.Count;

        private readonly List<NetWorldSnapshot> _historyBuffer;

        private NetWorldSnapshot _currentSnapshot;
        private float _currentTime;
        private float _nextSnapshotTime;
        private readonly bool _autoDestroy;

        public NetWorldReplica(NetWorldRuntime runtime, bool autoDestroy = true) : base(id: runtime.Id, worldName: $"World {runtime.Id} replica", runtime.Id)
        {
            InitializeView(NetWorldViewInitMode.SeparatePhysicsSimulation);

            _historyBuffer = new List<NetWorldSnapshot>(runtime.HistoryBuffer);
            _currentTime = 0f;
            CurrentSnapshotIndex = 0;
            _autoDestroy = autoDestroy;

            Paused = false;

            UpdateCurrentSnapshotInfo();
        }

        private void PeekNextSnapshot()
        {
            CurrentSnapshotIndex++;
            if (CurrentSnapshotIndex >= _historyBuffer.Count)
                CurrentSnapshotIndex = _historyBuffer.Count - 1;

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
            MoveToSnapshot(UnityEngine.Mathf.Clamp(snapshotIndex, min: 0, max: _historyBuffer.Count - 1), updateImmediate);

        public void MoveToSnapshot(int snapshotIndex, bool updateImmediate = false)
        {
            if (snapshotIndex < 0 || snapshotIndex >= _historyBuffer.Count)
                throw new IndexOutOfRangeException();

            _currentTime = GetSnapshotRelativeTime(snapshotIndex);

            CurrentSnapshotIndex = snapshotIndex;
            UpdateCurrentSnapshotInfo();

            if (updateImmediate)
                UpdateWorldState(_currentSnapshot.DeltaTime);
        }

        public void SetPaused(bool paused) =>
            Paused = paused;

        public int FindLastSnapshotIndex(Predicate<NetWorldSnapshot> predicate)
        {
            for (var i = _historyBuffer.Count - 1; i >= 0; i--)
            {
                if (predicate(_historyBuffer[i]))
                    return i;
            }

            return -1;
        }

        public int FindLastSnapshotIndexAndPeekNext(Predicate<NetWorldSnapshot> predicate)
        {
            var snapshotIndex = FindLastSnapshotIndex(predicate);
            snapshotIndex++;

            var lastPossibleIndex = _historyBuffer.Count - 1;
            if (snapshotIndex > lastPossibleIndex)
                snapshotIndex = lastPossibleIndex;

            return snapshotIndex;
        }

        public int FindFirstSnapshotIndex(Predicate<NetWorldSnapshot> predicate)
        {
            for (var i = 0; i < _historyBuffer.Count; i++)
            {
                if (predicate(_historyBuffer[i]))
                    return i;
            }

            return -1;
        }

        public float GetSnapshotRelativeTime(int snapshotIndex)
        {
            if (_historyBuffer.Count == 0)
            {
                UnityEngine.Debug.LogError($"[{Name}] [{Uid}] history buffer is empty.");
                return 0f;
            }

            if (snapshotIndex < 0 || snapshotIndex >= _historyBuffer.Count)
                throw new IndexOutOfRangeException();

            return _historyBuffer[snapshotIndex].LocalTime - _historyBuffer[0].LocalTime;
        }

        public float GetRemainTimeFromSnapshot(int snapshotIndex)
        {
            if (_historyBuffer.Count == 0)
            {
                UnityEngine.Debug.LogError($"[{Name}] [{Uid}] history buffer is empty.");
                return 0f;
            }

            if (snapshotIndex < 0 || snapshotIndex >= _historyBuffer.Count)
                throw new IndexOutOfRangeException();

            return _historyBuffer[^1].LocalTime - _historyBuffer[snapshotIndex].LocalTime;
        }

        private void UpdateCurrentSnapshotInfo()
        {
            if (CurrentSnapshotIndex >= _historyBuffer.Count || CurrentSnapshotIndex < 0)
                return;

            _currentSnapshot = _historyBuffer[CurrentSnapshotIndex];
            _nextSnapshotTime = _currentTime + _currentSnapshot.DeltaTime;
        }

        protected override void UpdateImplementation(float deltaTime)
        {
            if (Paused)
                return;

            _currentTime += deltaTime;
            if (_currentTime < _nextSnapshotTime)
                return;

            UpdateWorldState(_currentSnapshot.DeltaTime);

            //Do not pick next snapshot if this is a last snapshot;
            if (CurrentSnapshotIndex == _historyBuffer.Count - 1)
            {
                if (_autoDestroy)
                    Dispose();
            }
            else
                PeekNextSnapshot();
        }

        protected override void OnUpdateWorldState(float worldDelta)
        {
            base.OnUpdateWorldState(worldDelta);
            RestoreFromSnapshot(_currentSnapshot);
        }
    }
}