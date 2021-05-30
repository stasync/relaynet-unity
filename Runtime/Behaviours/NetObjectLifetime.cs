using System;
using UnityEngine;

public class GSObjectLifetimeData : NetObjectStateData
{
    public GSObjectLifetimeData(Func<bool> writePermission) : base(writePermission) { }
    public SerializableFloat Lifetime { get; private set; } = null;
    public SerializableFloat CurrentLifetime { get; private set; } = null;
    public float NormalizedLifetime => CurrentLifetime.Value / Lifetime.Value;

    protected override void OnDefineValues()
    {
        base.OnDefineValues();
        Lifetime = new SerializableFloat(0f, WritePemission);
        CurrentLifetime = new SerializableFloat(0f, WritePemission);
    }
}

public class GSObjectLifetime : GSInstanceEmptyBehaviour
{
    public event Action TimePassed = delegate { };
    public float NormalizedLifetime => m_StateData.NormalizedLifetime;
    public float CurrentLifetime => m_StateData.CurrentLifetime.Value;

    [SerializeField]
    bool m_UpdateTime = true;

    [SerializeField]    
    float m_Lifetime = 60f;

    [SerializeField]    
    bool m_AutoDestroy;

    GSObjectLifetimeData m_StateData;

    public void SetLifetimeWithAuthority(float lifetime)
    {
        m_StateData.Lifetime.Set(lifetime);
        m_StateData.CurrentLifetime.Set(lifetime);
    }

    public override void OnPostInitialize()
    {
        m_StateData = Identity
            .State
            .DataCollection
            .GetOrCreateData<GSObjectLifetimeData>();

        if (IsLocal)
            SetLifetimeWithAuthority(m_Lifetime);
    }

    void FixedUpdate()
    {
        if (!IsLocal)
            return;

        if (m_StateData.CurrentLifetime.Value > 0f)
        {
            if(m_UpdateTime)
                m_StateData.CurrentLifetime.Change(-Time.fixedDeltaTime);

            if (m_StateData.CurrentLifetime.Value <= 0f)
            {
                m_StateData.CurrentLifetime.Set(0f);
                TimePassed();

                if (m_AutoDestroy)
                    DestroyObjectViaNetwork();
            }
        }
    }
}
