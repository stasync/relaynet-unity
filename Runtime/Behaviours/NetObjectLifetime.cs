using System;
using LROTech.Network.GameObjects;
using LROTech.Network.StateManagement;
using UnityEngine;

namespace LROTech.NetworkBehaviours
{
    public sealed class NetObjectLifetimeData : NetObjectStateData
    {
        public readonly SerializableFloat Lifetime = new();
        public readonly SerializableFloat CurrentLifetime = new ();

        public float NormalizedLifetime => CurrentLifetime.Value / Lifetime.Value;
    }

    public sealed class NetObjectObjectLifetime : NetObjectBehaviour<NetObjectLifetimeData>
    {
        public event Action TimePassed = delegate { };
        public float NormalizedLifetime => Data.NormalizedLifetime;
        public float CurrentLifetime => Data.CurrentLifetime.Value;

        [SerializeField]
        private bool m_UpdateTime = true;

        [SerializeField]    
        private float m_Lifetime = 60f;

        [SerializeField]    
        private bool m_AutoDestroy;

        public void SetLifetimeWithAuthority(float lifetime)
        {
            Data.Lifetime.Set(lifetime);
            Data.CurrentLifetime.Set(lifetime);
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
}