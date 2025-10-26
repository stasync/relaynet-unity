using System;
using System.Collections.Generic;
using LROTech.Network.StateManagement.Snapshots;
using UnityEngine;

namespace LROTech.Network.StateManagement
{
    public abstract class NetWorld
    {
        public event Action Updated = delegate { };
        public event Action<NetObjectUid> ObjectCreated = delegate { };

        public bool IsValid => ID.IsValid;

        public NetWorldId UID { get; private set; }
        public NetWorldId ID { get; private set; }
        public NetWorldId SourceID { get; private set; }
        public float Time { get; protected set; }
        public float LocalTime { get; private set; }
        public string Name { get; private set; }
        public NetWorldView View { get; private set; }

        public bool IsReplica => SourceID.IsValid;

        public IReadOnlyDictionary<NetObjectUid, NetObjectState> ObjectStates => m_CurrentObjectStates;
        private readonly Dictionary<NetObjectUid, NetObjectState> m_CurrentObjectStates;

        public readonly NetWorldPlayersCollection Players;
        public readonly NetworkProperties Properties;

        private readonly Queue<Action> m_ViewInitActionQueue = new();

        protected NetWorld(NetWorldId uid, string worldName) : this(uid, worldName, NetWorldId.Invalid)
        {
        }

        protected NetWorld(NetWorldId uid, string worldName, NetWorldId sourceId)
        {
            UID = NetWorldId.Allocate();
            ID = uid;
            SourceID = sourceId;

            Time = 0f;
            LocalTime = 0f;
            Name = $"{ID}:{worldName}";

            m_CurrentObjectStates = new Dictionary<NetObjectUid, NetObjectState>();
            Players = new NetWorldPlayersCollection(validator: () => IsValid);
            Properties = new NetworkProperties(writePermission: () => Players.LocalPlayerIsMaster);
        }

        internal void InitializeView(NetWorldViewInitMode viewInitMode = NetWorldViewInitMode.Default)
        {
            if (View != null)
                throw new InvalidOperationException($"View for world {Name} already initialized");

            View = new GameObject(Name).AddComponent<NetWorldView>();
            View.Init(world: this, viewInitMode);

            while (m_ViewInitActionQueue.Count > 0)
                m_ViewInitActionQueue.Dequeue()();
        }

        public void Update(float deltaTime)
        {
            if (IsValid)
                UpdateImplementation(deltaTime);
        }

        protected abstract void UpdateImplementation(float deltaTime);

        protected bool TryAddObject(NetObjectState state)
        {
            if (!state.IsValid)
                throw new InvalidOperationException();

            var canAddObject = !ObjectExists(state.Uid);
            if (canAddObject)
            {
                m_CurrentObjectStates.Add(state.Uid, state);
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

            Updated();
        }

        protected virtual void OnBeforeUpdateWorldState(float worldDelta)
        {
            foreach (var state in m_CurrentObjectStates.Values)
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
            UID = NetWorldId.Invalid;
            ID = NetWorldId.Invalid;
            SourceID = NetWorldId.Invalid;

            Updated = delegate { };

            foreach (var netObject in m_CurrentObjectStates.Values)
                netObject.Invalidate();

            m_CurrentObjectStates.Clear();
            Players.Clear();
            Properties.Clean();
            m_ViewInitActionQueue.Clear();

            if (View == null)
                Debug.LogError("View already destroyed (Maybe it's done by unity via exiting play mode)");
            else
            {
                // Destroy all net object views.
                View.Destroy();
                View = null;
            }
        }

        public virtual bool ObjectExists(NetObjectUid uid) =>
            m_CurrentObjectStates.ContainsKey(uid);

        public NetObjectIdentity FindObjectView(NetObjectUid instanceId) =>
            View == null ? null : View.FindObjectView(instanceId);

        public T FindObjectView<T>(NetObjectUid instanceId) where T : GSInstanceBehaviourBase =>
            View == null ? null : View.FindObjectView<T>(instanceId);

        public IEnumerable<T> FindObjectViewsOfType<T>() where T : GSInstanceBehaviourBase =>
            View == null ? null : View.FindObjectViewsOfType<T>();

        public T FindObjectViewOfType<T>() where T : GSInstanceBehaviourBase
        {
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

        public virtual NetObjectState FindObject(NetObjectUid instanceId) =>
            m_CurrentObjectStates.GetValueOrDefault(instanceId);

        public bool TryGetObject(NetObjectUid instanceId, out NetObjectState state) =>
            m_CurrentObjectStates.TryGetValue(instanceId, out state);

        protected virtual void RemoveObject(NetObjectUid stateUID)
        {
            if (!m_CurrentObjectStates.TryGetValue(stateUID, out var state))
                return;

            state.Invalidate();
            m_CurrentObjectStates.Remove(stateUID);
        }

        public NetWorldSnapshot ToSnapshot(float deltaTime = 0f) =>
            new(deltaTime, world: this);

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
            foreach (var kvp in snapshot.Objects)
            {
                if (m_CurrentObjectStates.TryGetValue(kvp.Key, out var state))
                    state.ChangeObjectRemotely(kvp.Value);
                else
                    TryAddObject(new NetObjectState(kvp.Value, Players.Find));
            }

            // Remove absent objects.
            foreach (var objectUid in new List<NetObjectUid>(m_CurrentObjectStates.Keys))
            {
                if (!snapshot.Objects.ContainsKey(objectUid))
                    RemoveObject(objectUid);
            }
        }

        protected void ScheduleOnViewInit(Action action)
        {
            if (View != null)
                action();
            else
                m_ViewInitActionQueue.Enqueue(action);
        }

        public int GetVersionHash()
        {
            var hash = 13;
            hash = hash * 7 + ID.GetHashCode();
            hash = hash * 7 + Time.GetHashCode();
            hash = hash * 7 + LocalTime.GetHashCode(); //Do we need local time in hash code?
            foreach (var kvp in m_CurrentObjectStates)
            {
                hash += kvp.Key.GetHashCode();
                hash += kvp.Value.GetVersionHash();
            }

            hash = hash * 7 + Players.GetVersionHash();
            hash = hash * 7 + Properties.GetVersionHash();

            return hash;
        }
    }
}