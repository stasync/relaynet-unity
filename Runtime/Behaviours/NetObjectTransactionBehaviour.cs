using System;
using System.Collections.Generic;
using Core.Networking;
using LROTech.Network.GameObjects;
using LROTech.Network.StateManagement;
using UnityEngine;

namespace LROTech.NetworkBehaviours
{
    public sealed class NetObjectTransactionBehaviour : NetObjectTransactionBehaviour<NetObjectTransactionBehaviour.TransactionNetworkData>
    {
        public class TransactionNetworkData : NetObjectStateData
        {
            public readonly NetworkValue<NetObjectUid> CurrentInteraction = new();
            public readonly NetworkValue<NetObjectUid> LastInteraction = new();
            public readonly NetworkValue<bool> InteractionsEnabled = new();
        }
    }

    [DisallowMultipleComponent]
    public abstract class NetObjectTransactionBehaviour<TData> : NetObjectBehaviour<TData>
        where TData : NetObjectTransactionBehaviour.TransactionNetworkData, new()
    {
        public NetObjectUid CurrentInteraction => Data.CurrentInteraction.Value;
        public NetObjectUid LastInteraction => Data.LastInteraction.Value;
        public bool InteractionsEnabled => Data.InteractionsEnabled.Value;

        private readonly Dictionary<NetObjectUid, Action<bool>> _interactionChangeRequestCallbacks = new();

        private readonly NetWriter _writer = new();
        private readonly NetReader _reader = new();

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

            Data.InteractionsEnabled.Value = value;
        }

        public void ChangeTransactionRequest(NetObjectUid sender, bool interactionState) =>
            ChangeTransactionRequest(sender, interactionState, _ => { });

        public void ChangeTransactionRequest(NetObjectUid sender, bool interactionState, Action<bool> callback)
        {
            if (_interactionChangeRequestCallbacks.TryGetValue(sender, out _))
            {
                Debug.LogError($"Callback already registered for transaction with object '{sender}' on '{gameObject.name}' and will be merged");
                callback(false);
                return;
            }

            if (!InteractionsEnabled || CurrentInteraction.IsValid && interactionState)
            {
                Debug.LogError(
                    $"Unable to perform transaction with object '{sender}' on '{gameObject.name}'. Current: {CurrentInteraction} Enabled: {InteractionsEnabled}");
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
                Debug.LogError(
                    $"Unable to perform transaction with object '{sender}' on '{gameObject.name}'. Object state '{sender}' isn't owned by local player.");
                callback(false);
                return;
            }

            _interactionChangeRequestCallbacks.Add(sender, callback);

            _writer.SeekZero();
            _writer.WriteNetObjectUid(sender);
            _writer.WriteBool(interactionState);
            ToOwnerRpc(ChangeTransactionStateRequestRPC, data: _writer.ToArray());
        }

        [AutoRegisterRpcHandler]
        protected void ChangeTransactionStateRequestRPC(byte[] data)
        {
            _reader.Replace(data);
            var sender = _reader.ReadNetObjectUid();
            var state = _reader.ReadBoolean();

            var currentInteractionObject = Data.CurrentInteraction.Value;

            var canChangeInteractionState =
                state ? currentInteractionObject.IsInvalid && Data.InteractionsEnabled.Value : currentInteractionObject.Equals(sender);

            _writer.SeekZero();
            _writer.WriteNetObjectUid(sender);
            _writer.WriteBool(canChangeInteractionState);

            if (canChangeInteractionState)
            {
                Data.CurrentInteraction.Value = state ? sender : NetObjectUid.Invalid;
                if (state)
                    Data.LastInteraction.Value = sender;
            }

            OwnerToOthersRpc(ChangeTransactionResponseRPC, data: _writer.ToArray());
        }

        [AutoRegisterRpcHandler]
        protected void ChangeTransactionResponseRPC(byte[] data)
        {
            _reader.Replace(data);
            var sender = _reader.ReadNetObjectUid();
            var success = _reader.ReadBoolean();

            if (_interactionChangeRequestCallbacks.TryGetValue(sender, out var callback))
                callback(success);

            _interactionChangeRequestCallbacks.Remove(sender);
        }
    }
}