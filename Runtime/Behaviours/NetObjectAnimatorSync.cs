using System;
using System.Collections.Generic;
using Keel.Networking;
using LROTech.Network;
using LROTech.Network.GameObjects;
using LROTech.Network.StateManagement;
using UnityEngine;
using UnityEngine.Serialization;

namespace LROTech.NetworkBehaviours
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

        [SerializeField] private Animator m_Animator;

        [SerializeField] private AnimatorStateChangeMethod m_StateChangeMethod = AnimatorStateChangeMethod.CrossFade;

        [SerializeField, Range(0f, 1f)] private float m_CrossFadeDuration = 0.1f;

        [FormerlySerializedAs("m_RemoteLayerWeightChnageSpeed")] [SerializeField, Range(0f, 10f)]
        private float m_RemoteLayerWeightChangeSpeed = 5f;

        private readonly List<IAnimatorValueProxy> m_AnimatorParameterValueProxyCollection = new();
        private readonly List<float> m_RemoteLayerWeights = new();
        private bool[] m_LayersSyncFlags;

        private readonly NetWriter m_Writer = new();
        private readonly NetReader m_Reader = new();

        private void Awake()
        {
            foreach (var parameter in m_Animator.parameters)
            {
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Bool:
                        m_AnimatorParameterValueProxyCollection.Add(new AnimatorBoolParameterValueProxy(m_Animator, parameter));
                        break;

                    case AnimatorControllerParameterType.Float:
                        m_AnimatorParameterValueProxyCollection.Add(new AnimatorFloatParameterValueProxy(m_Animator, parameter));
                        break;

                    case AnimatorControllerParameterType.Int:
                        m_AnimatorParameterValueProxyCollection.Add(new AnimatorIntParameterValueProxy(m_Animator, parameter));
                        break;
                }
            }

            for (var layerIndex = 0; layerIndex < m_Animator.layerCount; layerIndex++)
                m_RemoteLayerWeights.Add(m_Animator.GetLayerWeight(layerIndex));

            /*var runtimeController = (UnityEditor.Animations.AnimatorController)m_Animator.runtimeAnimatorController;

            m_LayersSyncFlags = new bool[runtimeController.layers.Length];
            for (var index = 0; index < runtimeController.layers.Length; index++)
                m_LayersSyncFlags[index] = (uint)runtimeController.layers[index].syncedLayerIndex < (uint)runtimeController.layers.Length;*/
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

            m_Writer.SeekZero();
            m_Writer.WriteInt32(hash);
            m_Writer.WriteByte((byte)action);

            OwnerToOthersRpc(
                SyncAnimatorTriggersHandler,
                data: m_Writer.ToArray(),
                DeliveryMethod.Unreliable,
                invokeLocally: false);
        }

        [AutoRegisterRpcHandler]
        private void SyncAnimatorTriggersHandler(byte[] data)
        {
            if (IsLocal)
                return;

            m_Reader.Replace(data);
            var hash = m_Reader.ReadInt32();
            var action = (AnimatorTriggerAction)m_Reader.ReadByte();

            DoTriggerAction(hash, action);
        }

        private void DoTriggerAction(int hash, AnimatorTriggerAction action)
        {
            switch (action)
            {
                case AnimatorTriggerAction.Set:
                    m_Animator.SetTrigger(hash);
                    break;
                case AnimatorTriggerAction.Reset:
                    m_Animator.ResetTrigger(hash);
                    break;
            }
        }

        private void Update()
        {
            if (!Identity.Initialized)
                return;

            foreach (var animatorValueProxy in m_AnimatorParameterValueProxyCollection)
                animatorValueProxy.OnUpdate(Time.deltaTime, IsLocal);

            if (!IsLocal)
            {
                for (var layerIndex = 0; layerIndex < m_Animator.layerCount; layerIndex++)
                {
                    var targetLayerWeight = m_RemoteLayerWeights[layerIndex];
                    var currentLayerWeight = m_Animator.GetLayerWeight(layerIndex);

                    // Not sure if we should smooth the layer weight, it might be intended to be changed immediately, form 0 to 1 for instance.
                    //m_Animator.SetLayerWeight(layerIndex, Mathf.MoveTowards(currentLayerWeight, targetLayerWeight, Time.deltaTime * m_RemoteLayerWeightChnageSpeed));
                    m_Animator.SetLayerWeight(layerIndex, targetLayerWeight);
                }
            }
        }

        protected override void OnSerialize(NetWriter writer)
        {
            base.OnSerialize(writer);

            // Parameters.
            foreach (var p in m_AnimatorParameterValueProxyCollection)
                p.Serialize(writer);

            // Layers.
            for (var layerIndex = 0; layerIndex < m_Animator.layerCount; layerIndex++)
            {
                var syncFlag = false; //m_LayersSyncFlags[layerIndex];
                writer.WriteBoolean(syncFlag);
                if (syncFlag)
                    continue;

                var stateToSync =
                    m_Animator.IsInTransition(layerIndex)
                        ? m_Animator.GetNextAnimatorStateInfo(layerIndex)
                        : m_Animator.GetCurrentAnimatorStateInfo(layerIndex);

                var layerWeight = m_Animator.GetLayerWeight(layerIndex);

                writer.WriteSingle(layerWeight);
                writer.WriteInt32(stateToSync.fullPathHash);
                writer.WriteSingle(stateToSync.normalizedTime);
            }
        }

        protected override void OnDeserialize(NetReader reader)
        {
            base.OnDeserialize(reader);

            // Parameters.
            foreach (var animatorValueProxy in m_AnimatorParameterValueProxyCollection)
                animatorValueProxy.Deserialize(reader);

            // Layers.
            for (var layerIndex = 0; layerIndex < m_Animator.layerCount; layerIndex++)
            {
                var remoteSyncFlag = reader.ReadBoolean();
                if (remoteSyncFlag)
                    continue;

                var targetLayerWeight = reader.ReadSingle();
                var targetStateHash = reader.ReadInt32();
                var targetNormalizedTime = reader.ReadSingle();

                var localSyncFlag = false; //m_LayersSyncFlags[layerIndex];
                if (localSyncFlag)
                    continue;

                m_RemoteLayerWeights[layerIndex] = targetLayerWeight;

                switch (m_StateChangeMethod)
                {
                    case AnimatorStateChangeMethod.Play:
                        m_Animator.Play(targetStateHash, layerIndex, targetNormalizedTime);
                        break;
                    case AnimatorStateChangeMethod.CrossFade:
                        // todo: Calling Animator.GotoState on Synchronize layer
                        m_Animator.CrossFade(targetStateHash, m_CrossFadeDuration, layerIndex, targetNormalizedTime);
                        break;
                }
            }
        }
    }
}