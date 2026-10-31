using System;
using System.Collections.Generic;
using Keel.Networking;
using Keel.RelayNet.GameObjects;
using Keel.RelayNet.StateManagement;
using UnityEngine;

namespace Keel.RelayNet.Behaviours
{
    public interface IAnimatorValueProxy
    {
        void OnUpdate(float deltaTime, bool hasAuthority);

        void Serialize(NetWriter writer);
        void Deserialize(NetReader reader);
    }

    public abstract class AnimatorParameterValueProxy<T> : IAnimatorValueProxy where T : struct
    {
        protected readonly Animator Animator;
        protected readonly AnimatorControllerParameter Parameter;
        protected T LastDeserializedValue { get; private set; }

        protected AnimatorParameterValueProxy(Animator animator, AnimatorControllerParameter parameter)
        {
            Animator = animator;
            Parameter = parameter;
        }

        public virtual void OnUpdate(float deltaTime, bool hasAuthority)
        {
        }

        public abstract void Serialize(NetWriter writer);

        public virtual void Deserialize(NetReader reader) =>
            LastDeserializedValue = DeserializeValue(reader);

        protected abstract T DeserializeValue(NetReader reader);
    }

    public class AnimatorFloatParameterValueProxy : AnimatorParameterValueProxy<float>
    {
        public AnimatorFloatParameterValueProxy(Animator animator, AnimatorControllerParameter parameter) : base(animator, parameter)
        {
        }

        public override void OnUpdate(float deltaTime, bool hasAuthority)
        {
            base.OnUpdate(deltaTime, hasAuthority);

            if (!hasAuthority)
                Animator.SetFloat(Parameter.nameHash, LastDeserializedValue, 0.1f, deltaTime);
        }

        public override void Serialize(NetWriter writer) =>
            writer.WriteSingle(Animator.GetFloat(Parameter.nameHash));

        protected override float DeserializeValue(NetReader reader) =>
            reader.ReadSingle();
    }

    public class AnimatorIntParameterValueProxy : AnimatorParameterValueProxy<int>
    {
        public AnimatorIntParameterValueProxy(Animator animator, AnimatorControllerParameter parameter) : base(animator, parameter)
        {
        }

        public override void Serialize(NetWriter writer) =>
            writer.WriteInt32(Animator.GetInteger(Parameter.nameHash));

        protected override int DeserializeValue(NetReader reader) =>
            reader.ReadInt32();

        public override void Deserialize(NetReader reader)
        {
            base.Deserialize(reader);
            Animator.SetInteger(Parameter.nameHash, LastDeserializedValue);
        }
    }

    public class AnimatorBoolParameterValueProxy : AnimatorParameterValueProxy<bool>
    {
        public AnimatorBoolParameterValueProxy(Animator animator, AnimatorControllerParameter parameter) : base(animator, parameter)
        {
        }

        public override void Serialize(NetWriter writer) =>
            writer.WriteBoolean(Animator.GetBool(Parameter.nameHash));

        protected override bool DeserializeValue(NetReader reader) =>
            reader.ReadBoolean();

        public override void Deserialize(NetReader reader)
        {
            base.Deserialize(reader);
            Animator.SetBool(Parameter.nameHash, LastDeserializedValue);
        }
    }

    [DisallowMultipleComponent]
    public sealed class NetObjectAnimatorSync : NetObjectBehaviour<NetObjectAnimatorSync.StateData>
    {
        public sealed class StateData : NetObjectStateData
        {
        }

        private enum AnimatorStateChangeMethod : byte
        {
            Play,
            CrossFade,
        }

        private enum AnimatorTriggerAction : byte
        {
            Set,
            Reset,
        }

        [SerializeField] private Animator _animator;

        [SerializeField] private AnimatorStateChangeMethod _stateChangeMethod = AnimatorStateChangeMethod.CrossFade;

        [SerializeField, Range(0f, 1f)] private float _crossFadeDuration = 0.1f;

        [SerializeField, Range(0f, 10f)]
        private float _remoteLayerWeightChangeSpeed = 5f;

        private readonly List<IAnimatorValueProxy> _animatorParameterValueProxyCollection = new();
        private readonly List<float> _remoteLayerWeights = new();
        private bool[] _layersSyncFlags;

        private readonly NetWriter _writer = new();
        private readonly NetReader _reader = new();

        private void Awake()
        {
            foreach (var parameter in _animator.parameters)
            {
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Bool:
                        _animatorParameterValueProxyCollection.Add(new AnimatorBoolParameterValueProxy(_animator, parameter));
                        break;

                    case AnimatorControllerParameterType.Float:
                        _animatorParameterValueProxyCollection.Add(new AnimatorFloatParameterValueProxy(_animator, parameter));
                        break;

                    case AnimatorControllerParameterType.Int:
                        _animatorParameterValueProxyCollection.Add(new AnimatorIntParameterValueProxy(_animator, parameter));
                        break;
                }
            }

            for (var layerIndex = 0; layerIndex < _animator.layerCount; layerIndex++)
                _remoteLayerWeights.Add(_animator.GetLayerWeight(layerIndex));

