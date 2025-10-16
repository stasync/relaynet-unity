using System;
using System.Collections.Generic;

public interface IReadOnlyNetObjectRPCState
{
    IReadOnlyCollection<RPCState> PersistentBuffer { get; }
    IReadOnlyCollection<RPCState> TemporalBuffer { get; }

    NetObjectRPCStateSnapshot ToSnapshot();
    void ExecuteRemotely(bool receivePersistent, Action<int, byte[]> handler);
}

public struct RPCStateUIDAllocator
{
    public static uint AllocateUID() => ++m_Counter;
    private static uint m_Counter;
}

public readonly struct RPCState
{
    public readonly int Hash;
    public readonly uint UID;
    public readonly byte[] Payload;
    
    public bool IsValid => !Hash.Equals(0) && Payload != null;

    public RPCState(int hash, uint uid, byte[] payload)
    {
        Hash = hash;
        UID = uid;
        Payload = payload;
    }

    public RPCStateSnapshot ToSnapshot() => 
        RPCStateSnapshot.FromState(this);

    public static RPCState FromSnapshot(RPCStateSnapshot snapshot) => 
        new (snapshot.Hash, snapshot.UID, snapshot.Payload);

    public override int GetHashCode()
    {
        var hash = 13;
        hash = hash * 7 + Hash.GetHashCode();
        hash = hash * 7 + UID.GetHashCode();
        foreach (var d in Payload)
            hash = hash * 7 + d.GetHashCode();
        return hash;
    }
}

public sealed class NetObjectRPCState : IReadOnlyNetObjectRPCState
{
    private readonly List<RPCState> m_PersistentBuffer = new();
    public IReadOnlyCollection<RPCState> PersistentBuffer => m_PersistentBuffer;

    private readonly List<RPCState> m_TemporalBuffer = new();
    public IReadOnlyCollection<RPCState> TemporalBuffer => m_TemporalBuffer;

    private readonly HashSet<uint> m_ReceiveCollisions = new();

    public void AddOrUpdate(int hash, byte[] payload, bool persistent, RPCBufferChangeMethod changeMethod)
    {
        var uid = RPCStateUIDAllocator.AllocateUID();
        var data = new RPCState(hash, uid, payload);

        PushTo(m_TemporalBuffer, data, changeMethod);
        if (persistent)
            PushTo(m_PersistentBuffer, data, changeMethod);
    }

    private static void PushTo(List<RPCState> targetBuffer, RPCState newData, RPCBufferChangeMethod changeMethod)
    {
        if (changeMethod == RPCBufferChangeMethod.Replace)
            targetBuffer.RemoveAll(d => d.Hash == newData.Hash);

        targetBuffer.Add(newData);
    }

    public void ExecuteRemotely(bool executePersistent, Action<int, byte[]> handler)
    {
        m_ReceiveCollisions.Clear();

        if (executePersistent)
        {
            foreach (var rpcState in m_PersistentBuffer)
            {
                handler(rpcState.Hash, rpcState.Payload);
                m_ReceiveCollisions.Add(rpcState.UID);
            }
        }

        foreach (var rpcState in m_TemporalBuffer)
        {
            if (m_ReceiveCollisions.Contains(rpcState.UID))
                continue;

            handler(rpcState.Hash, rpcState.Payload);
            m_ReceiveCollisions.Add(rpcState.UID);
        }
    }

    public void ClearPersistent() =>
        m_PersistentBuffer.Clear();

    public void ClearTemporal() =>
        m_TemporalBuffer.Clear();

    public void Clear()
    {
        ClearPersistent();
        ClearTemporal();
    }

    public void RemovePersistent(int hashCode) =>
        m_PersistentBuffer.RemoveAll(d => d.Hash.Equals(hashCode));

    public void RestoreFormSnapshot(NetObjectRPCStateSnapshot snapshot)
    {
        m_PersistentBuffer.Clear();
        foreach (var rpcSnapshot in snapshot.PersistentBuffer)
            m_PersistentBuffer.Add(RPCState.FromSnapshot(rpcSnapshot));

        m_TemporalBuffer.Clear();
        foreach (var rpcSnapshot in snapshot.TemporalBuffer)
            m_TemporalBuffer.Add(RPCState.FromSnapshot(rpcSnapshot));
    }

    public NetObjectRPCStateSnapshot ToSnapshot() =>
        NetObjectRPCStateSnapshot.FromState(this);

    public override int GetHashCode()
    {
        var hash = 13;
        foreach (var rpc in m_PersistentBuffer)
            hash = hash * 7 + rpc.GetHashCode();
        foreach (var rpc in m_TemporalBuffer)
            hash = hash * 7 + rpc.GetHashCode();
        return hash;
    }
}
