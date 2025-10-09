using Core.Networking;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public interface IAnimatorValueProxy : ISerializable
{
    void OnUpdate(float deltaTime, bool hasAuthority);
}

public abstract class AnimatorParameterValueProxy<T> : IAnimatorValueProxy where T : struct
{
    protected readonly Animator Animator;
    protected readonly AnimatorControllerParameter Parameter;
    protected T LastDeserializedValue { get; private set; }

    public AnimatorParameterValueProxy(Animator animator, AnimatorControllerParameter parameter)
    {
        Animator = animator;
        Parameter = parameter;
    }

    public virtual void OnUpdate(float deltaTime, bool hasAuthority) { }

    public abstract void Serialize(NetWriter writer);
    public virtual void Deserialize(NetReader reader, bool firstTime) =>
        LastDeserializedValue = DeserializeValue(reader, firstTime);

    protected abstract T DeserializeValue(NetReader reader, bool firstTime);
}

public class AnimatorFloatParameterValueProxy : AnimatorParameterValueProxy<float>
{
    public AnimatorFloatParameterValueProxy(Animator animator, AnimatorControllerParameter parameter) : base(animator, parameter)
    { }

    public override void OnUpdate(float deltaTime, bool hasAuthority) 
    {
        base.OnUpdate(deltaTime, hasAuthority);

        if(!hasAuthority)
            Animator.SetFloat(Parameter.nameHash, LastDeserializedValue, 0.1f, deltaTime);
    }

    public override void Serialize(NetWriter writer) =>
        writer.Write(Animator.GetFloat(Parameter.nameHash));

    protected override float DeserializeValue(NetReader reader, bool firstTime) =>
        reader.ReadSingle();

}

public class AnimatorIntParameterValueProxy : AnimatorParameterValueProxy<int>
{
    public AnimatorIntParameterValueProxy(Animator animator, AnimatorControllerParameter parameter) : base(animator, parameter)
    { }

    public override void Serialize(NetWriter writer) =>
        writer.Write(Animator.GetInteger(Parameter.nameHash));

    protected override int DeserializeValue(NetReader reader, bool firstTime) =>
        reader.ReadInt32();

    public override void Deserialize(NetReader reader, bool firstTime)
    {
        base.Deserialize(reader, firstTime);
        Animator.SetInteger(Parameter.nameHash, LastDeserializedValue);
    }
}

public class AnimatorBoolParameterValueProxy : AnimatorParameterValueProxy<bool>
{
    public AnimatorBoolParameterValueProxy(Animator animator, AnimatorControllerParameter parameter) : base(animator, parameter)
    { }

    public override void Serialize(NetWriter writer) =>
        writer.Write(Animator.GetBool(Parameter.nameHash));

    protected override bool DeserializeValue(NetReader reader, bool firstTime) =>
        reader.ReadBoolean();

    public override void Deserialize(NetReader reader, bool firstTime)
    {
        base.Deserialize(reader, firstTime);
        Animator.SetBool(Parameter.nameHash, LastDeserializedValue);
    }
}

[DisallowMultipleComponent]
public sealed class GSAnimatorSync : GSInstanceBehaviour
{
    private enum AnimatorStateChangeMethod : byte
    {
        Play, CrossFade,
    }

    private enum AnimatorTriggerAction : byte
    {
        Set, Reset,
    }

    [SerializeField]
    private Animator m_Animator;

    [SerializeField]
    private AnimatorStateChangeMethod m_StateChangeMethod = AnimatorStateChangeMethod.CrossFade;

    [SerializeField, Range(0f, 1f)]
    private float m_CrossFadeDuration = 0.1f;

    [FormerlySerializedAs("m_RemoteLayerWeightChnageSpeed")] 
    [SerializeField, Range(0f, 10f)]
    private float m_RemoteLayerWeightChangeSpeed = 5f;

    private readonly List<IAnimatorValueProxy> m_AnimatorParameterValueProxyCollection = new ();
    private readonly List<float> m_RemoteLayerWeights = new();

    private void Awake()
    {
        foreach (var p in m_Animator.parameters)
        {
            switch (p.type)
            {
                case AnimatorControllerParameterType.Bool:
                    m_AnimatorParameterValueProxyCollection.Add(new AnimatorBoolParameterValueProxy(m_Animator, p));
                    break;

                case AnimatorControllerParameterType.Float:
                    m_AnimatorParameterValueProxyCollection.Add(new AnimatorFloatParameterValueProxy(m_Animator, p));
                    break;

                case AnimatorControllerParameterType.Int:
                    m_AnimatorParameterValueProxyCollection.Add(new AnimatorIntParameterValueProxy(m_Animator, p));
                    break;
            }
        }

        for (var layerIndex = 0; layerIndex < m_Animator.layerCount; layerIndex++)
            m_RemoteLayerWeights.Add(m_Animator.GetLayerWeight(layerIndex));
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

        var writer = NetWriter.Get();
        writer.Write(hash);
        writer.Write((byte)action);

        OwnerToOthersRPC(
            SyncAnimatorTriggersHandler,
            writer.ToArray(), 
            DeliveryIntent.Unreliable, 
            false);
    }

    [AutoRegisterRPCHandler]
    private void SyncAnimatorTriggersHandler(byte[] data)
    {
        if (IsLocal)
            return;

        var r = NetReader.Get(data);
        var hash = r.ReadInt32();
        var action = (AnimatorTriggerAction)r.ReadByte();

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

        foreach (var p in m_AnimatorParameterValueProxyCollection)
            p.OnUpdate(Time.deltaTime, IsLocal);

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

    protected override void SerializeDirtyChecked(NetWriter writer)
    {
        base.SerializeDirtyChecked(writer);

        // Parameters.
        foreach (var p in m_AnimatorParameterValueProxyCollection)
            p.Serialize(writer);

        // Layers.
        for (var layerIndex = 0; layerIndex < m_Animator.layerCount; layerIndex++)
        {
            var stateToSync =
                m_Animator.IsInTransition(layerIndex) ?
                m_Animator.GetNextAnimatorStateInfo(layerIndex) :
                m_Animator.GetCurrentAnimatorStateInfo(layerIndex);

            var layerWeight = m_Animator.GetLayerWeight(layerIndex);

            writer.Write(layerWeight);
            writer.Write(stateToSync.fullPathHash);
            writer.Write(stateToSync.normalizedTime);
        }
    }

    protected override void DeserializeDirtyChecked(NetReader reader, bool firstTime)
    {
        base.DeserializeDirtyChecked(reader, firstTime);

        // Parameters.
        foreach (var p in m_AnimatorParameterValueProxyCollection)
            p.Deserialize(reader, firstTime);

        // Layers.
        for (var layerIndex = 0; layerIndex < m_Animator.layerCount; layerIndex++)
        {
            var targetLayerWeight = reader.ReadSingle();
            var targetStateHash = reader.ReadInt32();
            var targetNormalizedTime = reader.ReadSingle();

            m_RemoteLayerWeights[layerIndex] = targetLayerWeight;

            switch (m_StateChangeMethod)
            {
                case AnimatorStateChangeMethod.Play:
                    m_Animator.Play(targetStateHash, layerIndex, targetNormalizedTime);
                    break;
                case AnimatorStateChangeMethod.CrossFade:
                    m_Animator.CrossFade(targetStateHash, m_CrossFadeDuration, layerIndex, targetNormalizedTime);
                    break;
            }
        }
    }
}
