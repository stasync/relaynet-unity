using System;
using LROTech.Network.GameObjects;
using LROTech.Network.StateManagement;
using UnityEngine;

namespace LROTech.NetworkBehaviours
{
    public sealed class NetObjectLifetimeData : NetObjectStateData
    {
        public readonly NetworkValue<float> Lifetime = new();
        public readonly NetworkValue<float> CurrentLifetime = new();

        public float NormalizedLifetime => CurrentLifetime.Value / Lifetime.Value;
    }

    public sealed class NetObjectObjectLifetime : NetObjectBehaviour<NetObjectLifetimeData>
    {
        public event Action TimePassed = delegate { };
        public float NormalizedLifetime => Data.NormalizedLifetime;
        public float CurrentLifetime => Data.CurrentLifetime.Value;

        [SerializeField] private bool m_UpdateTime = true;

        [SerializeField] private float m_Lifetime = 60f;

        [SerializeField] private bool m_AutoDestroy;

        public void SetLifetimeWithAuthority(float lifetime)
        {
            Data.Lifetime.Value = lifetime;
            Data.CurrentLifetime.Value = lifetime;
        }

        protected override void OnInitializeViewData()
        {
            base.OnInitializeViewData();

            if (IsLocal)
                SetLifetimeWithAuthority(m_Lifetime);
        }

        private void FixedUpdate()
        {
            if (!IsLocal)
                return;

            if (Data.CurrentLifetime.Value > 0f)
            {
                if (m_UpdateTime)
                    Data.CurrentLifetime.Value -= Time.fixedDeltaTime;

                if (Data.CurrentLifetime.Value <= 0f)
                {
                    Data.CurrentLifetime.Value = 0;
                    TimePassed();

                    if (m_AutoDestroy)
                        DestroyObjectViaNetwork();
                }
            }
        }
    }
}