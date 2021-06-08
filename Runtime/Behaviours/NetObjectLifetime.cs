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

public class GSObjectLifetime : GSInstanceBehaviour<GSBehaviourEmptyData, GSObjectLifetimeData>
{
    public event Action TimePassed = delegate { };
    public float NormalizedLifetime => Data.NormalizedLifetime;
    public float CurrentLifetime => Data.CurrentLifetime.Value;

    [SerializeField]
    bool m_UpdateTime = true;

    [SerializeField]    
    float m_Lifetime = 60f;

    [SerializeField]    
    bool m_AutoDestroy;

    public void SetLifetimeWithAuthority(float lifetime)
    {
        Data.Lifetime.Set(lifetime);
        Data.CurrentLifetime.Set(lifetime);
    }

    public override void OnPostInitialize() { }

    public override void OnInitializeViewData()
    {
        base.OnInitializeViewData();

        if (IsLocal)
            SetLifetimeWithAuthority(m_Lifetime);
    }

    void FixedUpdate()
    {
        if (!IsLocal)
            return;

        if (Data.CurrentLifetime.Value > 0f)
        {
            if(m_UpdateTime)
                Data.CurrentLifetime.Change(-Time.fixedDeltaTime);

            if (Data.CurrentLifetime.Value <= 0f)
            {
                Data.CurrentLifetime.Set(0f);
                TimePassed();

                if (m_AutoDestroy)
                    DestroyObjectViaNetwork();
            }
        }
    }
}
