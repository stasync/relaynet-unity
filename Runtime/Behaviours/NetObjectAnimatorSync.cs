using Core.Networking;
using System;
using System.Collections.Generic;
using UnityEngine;

public interface IAnimatorValueProxy
{
    void OnAuthorityUpdate();
    void OnDataDeserialized();
    void OnUpdate(float deltaTime, bool hasAuthority);
}

public abstract class AnimatorPerameterValueProxy<T> : IAnimatorValueProxy where T : struct
{
    protected readonly Animator TargetAnimator;
    protected readonly AnimatorControllerParameter Parameter;
    protected readonly SerializableValue<T> InternalValue;

    public AnimatorPerameterValueProxy(Animator animator, AnimatorControllerParameter parameter, SerializableValue<T> internalValue)
    {
        TargetAnimator = animator;
        Parameter = parameter;
        InternalValue = internalValue;
    }

    public abstract void OnAuthorityUpdate();
    public abstract void OnDataDeserialized();
    public abstract void OnUpdate(float deltaTime, bool hasAuthority);
}

public class AnimatorFloatParameterValueProxy : AnimatorPerameterValueProxy<float>
{
    public AnimatorFloatParameterValueProxy(Animator animator, AnimatorControllerParameter parameter, SerializableValue<float> internalValue) :
        base(animator, parameter, internalValue)
    { }

    public override void OnDataDeserialized() { }
    public override void OnAuthorityUpdate() => InternalValue.Set(TargetAnimator.GetFloat(Parameter.nameHash));
    public override void OnUpdate(float deltaTime, bool hasAuthority) 
    {
        if(!hasAuthority)
            TargetAnimator.SetFloat(Parameter.nameHash, InternalValue.Value, 0.1f, deltaTime);
    }
}

public class AnimatorIntParameterValueProxy : AnimatorPerameterValueProxy<int>
{
    public AnimatorIntParameterValueProxy(Animator animator, AnimatorControllerParameter parameter, SerializableValue<int> internalValue) :
        base(animator, parameter, internalValue)
    { }

    public override void OnDataDeserialized() => TargetAnimator.SetInteger(Parameter.nameHash, InternalValue.Value);
    public override void OnAuthorityUpdate() => InternalValue.Set(TargetAnimator.GetInteger(Parameter.nameHash));
    public override void OnUpdate(float deltaTime, bool hasAuthority) { }
}

public class AnimatorBoolParameterValueProxy : AnimatorPerameterValueProxy<bool>
{
    public AnimatorBoolParameterValueProxy(Animator animator, AnimatorControllerParameter parameter, SerializableValue<bool> internalValue) :
        base(animator, parameter, internalValue)
    { }

    public override void OnDataDeserialized() => TargetAnimator.SetBool(Parameter.nameHash, InternalValue.Value);
    public override void OnAuthorityUpdate() => InternalValue.Set(TargetAnimator.GetBool(Parameter.nameHash));
    public override void OnUpdate(float deltaTime, bool hasAuthority) { }
}

public class AnimatorSyncData : GSBehaviourAbstractData
{
    public SerializableValueCollection Parameters { get; private set; } = null;
    public SerializableFloatList LayerWeights { get; private set; } = null;
    public SerializableIntList LayerStates { get; private set; } = null;
    public SerializableFloatList LayerNormalizedTimes { get; private set; } = null;

    protected override void OnDefineValues()
    {
        Parameters = new SerializableValueCollection(() => !Initialized);
        LayerWeights = new SerializableFloatList(new List<float>(), WritePemission);
        LayerStates = new SerializableIntList(new List<int>(), WritePemission);
        LayerNormalizedTimes = new SerializableFloatList(new List<float>(), WritePemission);
    }
}

[DisallowMultipleComponent]
public sealed class GSAnimatorSync : GSInstanceBehaviour<AnimatorSyncData>
{
    enum AnimatorStateChangeMethod : byte
    {
        Play, CrossFade,
    }

    enum AnimatorTriggerAction : byte
    {
        Set, Reset,
    }

    [SerializeField]
    Animator m_Animator;

    [SerializeField]
    AnimatorStateChangeMethod m_StateChangeMethod = AnimatorStateChangeMethod.CrossFade;

    [SerializeField, Range(0f, 1f)]
    float m_CrossFadeDuration = 0.1f;

    List<IAnimatorValueProxy> m_AnimatorParameterValueProxyCollection = new List<IAnimatorValueProxy>();

