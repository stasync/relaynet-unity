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

    private readonly Dictionary<NetObjectUID, Action<bool>> m_InteractionChangeRequestCallbacks = new ();
    
    private readonly NetWriter m_Writer = new();
    private readonly NetReader m_Reader = new();
    
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

    public void ChangeTransactionRequest(NetObjectUID sender, bool interactionState) =>
        ChangeTransactionRequest(sender, interactionState, _ => { });

    public void ChangeTransactionRequest(NetObjectUID sender, bool interactionState, Action<bool> callback)
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
        ToOwnerRPC(ChangeTransactionStateRequestRPC, data: m_Writer.ToArray());
    }

    [AutoRegisterRPCHandler]
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
            Data.CurrentInteraction.Set(state ? sender : NetObjectUID.Invalid);
            if(state)
                Data.LastInteraction.Set(sender);
        }

        OwnerToOthersRPC(ChangeTransactionResponseRPC, data: m_Writer.ToArray());
    }

    [AutoRegisterRPCHandler]
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
