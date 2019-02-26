using GameSparks.RT;
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
        get { return m_SyncEnabled; }
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
        Identity.RegisterRPCHandler(SyncAnimatorTriggersHandler);
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

        OwnerToOthersRPC(
            SyncAnimatorTriggersHandler,
            new GSWriter()
                .Write(hash)
                .Write((int)action), 
            GameSparksRT.DeliveryIntent.UNRELIABLE_SEQUENCED, 
            false);
    }

    void SyncAnimatorTriggersHandler(RTData data)
    {
        if (IsLocal)
            return;

        var r = new GSReader(data);
        var hash = r.ReadInt();
        var action = (AnimatorTriggerAction)r.ReadInt();

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
    void WriteLayers(GSWriter writer)
    {
        ForEachLayer((state, layerWeight, layerIndex) => 
        {
            writer
                .Write(layerWeight)
                .Write(state.fullPathHash)
                .Write(state.normalizedTime);
        });
    }

    void ReadLayers(GSReader reader)
    {
        ForEachLayer((state, layerWeight, layerIndex) =>
        {
            var targetLayerWeight = reader.ReadFloat();
            var targetStateHash = reader.ReadInt();
            var targetStateNormalizedTime = reader.ReadFloat();

            if (layerWeight != targetLayerWeight)
                m_Animator.SetLayerWeight(layerIndex, targetLayerWeight);

            if(targetStateHash != 0 && state.fullPathHash != targetStateHash)
                m_Animator.Play(targetStateHash, layerIndex, targetStateNormalizedTime);
        });
    }

    void WriteParameters(GSWriter writer)
    {
        for (var i = 0; i < m_Animator.parameters.Length; i++)
        {
            var par = m_Animator.parameters[i];
            if (par.type == AnimatorControllerParameterType.Int)
                writer.Write(m_Animator.GetInteger(par.nameHash));

            if (par.type == AnimatorControllerParameterType.Float)
                writer.Write(m_Animator.GetFloat(par.nameHash));

            if (par.type == AnimatorControllerParameterType.Bool)
                writer.Write(m_Animator.GetBool(par.nameHash));
        }
    }

    void ReadParameters(GSReader reader)
    {
        for (var i = 0; i < m_Animator.parameters.Length; i++)
        {
            var par = m_Animator.parameters[i];
            if (par.type == AnimatorControllerParameterType.Int)
                m_Animator.SetInteger(par.nameHash, reader.ReadInt());

            if (par.type == AnimatorControllerParameterType.Float)
                m_Animator.SetFloat(par.nameHash, reader.ReadFloat());

            if (par.type == AnimatorControllerParameterType.Bool)
                m_Animator.SetBool(par.nameHash, reader.ReadBool());
        }
    }
    #endregion

    protected override void DeserializeDirtyChecked(GSReader reader, bool firstTime)
    {
        ReadParameters(reader);
        ReadLayers(reader);
    }

    protected override void SerializeDirtyChecked(GSWriter writer)
    {
        WriteParameters(writer);
        WriteLayers(writer);
    }
}
