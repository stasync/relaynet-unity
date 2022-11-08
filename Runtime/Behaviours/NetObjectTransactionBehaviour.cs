using Core.Networking;
using System;
using System.Collections.Generic;
using UnityEngine;

public class InteractionNetworkData : NetObjectStateData
{
    public InteractionNetworkData(Func<bool> writePermission) : base(writePermission) { }

    public readonly SerializableObjectUID CurrentInteraction = new ();
    public readonly SerializableObjectUID LastInteraction = new ();
    public readonly SerializableBool InteractionsEnabled = new ();
}

public sealed class GSObjectTransaction : GSObjectTransaction<InteractionNetworkData> { }

[DisallowMultipleComponent]
public abstract class GSObjectTransaction<TData> : GSInstanceBehaviour<TData> where TData : InteractionNetworkData
{
    public NetObjectUID CurrentInteraction => Data.CurrentInteraction.Value;
    public NetObjectUID LastInteraction => Data.LastInteraction.Value;
    public bool InteractionsEnabled => Data.InteractionsEnabled.Value;

    Dictionary<NetObjectUID, Action<bool>> m_InteractionChangeRequestCallbacks = new ();

    public override void OnInitializeViewData()
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

    public void ChangeTransactrionRequest(NetObjectUID sender, bool interactionState) =>
        ChangeTransactrionRequest(sender, interactionState, success => { });

    public void ChangeTransactrionRequest(NetObjectUID sender, bool interactionState, Action<bool> callback)
    {
        if (m_InteractionChangeRequestCallbacks.TryGetValue(sender, out var callbackCollection))
        {
            Debug.LogError($"Callback already registerd for transaction with object '{sender}' on '{gameObject.name}' and will be merged");
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

        var writer = NetWriter.Get();
        writer.WriteInstanceId(sender);
        writer.Write(interactionState);
        ToOwnerRPC(ChangeTransactionStateRequestRPC, writer.ToArray());
    }

    [AutoRegisterRPCHandler]
    protected void ChangeTransactionStateRequestRPC(byte[] data)
    {
        var reader = NetReader.Get(data);
        var sender = reader.ReadInstanceId();
        var state = reader.ReadBoolean();

        var currentInteractionObject = Data.CurrentInteraction.Value;

        var canChangeInteractionState = 
            state ? 
            currentInteractionObject.IsInvalid && Data.InteractionsEnabled.Value :
            currentInteractionObject.Equals(sender);

        var responseWriter = NetWriter.Get();
        responseWriter.WriteInstanceId(sender);
        responseWriter.Write(canChangeInteractionState);

        if (canChangeInteractionState)
        {
            Data.CurrentInteraction.Set(state ? sender : NetObjectUID.Invalid);
            if(state)
                Data.LastInteraction.Set(sender);
        }

        OwnerToOthersRPC(ChangeTransactrionResponseRPC, responseWriter.ToArray());
    }

    [AutoRegisterRPCHandler]
    protected void ChangeTransactrionResponseRPC(byte[] data)
    {
        var reader = NetReader.Get(data);
        var sender = reader.ReadInstanceId();
        var success = reader.ReadBoolean();

        if (m_InteractionChangeRequestCallbacks.TryGetValue(sender, out var callback))
            callback(success);

        m_InteractionChangeRequestCallbacks.Remove(sender);
    }
}
