using System;
using Keel.RelayNet.GameObjects;
using Keel.RelayNet.StateManagement;
using Keel.RelayNet.StateManagement.Values;
using UnityEngine;

namespace Keel.RelayNet.Behaviours
{
    public sealed class NetObjectLifetimeData : NetObjectStateData
    {
        public readonly NetworkValue<float> Lifetime = new();
        public readonly NetworkValue<float> CurrentLifetime = new();

        public float NormalizedLifetime => CurrentLifetime.Value / Lifetime.Value;
    }

    public sealed class NetObjectLifetime : NetObjectBehaviour<NetObjectLifetimeData>
    {
        public event Action TimePassed = delegate { };
        public float NormalizedLifetime => Data.NormalizedLifetime;
        public float CurrentLifetime => Data.CurrentLifetime.Value;

        [SerializeField] private bool _updateTime = true;

        [SerializeField] private float _lifetime = 60f;

        [SerializeField] private bool _autoDestroy;

        public void SetLifetimeWithAuthority(float lifetime)
        {
            Data.Lifetime.Value = lifetime;
            Data.CurrentLifetime.Value = lifetime;
        }

        protected override void OnPostInitializeOnce()
        {
            base.OnPostInitializeOnce();

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