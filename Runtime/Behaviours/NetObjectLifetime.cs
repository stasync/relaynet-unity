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
    public float NormalizedLifetime => CurrentLifetime / m_lifetime;
    public float CurrentLifetime => Data.CurrentLifetime.Value;

    [SerializeField]    float m_lifetime = 60f;
    [SerializeField]    bool m_autoDestroy;
    [SerializeField]    UnityEvent m_onDestroy;

    public void SetLifetimeWithAuthority(float lifetime) => Data.CurrentLifetime.Set(lifetime);

    public override void OnPostInitialize()
    {
        if (IsLocal)
            SetLifetimeWithAuthority(m_lifetime);
    }

    void FixedUpdate()
    {
        if (!IsLocal)
            return;

        if (Data.CurrentLifetime.Value > 0f)
        {
            Data.CurrentLifetime.Change(-Time.fixedDeltaTime);

            if (Data.CurrentLifetime.Value <= 0f)
            {
                Data.CurrentLifetime.Set(0f);
                m_onDestroy.Invoke();
                if (m_autoDestroy)
                    DestroyObjectViaNetwork();
            }
        }
    }
}
