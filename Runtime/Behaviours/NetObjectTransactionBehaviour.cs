using System;
using System.Collections.Generic;
using Core.Networking;
using LROTech.Network.GameObjects;
using LROTech.Network.StateManagement;
using LROTech.Network.StateManagement.Values;
using UnityEngine;

namespace LROTech.NetworkBehaviours
{
    [DisallowMultipleComponent]
    public sealed class NetObjectTransactionBehaviour : NetObjectBehaviour<NetObjectTransactionBehaviour.StateData>
    {
        public sealed class StateData : NetObjectStateData
        {
            public readonly NetworkValue<NetObjectUid> CurrentInteraction = new();
            public readonly NetworkValue<NetObjectUid> LastInteraction = new();
            public readonly NetworkValue<bool> InteractionsEnabled = new();
        }
        
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

        public void ChangeTransactionRequest(NetObjectUid sender, bool value) =>
            ChangeTransactionRequest(sender, value, _ => { });

        public void ChangeTransactionRequest(NetObjectUid sender, bool value, Action<bool> callback)
        {
            if (_interactionChangeRequestCallbacks.TryGetValue(sender, out _))
            {
                Debug.LogError($"Callback already registered for transaction with object '{sender}' on '{gameObject.name}' and will be merged");
                callback(false);
                return;
            }

            if (!Data.InteractionsEnabled.Value)
            {
                Debug.LogError($"Unable to perform transaction with object '{sender}' on '{gameObject.name}'. Enabled: '{Data.InteractionsEnabled.Value}'.");
                callback(false);
                return;
            }

            if (Data.CurrentInteraction.Value.IsValid && value)
            {
                // Validate current interaction sate.
                if (Identity.World.ObjectExists(Data.CurrentInteraction.Value))
                {
                    Debug.LogError($"Unable to perform transaction with object '{sender}' on '{gameObject.name}'. Current: '{Data.CurrentInteraction.Value}' Enabled: '{Data.InteractionsEnabled.Value}'.");
                    callback(false);
                    return;
                }
            }

            var senderState = Identity.World.GetObject(sender);
            if (senderState == null)
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

            _interactionChangeRequestCallbacks.Add(sender, callback);

            _writer.SeekZero();
            _writer.WriteNetObjectUid(sender);
            _writer.WriteBoolean(value);
            ToOwnerRpc(ChangeTransactionStateRequestRPC, data: _writer.ToArray());
        }

        [AutoRegisterRpcHandler]
        private void ChangeTransactionStateRequestRPC(byte[] data)
        {
            _reader.Replace(data);
            var sender = _reader.ReadNetObjectUid();
            var state = _reader.ReadBoolean();

            var currentInteractionObject = Data.CurrentInteraction.Value;

            var currentInteractionExist =
                currentInteractionObject.IsValid &&
                Identity.World.ObjectExists(currentInteractionObject);

            var canChangeInteractionState =
                state ? !currentInteractionExist && Data.InteractionsEnabled.Value : currentInteractionObject.Equals(sender);

            _writer.SeekZero();
            _writer.WriteNetObjectUid(sender);
            _writer.WriteBoolean(canChangeInteractionState);

            if (canChangeInteractionState)
            {
                Data.CurrentInteraction.Value = state ? sender : NetObjectUid.Invalid;
                if (state)
                    Data.LastInteraction.Value = sender;
            }

            OwnerToOthersRpc(ChangeTransactionResponseRPC, data: _writer.ToArray());
        }

        [AutoRegisterRpcHandler]
        private void ChangeTransactionResponseRPC(byte[] data)
        {
            _reader.Replace(data);
            var sender = _reader.ReadNetObjectUid();
            var success = _reader.ReadBoolean();

            if (_interactionChangeRequestCallbacks.TryGetValue(sender, out var callback))
                callback(success);

            _interactionChangeRequestCallbacks.Remove(sender);
        }

        private void Update()
        {
            if (IsLocal)
            {
                // Validating the current interaction object.
                // Update is a simple way to do so, yet not the smartest one.
                // Although, dictionary lookup only will only be the thing for local object.
                if (Data.CurrentInteraction.Value.IsValid && !Identity.World.ObjectExists(Data.CurrentInteraction.Value))
                    Data.CurrentInteraction.Value = NetObjectUid.Invalid;
            }
        }
    }
}