using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LROTech.Network.GameObjects;
using LROTech.Network.StateManagement.Snapshots;
using UnityEngine;

namespace LROTech.Network.StateManagement
{
    public abstract class NetWorld
    {
        public static IReadOnlyDictionary<NetWorldId, NetWorld> All => s_all;
        private static readonly Dictionary<NetWorldId, NetWorld> s_all = new();

        public event Action Disposed = delegate { };
        public event Action<float> Updated = delegate { };
        public event Action<NetObjectUid> ObjectCreated = delegate { };

        public bool IsValid => Id.IsValid;

        public NetWorldId Uid { get; private set; }
        public NetWorldId Id { get; private set; }
        public NetWorldId SourceId { get; private set; }
        public float Time { get; protected set; }
        public float LocalTime { get; private set; }
        public string Name { get; private set; }
        public NetWorldView View { get; private set; }

        public bool IsReplica => SourceId.IsValid;

        public IReadOnlyDictionary<NetObjectUid, NetObjectState> ObjectStates => _currentObjectStates;
        private readonly Dictionary<NetObjectUid, NetObjectState> _currentObjectStates = new();

        public readonly NetWorldPlayersCollection Players;
        public readonly NetworkProperties Properties;

        protected NetWorld(NetWorldId id, string worldName) : this(id, worldName, NetWorldId.Invalid)
        {
        }

        protected NetWorld(NetWorldId id, string worldName, NetWorldId sourceId)
        {
            Uid = NetWorldId.Allocate();
            Id = id;
            SourceId = sourceId;

            Time = 0f;
            LocalTime = 0f;
            Name = $"{Id}:{worldName}";

            Players = new NetWorldPlayersCollection(validator: () => IsValid, GetSessionOwnerUid);
            Properties = new NetworkProperties(validator: () => IsValid, writePermission: () => Players.LocalPlayer != null && Players.LocalPlayer.IsMaster());

            s_all.Add(Uid, this);

#if UNITY_EDITOR
            // Automatically dispose the world upon exiting playmode.
            UnityEditor.EditorApplication.playModeStateChanged += OnPlaymodeChange_Editor;
#endif
        }

        protected virtual ClientUid GetSessionOwnerUid() => 
            ClientUid.Invalid;

        internal void InitializeView(NetWorldViewInitMode viewInitMode = NetWorldViewInitMode.Default)
        {
            if (View != null)
                throw new InvalidOperationException($"View for world '{Name}' with uid '{Uid}' already initialized.");

            View = new GameObject(name: $"[{Uid}] {Name} View").AddComponent<NetWorldView>();
            View.Init(world: this, viewInitMode);
        }

        public void Update(float deltaTime)
        {
            EnsureValid();
            UpdateImplementation(deltaTime);
        }

        protected abstract void UpdateImplementation(float deltaTime);

        protected bool TryAddObject(NetObjectState state)
        {
            if (!state.Uid.IsValid)
                throw new InvalidOperationException();

            var canAddObject = !ObjectExists(state.Uid);
            if (canAddObject)
            {
                _currentObjectStates.Add(state.Uid, state);
                ObjectCreated(state.Uid);
            }

            return canAddObject;
        }

        protected void UpdateWorldState(float worldDelta)
        {
            OnBeforeUpdateWorldState(worldDelta);
            OnUpdateWorldState(worldDelta);

            if (View != null)
            {
                OnBeforeExecuteView(worldDelta);
                View.RebuildAccordingState();
            }

            Updated(worldDelta);
        }

        protected virtual void OnBeforeUpdateWorldState(float worldDelta)
        {
            foreach (var state in _currentObjectStates.Values)
                state.OnBeforeUpdateWorldState(worldDelta);

            Players.OnBeforeUpdateWorldState(worldDelta);
        }

        protected virtual void OnUpdateWorldState(float worldDelta)
        {
            Time += worldDelta;
            LocalTime += worldDelta;
        }

        protected virtual void OnBeforeExecuteView(float worldDelta)
        {
        }

