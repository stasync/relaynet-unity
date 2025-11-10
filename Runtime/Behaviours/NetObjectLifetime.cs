using System;
using LROTech.Network.GameObjects;
using LROTech.Network.StateManagement;
using LROTech.Network.StateManagement.Values;
using UnityEngine;
using UnityEngine.Serialization;

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

        [FormerlySerializedAs("m_UpdateTime")] 
        [SerializeField] private bool _updateTime = true;

        [FormerlySerializedAs("m_Lifetime")] 
        [SerializeField] private float _lifetime = 60f;

        [FormerlySerializedAs("m_AutoDestroy")] 
        [SerializeField] private bool _autoDestroy;

        public void SetLifetimeWithAuthority(float lifetime)
        {
            Data.Lifetime.Value = lifetime;
            Data.CurrentLifetime.Value = lifetime;
        }

        protected override void OnInitializeViewData()
        {
            base.OnInitializeViewData();

            if (IsLocal)
                SetLifetimeWithAuthority(_lifetime);
        }

        private void Update()
        {
            if (!IsLocal)
                return;

            if (Data.CurrentLifetime.Value > 0f)
            {
                if (_updateTime)
                    Data.CurrentLifetime.Value -= Time.deltaTime;

                if (Data.CurrentLifetime.Value <= 0f)
                {
                    Data.CurrentLifetime.Value = 0f;
                    TimePassed();

                    if (_autoDestroy)
                        DestroyObjectViaNetwork();
                }
            }
        }
    }
}