            /*var runtimeController = (UnityEditor.Animations.AnimatorController)_animator.runtimeAnimatorController;

            _layersSyncFlags = new bool[runtimeController.layers.Length];
            for (var index = 0; index < runtimeController.layers.Length; index++)
                _layersSyncFlags[index] = (uint)runtimeController.layers[index].syncedLayerIndex < (uint)runtimeController.layers.Length;*/
        }

        public void SetTrigger(string triggerName) =>
            SetTrigger(Animator.StringToHash(triggerName));

        public void SetTrigger(int triggerNameHash)
        {
            if (!IsLocal)
                throw new InvalidOperationException("No authority");

            AnimatorTrigger(triggerNameHash, AnimatorTriggerAction.Set);
        }

        public void ResetTrigger(string triggerName) =>
            ResetTrigger(Animator.StringToHash(triggerName));

        public void ResetTrigger(int triggerNameHash)
        {
            if (!IsLocal)
                throw new InvalidOperationException("No authority");

            AnimatorTrigger(triggerNameHash, AnimatorTriggerAction.Reset);
        }

        private void AnimatorTrigger(int hash, AnimatorTriggerAction action)
        {
            DoTriggerAction(hash, action);

            _writer.SeekZero();
            _writer.WriteInt32(hash);
            _writer.WriteByte((byte)action);

            OwnerToOthersRpc(
                SyncAnimatorTriggersHandler,
                data: _writer.ToArray(),
                DeliveryMethod.Unreliable,
                invokeLocally: false);
        }

        [AutoRegisterRpcHandler]
        private void SyncAnimatorTriggersHandler(byte[] data)
        {
            if (IsLocal)
                return;

            _reader.Replace(data);
            var hash = _reader.ReadInt32();
            var action = (AnimatorTriggerAction)_reader.ReadByte();

            DoTriggerAction(hash, action);
        }

        private void DoTriggerAction(int hash, AnimatorTriggerAction action)
        {
            switch (action)
            {
                case AnimatorTriggerAction.Set:
                    _animator.SetTrigger(hash);
                    break;
                case AnimatorTriggerAction.Reset:
                    _animator.ResetTrigger(hash);
                    break;
            }
        }

        private void Update()
        {
            if (!Identity.Initialized)
                return;

            foreach (var animatorValueProxy in _animatorParameterValueProxyCollection)
                animatorValueProxy.OnUpdate(Time.deltaTime, IsLocal);

            if (!IsLocal)
            {
                for (var layerIndex = 0; layerIndex < _animator.layerCount; layerIndex++)
                {
                    var targetLayerWeight = _remoteLayerWeights[layerIndex];
                    var currentLayerWeight = _animator.GetLayerWeight(layerIndex);

                    // Not sure if we should smooth the layer weight, it might be intended to be changed immediately, from 0 to 1 for instance.
                    //_animator.SetLayerWeight(layerIndex, Mathf.MoveTowards(currentLayerWeight, targetLayerWeight, Time.deltaTime * _remoteLayerWeightChangeSpeed));
                    _animator.SetLayerWeight(layerIndex, targetLayerWeight);
                }
            }
        }

        protected override void OnSerialize(NetWriter writer)
        {
            base.OnSerialize(writer);

            // Parameters.
            foreach (var p in _animatorParameterValueProxyCollection)
                p.Serialize(writer);

            // Layers.
            for (var layerIndex = 0; layerIndex < _animator.layerCount; layerIndex++)
            {
                var syncFlag = false; //_layersSyncFlags[layerIndex];
                writer.WriteBoolean(syncFlag);
                if (syncFlag)
                    continue;

                var stateToSync =
                    _animator.IsInTransition(layerIndex)
                        ? _animator.GetNextAnimatorStateInfo(layerIndex)
                        : _animator.GetCurrentAnimatorStateInfo(layerIndex);

                var layerWeight = _animator.GetLayerWeight(layerIndex);

                writer.WriteSingle(layerWeight);
                writer.WriteInt32(stateToSync.fullPathHash);
                writer.WriteSingle(stateToSync.normalizedTime);
            }
        }

        protected override void OnDeserialize(NetReader reader)
        {
            base.OnDeserialize(reader);

            // Parameters.
            foreach (var animatorValueProxy in _animatorParameterValueProxyCollection)
                animatorValueProxy.Deserialize(reader);

            // Layers.
            for (var layerIndex = 0; layerIndex < _animator.layerCount; layerIndex++)
            {
                var remoteSyncFlag = reader.ReadBoolean();
                if (remoteSyncFlag)
                    continue;

                var targetLayerWeight = reader.ReadSingle();
                var targetStateHash = reader.ReadInt32();
                var targetNormalizedTime = reader.ReadSingle();

                var localSyncFlag = false; //_layersSyncFlags[layerIndex];
                if (localSyncFlag)
                    continue;

                _remoteLayerWeights[layerIndex] = targetLayerWeight;

                switch (_stateChangeMethod)
                {
                    case AnimatorStateChangeMethod.Play:
                        _animator.Play(targetStateHash, layerIndex, targetNormalizedTime);
                        break;
                    case AnimatorStateChangeMethod.CrossFade:
                        // todo: Calling Animator.GotoState on Synchronize layer
                        _animator.CrossFade(targetStateHash, _crossFadeDuration, layerIndex, targetNormalizedTime);
                        break;
                }
            }
        }
    }
}