using System;
using System.Collections.Generic;
using Core.Networking;
using LROTech.Network.StateManagement;
using UnityEngine;

namespace LROTech.Network.DefaultBehaviours
{
    public class InteractionNetworkData : NetObjectStateData
    {
        public readonly SerializableObjectUID CurrentInteraction = new ();
        public readonly SerializableObjectUID LastInteraction = new ();
        public readonly SerializableBool InteractionsEnabled = new ();

        public InteractionNetworkData(Func<bool> writePermission) : base(writePermission)
        {
        }
    }

    public sealed class GSObjectTransaction : GSObjectTransaction<InteractionNetworkData> { }

    [DisallowMultipleComponent]
    public abstract class GSObjectTransaction<TData> : GSInstanceBehaviour<TData> where TData : InteractionNetworkData
    {
        public NetObjectUid CurrentInteraction => Data.CurrentInteraction.Value;
        public NetObjectUid LastInteraction => Data.LastInteraction.Value;
        public bool InteractionsEnabled => Data.InteractionsEnabled.Value;

        private readonly Dictionary<NetObjectUid, Action<bool>> m_InteractionChangeRequestCallbacks = new ();
    
        private readonly NetWriter m_Writer = new();
        private readonly NetReader m_Reader = new();
    
        protected override void OnInitializeViewData()
        {
            base.OnInitializeViewData();
            if (IsLocal)
                SetInteractionsEnabledWithAuthority(true);
        }

        public void SetInteractionsEnabledWithAuthority(bool value) 
        {
            if (!IsLocal)
                throw new InvalidOperationException("No authority");

            Data.InteractionsEnabled.Set(value);
        }

        public void ChangeTransactionRequest(NetObjectUid sender, bool interactionState) =>
            ChangeTransactionRequest(sender, interactionState, _ => { });

        public void ChangeTransactionRequest(NetObjectUid sender, bool interactionState, Action<bool> callback)
        {
            if (m_InteractionChangeRequestCallbacks.TryGetValue(sender, out _))
            {
                Debug.LogError($"Callback already registered for transaction with object '{sender}' on '{gameObject.name}' and will be merged");
                callback(false);
                return;
            }

            if (!InteractionsEnabled || CurrentInteraction.IsValid && interactionState)
            {
                Debug.LogError($"Unable to perform transaction with object '{sender}' on '{gameObject.name}'. Current: {CurrentInteraction} Enabled: {InteractionsEnabled}");
                callback(false);
                return;
            }

            if (!Identity.World.TryGetObject(sender, out var senderState))
            {
                Debug.LogError($"Unable to perform transaction with object '{sender}' on '{gameObject.name}'. Object state '{sender}' not exists.");
                callback(false);
                return;
            }

            if (!senderState.IsOwnedByLocalPlayer)
            {
                Debug.LogError($"Unable to perform transaction with object '{sender}' on '{gameObject.name}'. Object state '{sender}' isn't owned by local player.");
                callback(false);
                return;
            }

            m_InteractionChangeRequestCallbacks.Add(sender, callback);

            m_Writer.SeekZero();
            m_Writer.WriteInstanceId(sender);
            m_Writer.WriteBool(interactionState);
            ToOwnerRpc(ChangeTransactionStateRequestRPC, data: m_Writer.ToArray());
        }

        [AutoRegisterRpcHandler]
        protected void ChangeTransactionStateRequestRPC(byte[] data)
        {
            m_Reader.Replace(data);
            var sender = m_Reader.ReadInstanceId();
            var state = m_Reader.ReadBoolean();

            var currentInteractionObject = Data.CurrentInteraction.Value;

            var canChangeInteractionState = 
                state ? 
                    currentInteractionObject.IsInvalid && Data.InteractionsEnabled.Value :
                    currentInteractionObject.Equals(sender);

            m_Writer.SeekZero();
            m_Writer.WriteInstanceId(sender);
            m_Writer.WriteBool(canChangeInteractionState);

            if (canChangeInteractionState)
            {
                Data.CurrentInteraction.Set(state ? sender : NetObjectUid.Invalid);
                if(state)
                    Data.LastInteraction.Set(sender);
            }

            OwnerToOthersRpc(ChangeTransactionResponseRPC, data: m_Writer.ToArray());
        }

        [AutoRegisterRpcHandler]
        protected void ChangeTransactionResponseRPC(byte[] data)
        {
            m_Reader.Replace(data);
            var sender = m_Reader.ReadInstanceId();
            var success = m_Reader.ReadBoolean();

            if (m_InteractionChangeRequestCallbacks.TryGetValue(sender, out var callback))
                callback(success);

            m_InteractionChangeRequestCallbacks.Remove(sender);
        }
    }
}