using GameSparks.RT;
using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GSAnimatorSync : GSInstanceBehaviour
{
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

    int m_AnimationHash;
    int m_TransitionHash;
    float m_NextSendEventTime;

    public override void OnInitialze()
    {
        Identity.RegisterRPCHandler(RpcCode.SyncAnimatorParameters, SyncAnimatorParametersHandler);
    }

    void SyncAnimatorParametersHandler(RTData data)
    {
        if (IsLocal)
            return;

        var r = new GSReader(data);
        var stateHash = r.ReadInt();
        var normalizedTime = r.ReadFloat();
        var parameters = new GSReader(r.ReadData());

        if (stateHash != 0)
            m_Animator.Play(stateHash, 0, normalizedTime);

        ReadParameters(parameters);
    }

    void FixedUpdate()
    {
        if (!m_SyncEnabled && IsLocal)
            return;

        if (m_NextSendEventTime > Time.fixedUnscaledTime)
            return;
        m_NextSendEventTime = Time.fixedUnscaledTime + m_MinSendRate;

        int stateHash;
        float normalizedTime;
        if (!CheckAnimStateChanged(out stateHash, out normalizedTime))
            return;

        var rpc = new GSWriter();
        WriteAnimatorState(rpc, stateHash, normalizedTime);

        AuthRpc(RpcCode.SyncAnimatorParameters, rpc, GameSparksRT.DeliveryIntent.UNRELIABLE_SEQUENCED, false);
    }

    void WriteAnimatorState(GSWriter writer, int stateHash, float normalizedTime)
    {
        writer
            .Write(stateHash)
            .Write(normalizedTime);

        var parameters = new GSWriter();
        WriteParameters(parameters);

        writer.Write(parameters.AsData());
    }

    bool CheckAnimStateChanged(out int stateHash, out float normalizedTime)
    {
        stateHash = 0;
        normalizedTime = 0;

        if (m_Animator.IsInTransition(0))
        {
            var tt = m_Animator.GetAnimatorTransitionInfo(0);
            if (tt.fullPathHash != m_TransitionHash)
            {
                // first time in this transition
                m_TransitionHash = tt.fullPathHash;
                m_AnimationHash = 0;
                return true;
            }
            return false;
        }

        var st = m_Animator.GetCurrentAnimatorStateInfo(0);
        if (st.fullPathHash != m_AnimationHash)
        {
            // first time in this animation state
            if (m_AnimationHash != 0)
            {
                // came from another animation directly - from Play()
                stateHash = st.fullPathHash;
                normalizedTime = st.normalizedTime;
            }
            m_TransitionHash = 0;
            m_AnimationHash = st.fullPathHash;
            return true;
        }
        return false;
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


    protected override void DeserializeDirtyChecked(GSReader reader, bool firstTime)
    {
        var stateHash = reader.ReadInt();
        var normalizedTime = reader.ReadFloat();
        ReadParameters(reader);

        if (firstTime)
            m_Animator.Play(stateHash, 0, normalizedTime);
    }

    protected override void SerializeDirtyChecked(GSWriter writer)
    {
        var stateHash = 0;
        var normalizedTime = 0f;

        if (m_Animator.IsInTransition(0))
        {
            var st = m_Animator.GetNextAnimatorStateInfo(0);
            stateHash = st.fullPathHash;
            normalizedTime = st.normalizedTime;
        }
        else
        {
            var st = m_Animator.GetCurrentAnimatorStateInfo(0);
            stateHash = st.fullPathHash;
            normalizedTime = st.normalizedTime;
        }
        WriteAnimatorState(writer, stateHash, normalizedTime);
    }
}
