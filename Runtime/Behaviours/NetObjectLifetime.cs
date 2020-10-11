using UnityEngine;
using UnityEngine.Events;

public class GSObjectLifetimeData : GSBehaviourAbstractData
{
    public SerializableFloat CurrentLifetime { get; private set; } = null;

    protected override void OnDefineValues()
    {
        CurrentLifetime = new SerializableFloat(0f, WritePemission);
    }
}

public class GSObjectLifetime : GSInstanceBehaviour<GSObjectLifetimeData>
{
    public float NormalizedLifetime => CurrentLifetime / m_Lifetime;
    public float CurrentLifetime => Data.CurrentLifetime.Value;

    [SerializeField]
    bool m_UpdateTime = true;

    [SerializeField]    
    float m_Lifetime = 60f;

    [SerializeField]    
    bool m_AutoDestroy;

    public void SetLifetimeWithAuthority(float lifetime) => Data.CurrentLifetime.Set(lifetime);

    public override void OnPostInitialize()
    {
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
                if (m_AutoDestroy)
                    DestroyObjectViaNetwork();
            }
        }
    }
}
