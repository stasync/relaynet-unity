using Core.Networking;
using System;
using System.Collections.Generic;
using UnityEngine;

public class InteractionNetworkData : GSBehaviourAbstractData
{
    public SerializableObjectUID CurrentInteraction { get; private set; } = null;
    public SerializableObjectUID LastInteraction { get; private set; } = null;
    public SerializableBool InteractionsEnabled { get; private set; } = null;

    protected override void OnDefineValues()
    {
        CurrentInteraction = new SerializableObjectUID(NetObjectUID.Invalid, WritePemission);
        LastInteraction = new SerializableObjectUID(NetObjectUID.Invalid, WritePemission);
        InteractionsEnabled = new SerializableBool(false, WritePemission);
    }
}

public sealed class GSObjectTransaction : GSObjectTransaction<InteractionNetworkData> { }

[DisallowMultipleComponent]
public abstract class GSObjectTransaction<TData> : GSInstanceBehaviour<TData> where TData : InteractionNetworkData, new ()
{
    public NetObjectUID CurrentInteraction => Data.CurrentInteraction.Value;
    public NetObjectUID LastInteraction => Data.LastInteraction.Value;
    public bool InteractionsEnabled => Data.InteractionsEnabled.Value;

    Dictionary<NetObjectUID, Action<bool>> m_InteractionChangeRequestCallbacks = new Dictionary<NetObjectUID, Action<bool>>();

    public override void OnPostInitialize() 
    {
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
        }

        if (CurrentInteraction.IsValid || !InteractionsEnabled)
        {
            Debug.LogError($"Unable to perform transaction with object '{sender}' on '{gameObject.name}'. Current: {CurrentInteraction} Enabled: {InteractionsEnabled}");
            callback(false);
        }

        if(!Identity.World.TryGetObject(sender, out var state))
        {
            Debug.LogError($"Unable to perform transaction with object '{sender}' on '{gameObject.name}'. Object state '{sender}' not exists.");
            callback(false);
        }

        if (!state.IsOwnedByLocalPlayer)
        {
            Debug.LogError($"Unable to perform transaction with object '{sender}' on '{gameObject.name}'. Object state '{sender}' isn't owned by local player.");
            callback(false);
        }

        m_InteractionChangeRequestCallbacks.Add(sender, callback);

        var writer = new NetWriter();
        writer.WriteInstanceId(sender);
        writer.Write(interactionState);
        ToOwnerRPC(ChangeTransactionStateRequestRPC, writer.ToArray());
    }

    [AutoRegisterRPCHandler]
    protected void ChangeTransactionStateRequestRPC(byte[] data)
    {
        var reader = new NetReader(data);
        var sender = reader.ReadInstanceId();
        var state = reader.ReadBoolean();

        var currentInteractionObject = Data.CurrentInteraction.Value;

        var canChangeInteractionState = 
            state ? 
            currentInteractionObject.IsInvalid && Data.InteractionsEnabled.Value :
            currentInteractionObject.Equals(sender);

        var responseWriter = new NetWriter();
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
        var reader = new NetReader(data);
        var sender = reader.ReadInstanceId();
        var success = reader.ReadBoolean();

        if (m_InteractionChangeRequestCallbacks.TryGetValue(sender, out var callback))
            callback(success);

        m_InteractionChangeRequestCallbacks.Remove(sender);
    }
}