    protected override void OnDataValuesCollected()
    {
        base.OnDataValuesCollected();

        m_AnimatorParameterValueProxyCollection.Clear();
        ForEachParameter((p, i) => 
        {
            switch(p.type)
            {
                case AnimatorControllerParameterType.Bool:
                    BuildProxyForParameter(p.name, 
                        () => new SerializableBool(false, () => Data.HasPermission), v => new AnimatorBoolParameterValueProxy(m_Animator, p, v));
                    break;

                case AnimatorControllerParameterType.Float:
                    BuildProxyForParameter(p.name,
                        () => new SerializableFloat(0f, () => Data.HasPermission), v => new AnimatorFloatParameterValueProxy(m_Animator, p, v));
                    break;

                case AnimatorControllerParameterType.Int:
                    BuildProxyForParameter(p.name,
                         () => new SerializableInt(0, () => Data.HasPermission), v => new AnimatorIntParameterValueProxy(m_Animator, p, v));
                    break;
            }
        });

        void BuildProxyForParameter<T>(
            string valueName,
            Func<SerializableValue<T>> valueFactory,
            Func<SerializableValue<T>, AnimatorPerameterValueProxy<T>> parameterFactory) where T : struct
        {
            var v = valueFactory();
            var p = parameterFactory(v);

            Data.Parameters.Add(valueName, v);
            m_AnimatorParameterValueProxyCollection.Add(p);
        }
    }

    public override void OnPostInitialize() { }

    void ForEachLayer(Action<AnimatorStateInfo, float, int> processor)
    {
        for (var i = 0; i < m_Animator.layerCount; i++)
            processor(
                m_Animator.IsInTransition(i) ?
                m_Animator.GetNextAnimatorStateInfo(i) :
                m_Animator.GetCurrentAnimatorStateInfo(i), m_Animator.GetLayerWeight(i), i);
    }

    void ForEachParameter(Action<AnimatorControllerParameter, int> processor)
    {
        for (var i = 0; i < m_Animator.parameters.Length; i++)
            processor(m_Animator.parameters[i], i);
    }

    public void SetTrigger(string triggerName)
    {
        if (!IsLocal)
            throw new InvalidOperationException("No authority");

        AnimatorTrigger(Animator.StringToHash(triggerName), AnimatorTriggerAction.Set);
    }

    public void ResetTrigger(string triggerName)
    {
        if (!IsLocal)
            throw new InvalidOperationException("No authority");

        AnimatorTrigger(Animator.StringToHash(triggerName), AnimatorTriggerAction.Reset);
    }

    void AnimatorTrigger(int hash, AnimatorTriggerAction action)
    {
        DoTriggerAction(hash, action);

        var writer = new NetWriter();
        writer.Write(hash);
        writer.Write((byte)action);

        OwnerToOthersRPC(
            SyncAnimatorTriggersHandler,
            writer.ToArray(), 
            DeliveryIntent.Unreliable, 
            false);
    }

    [AutoRegisterRPCHandler]
    void SyncAnimatorTriggersHandler(byte[] data)
    {
        if (IsLocal)
            return;

        var r = new NetReader(data);
        var hash = r.ReadInt32();
        var action = (AnimatorTriggerAction)r.ReadByte();

        DoTriggerAction(hash, action);
    }

    void DoTriggerAction(int hash, AnimatorTriggerAction action)
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

    void Update()
    {
        if (!Identity.Initialized)
            return;

        foreach (var p in m_AnimatorParameterValueProxyCollection)
            p.OnUpdate(Time.deltaTime, IsLocal);
    }

    public override void OnAuthorityUpdate()
    {
        base.OnAuthorityUpdate();

        foreach (var p in m_AnimatorParameterValueProxyCollection)
            p.OnAuthorityUpdate();

        ForEachLayer((state, layerWeight, layerIndex) =>
        {
            Data.LayerWeights.SetAt(layerIndex, layerWeight);
            Data.LayerStates.SetAt(layerIndex, state.fullPathHash);
            Data.LayerNormalizedTimes.SetAt(layerIndex, state.normalizedTime);
        });
    }

    protected override void OnDataDeserialized(bool firstTime)
    {
        base.OnDataDeserialized(firstTime);
        foreach (var p in m_AnimatorParameterValueProxyCollection)
            p.OnDataDeserialized();

        ForEachLayer((state, layerWeight, layerIndex) =>
        {
            if (!ValidateCollections(layerIndex, Data.LayerWeights, Data.LayerStates, Data.LayerNormalizedTimes))
                return;

            var targetLayerWeight = Data.LayerWeights[layerIndex];
            var targetStateHash = Data.LayerStates[layerIndex];
            var targetStateNormalizedTime = Data.LayerNormalizedTimes[layerIndex];

            if (layerWeight != targetLayerWeight)
                m_Animator.SetLayerWeight(layerIndex, targetLayerWeight);

            if (targetStateHash != 0 && state.fullPathHash != targetStateHash)
                switch (m_StateChangeMethod)
                {
                    case AnimatorStateChangeMethod.Play:
                        m_Animator.Play(targetStateHash, layerIndex, targetStateNormalizedTime);
                        break;
                    case AnimatorStateChangeMethod.CrossFade:
                        m_Animator.CrossFade(targetStateHash, m_CrossFadeDuration, layerIndex, targetStateNormalizedTime);
                        break;
                }
        });

        bool ValidateCollections(int index, params ICollectionWithCount[] collections)
        {
            foreach (var c in collections)
                if (index >= c.Count)
                    return false;
            return true;
        }
    }
}