        public virtual void Dispose()
        {
            EnsureValid();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlaymodeChange_Editor;
#endif

            if (View != null)
            {
                // Destroy all net object views.
                View.Destroy();
                View = null;
            }
#if UNITY_EDITOR
            else
                Debug.LogError($"View already destroyed for world with uid '{Uid}' (Maybe it's done by unity via exiting play mode).");
#endif
            s_all.Remove(Uid);

            Uid = NetWorldId.Invalid;
            Id = NetWorldId.Invalid;
            SourceId = NetWorldId.Invalid;

            Time = 0f;
            LocalTime = 0f;

            Updated = null;

            foreach (var netObject in _currentObjectStates.Values)
                netObject.Invalidate();
            _currentObjectStates.Clear();

            Players.Clear();
            Properties.Clean();

            Disposed?.Invoke();
            Disposed = null;
        }

#if UNITY_EDITOR
        private void OnPlaymodeChange_Editor(UnityEditor.PlayModeStateChange playModeState)
        {
            if (playModeState == UnityEditor.PlayModeStateChange.EnteredEditMode && IsValid)
                Dispose();
        }
#endif

        public bool ObjectExists(NetObjectUid uid)
        {
            EnsureValid();
            return _currentObjectStates.ContainsKey(uid);
        }

        public NetObjectIdentity FindObjectView(NetObjectUid instanceId)
        {
            EnsureValid();
            return View == null ? null : View.FindObjectView(instanceId);
        }

        public T FindObjectView<T>(NetObjectUid instanceId) where T : NetObjectBehaviourBase
        {
            EnsureValid();
            return View == null ? null : View.FindObjectView<T>(instanceId);
        }

        public T FindObjectViewOfType<T>() where T : NetObjectBehaviourBase
        {
            EnsureValid();

            if (View == null)
                return null;

            foreach (var view in View.CurrentObjectViews.Values)
            {
                var component = view.GetBehaviour<T>();
                if (component != null)
                    return component;
            }

            return null;
        }

        public virtual NetObjectState FindObject(NetObjectUid instanceId)
        {
            EnsureValid();
            return _currentObjectStates.GetValueOrDefault(instanceId);
        }

        public bool TryGetObject(NetObjectUid instanceId, out NetObjectState state)
        {
            EnsureValid();
            return _currentObjectStates.TryGetValue(instanceId, out state);
        }

        protected virtual bool IsObjectValid(NetObjectUid instanceId) =>
            _currentObjectStates.ContainsKey(instanceId);

        protected virtual void RemoveObject(NetObjectUid stateUid)
        {
            EnsureValid();

            if (!_currentObjectStates.TryGetValue(stateUid, out var state))
                return;

            state.Invalidate();
            _currentObjectStates.Remove(stateUid);
        }

        public NetWorldSnapshot ToSnapshot(float deltaTime = 0f)
        {
            EnsureValid();
            return new NetWorldSnapshot(deltaTime, world: this);
        }

        protected void RestoreFromSnapshot(NetWorldSnapshot snapshot)
        {
            // Update time.
            Time = snapshot.Time;
            LocalTime = snapshot.LocalTime;

            // Restore players.
            Players.RestoreFromSnapshot(snapshot.Players);

            // Restore properties.
            Properties.RestoreFromSnapshot(snapshot.Properties);

            // Add or update objects.
            foreach (var (uid, objectStateSnapshot) in snapshot.Objects)
            {
                if (_currentObjectStates.TryGetValue(uid, out var state))
                    state.ChangeObjectRemotely(objectStateSnapshot);
                else
                    TryAddObject(new NetObjectState(IsObjectValid, Players.Get, objectStateSnapshot));
            }

            // Remove absent objects.
            foreach (var objectUid in new List<NetObjectUid>(_currentObjectStates.Keys))
            {
                if (!snapshot.Objects.ContainsKey(objectUid))
                    RemoveObject(objectUid);
            }
        }

        public int GetVersionHash()
        {
            EnsureValid();

            var hash = 13;
            hash = hash * 7 + Id.GetHashCode();
            hash = hash * 7 + Time.GetHashCode();
            hash = hash * 7 + LocalTime.GetHashCode(); //Do we need local time in hash code?
            foreach (var kvp in _currentObjectStates)
            {
                hash += kvp.Key.GetHashCode();
                hash += kvp.Value.GetVersionHash();
            }

            hash = hash * 7 + Players.GetVersionHash();
            hash = hash * 7 + Properties.GetVersionHash();

            return hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void EnsureValid()
        {
            if (!IsValid)
                throw new InvalidOperationException($"Object of type '{GetType().FullName}' is invalid.");
        }
    }
}