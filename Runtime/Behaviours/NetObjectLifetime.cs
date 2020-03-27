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
    public float CurrentLifetime
    {
        get => Data.CurrentLifetime.Value;
        private set => Data.CurrentLifetime.Set(value);
    }

    [SerializeField]    float m_lifetime = 60f;
    [SerializeField]    bool m_autoDestroy;
    [SerializeField]    UnityEvent m_onDestroy;

    public void SetLifetimeWithAuthority(float lifetime) => CurrentLifetime = lifetime;

    public override void OnPostInitialize()
    {
        if (IsLocal)
            CurrentLifetime = m_lifetime;
    }

    void FixedUpdate()
    {
        if (!IsLocal)
            return;

        if (CurrentLifetime > 0f)
        {
            CurrentLifetime -= Time.fixedDeltaTime;

            if (CurrentLifetime <= 0f)
            {
                CurrentLifetime = 0f;
                m_onDestroy.Invoke();
                if (m_autoDestroy)
                    DestroyObjectViaNetwork();
            }
        }
    }
}
