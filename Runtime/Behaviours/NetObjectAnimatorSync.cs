using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GSAnimatorSync : GSInstanceBehaviour
{
    enum AnimatorTriggerAction
    {
        Set, Reset,
    }

    public bool SyncEnabled
    {
        get => m_SyncEnabled;
        set
        {
            if (!IsLocal)
                throw new Exception("Cannot change transform sync state on remote object");
            m_SyncEnabled = value;
        }
    }
    [SerializeField, Range(0.01f, 1f)]
    float m_MinSendRate = 0.1f;

    [SerializeField]
    bool m_SyncEnabled = true;

    [SerializeField]
    Animator m_Animator;

    float m_NextSendEventTime;

    public override void OnPostInitialize()
    {
    }

    void FixedUpdate()
    {
        if (!m_SyncEnabled || !IsLocal)
            return;

        if (m_NextSendEventTime > Time.fixedUnscaledTime)
            return;

        m_NextSendEventTime = Time.fixedUnscaledTime + m_MinSendRate;
        SetDirty();
    }

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
        writer.Write((int)action);

        OwnerToOthersRPC(
            SyncAnimatorTriggersHandler,
            writer.ToArray(), 
            DeliveryIntent.UnreliableSequenced, 
            false);
    }

    [AutoRegisterRPCHandler]
    void SyncAnimatorTriggersHandler(byte[] data)
    {
        if (IsLocal)
            return;

        var r = new NetReader(data);
        var hash = r.ReadInt32();
        var action = (AnimatorTriggerAction)r.ReadInt32();

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

    #region WRITE/READ
    void WriteLayers(NetWriter writer)
    {
        ForEachLayer((state, layerWeight, layerIndex) => 
        {
            writer.Write(layerWeight);
            writer.Write(state.fullPathHash);
            writer.Write(state.normalizedTime);
        });
    }

    void ReadLayers(NetReader reader)
    {
        ForEachLayer((state, layerWeight, layerIndex) =>
        {
            var targetLayerWeight = reader.ReadSingle();
            var targetStateHash = reader.ReadInt32();
            var targetStateNormalizedTime = reader.ReadSingle();

            if (layerWeight != targetLayerWeight)
                m_Animator.SetLayerWeight(layerIndex, targetLayerWeight);

            if(targetStateHash != 0 && state.fullPathHash != targetStateHash)
                m_Animator.Play(targetStateHash, layerIndex, targetStateNormalizedTime);
        });
    }

    void WriteParameters(NetWriter writer)
    {
        ForEachParameter((p, i) =>
        {
            if (p.type == AnimatorControllerParameterType.Int)
                writer.Write(m_Animator.GetInteger(p.nameHash));

            if (p.type == AnimatorControllerParameterType.Float)
                writer.Write(m_Animator.GetFloat(p.nameHash));

            if (p.type == AnimatorControllerParameterType.Bool)
                writer.Write(m_Animator.GetBool(p.nameHash));
        });
    }

    void ReadParameters(NetReader reader)
    {
        ForEachParameter((p, i) => 
        {
            if (p.type == AnimatorControllerParameterType.Int)
                m_Animator.SetInteger(p.nameHash, reader.ReadInt32());

            if (p.type == AnimatorControllerParameterType.Float)
                m_Animator.SetFloat(p.nameHash, reader.ReadSingle());

            if (p.type == AnimatorControllerParameterType.Bool)
                m_Animator.SetBool(p.nameHash, reader.ReadBoolean());
        });
    }
    #endregion

    protected override void DeserializeDirtyChecked(NetReader reader, bool firstTime)
    {
        ReadParameters(reader);
        ReadLayers(reader);
    }

    protected override void SerializeDirtyChecked(NetWriter writer)
    {
        WriteParameters(writer);
        WriteLayers(writer);
    }
}
