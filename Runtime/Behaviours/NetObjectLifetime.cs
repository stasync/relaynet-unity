using UnityEngine;
using UnityEngine.Events;

public class GSObjectLifetime : GSInstanceBehaviour
{
    public float NormalizedLifetime { get { return CurrentLifetime / m_lifetime; } }
    public float CurrentLifetime { get; private set; }

    [SerializeField]    private float m_lifetime = 60f;
    [SerializeField]    private bool m_autoDestroy;
    [SerializeField]    private UnityEvent m_onDestroy;

    public void SetLifetime(float lifetime)
    {
        CurrentLifetime = lifetime;
        SetDirty();
    }

    public override void OnPostInitialize()
    {
        if (IsSceneObject && Network.Realtime.IsMasterPlayer || IsLocal)
            CurrentLifetime = m_lifetime;
    }

    protected override void SerializeDirtyChecked(NetWriter writer)
    {
        writer.Write(CurrentLifetime);
    }

    protected override void DeserializeDirtyChecked(NetReader reader, bool firstTime)
    {
        CurrentLifetime = reader.ReadSingle();
    }

    private void FixedUpdate()
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

            SetDirty();
        }
    }
}
