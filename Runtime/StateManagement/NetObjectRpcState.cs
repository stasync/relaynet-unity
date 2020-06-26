using System;
using System.Collections.Generic;

public interface IReadOnlyNetObjectRPCState
{
    IReadOnlyCollection<RPCState> PersistentBuffer { get; }
    IReadOnlyCollection<RPCState> TemporalBuffer { get; }

    NetObjectRPCStateSnapshot ToSnapshot();
    void Receive(bool receivePersistent, Action<int, byte[]> reciever);
}

public struct RPCStateUIDAllocator
{
    public static uint AllocateUID() => ++UIDCounter;

    static uint UIDCounter = 0;
}

public struct RPCState
{
    public int Hash { get; private set; }
    public uint UID { get; private set; }
    public byte[] Payload { get; private set; }

    public RPCState(int hash, uint uid, byte[] payload)
    {
        Hash = hash;
        UID = uid;
        Payload = payload;
    }

    public RPCStateSnapshot ToSnapshot() => RPCStateSnapshot.FromState(this);

    public static RPCState FromSnapshot(RPCStateSnapshot snapshot) => new RPCState(snapshot.Hash, snapshot.UID, snapshot.Payload);

    public override int GetHashCode()
    {
        var hash = 13;
        hash = (hash * 7) + Hash.GetHashCode();
        hash = (hash * 7) + UID.GetHashCode();
        foreach (var d in Payload)
            hash = (hash * 7) + d.GetHashCode();
        return hash;
    }
}

public class NetObjectRPCState : IReadOnlyNetObjectRPCState
{
    List<RPCState> m_PersistentBuffer = new List<RPCState>();
    public IReadOnlyCollection<RPCState> PersistentBuffer => m_PersistentBuffer;

    List<RPCState> m_TemporalBuffer = new List<RPCState>();
    public IReadOnlyCollection<RPCState> TemporalBuffer => m_TemporalBuffer;

    HashSet<uint> m_ReceiveCollisions = new HashSet<uint>();

    public void AddOrUpdate(int hash, byte[] payload, bool persistent, RPCBufferChangeMethod changeMethod)
    {
        var uid = RPCStateUIDAllocator.AllocateUID();
        var data = new RPCState(hash, uid, payload);

        PushTo(data, m_TemporalBuffer, changeMethod);
        if (persistent)
            PushTo(data, m_PersistentBuffer, changeMethod);
    }

    void PushTo(RPCState data, List<RPCState> targetBuffer, RPCBufferChangeMethod changeMethod)
    {
        if (changeMethod == RPCBufferChangeMethod.Replace)
            targetBuffer.RemoveAll(d => d.Hash.Equals(data.Hash));

        targetBuffer.Add(data);
    }

    public void Receive(bool receivePersistent, Action<int, byte[]> reciever)
    {
        m_ReceiveCollisions.Clear();

        if (receivePersistent)
            foreach (var d in m_PersistentBuffer)
            {
                reciever(d.Hash, d.Payload);
                m_ReceiveCollisions.Add(d.UID);
            }


        foreach (var d in m_TemporalBuffer)
        {
            if (m_ReceiveCollisions.Contains(d.UID))
                continue;

            reciever(d.Hash, d.Payload);
            m_ReceiveCollisions.Add(d.UID);
        }
    }

    public void ClearPersistent() => m_PersistentBuffer.Clear();
    public void ClearTemporal() => m_TemporalBuffer.Clear();

    public void Clear()
    {
        ClearPersistent();
        ClearTemporal();
    }

    public void RemovePersistent(int hashCode) => m_PersistentBuffer.RemoveAll(d => d.Hash.Equals(hashCode));

    public void RestoreFormSnapshot(NetObjectRPCStateSnapshot snapshot)
    {
        m_PersistentBuffer.Clear();
        foreach (var rpcSnapshot in snapshot.PersistentBuffer)
            m_PersistentBuffer.Add(RPCState.FromSnapshot(rpcSnapshot));

        m_TemporalBuffer.Clear();
        foreach (var rpcSnapshot in snapshot.TemporalBuffer)
            m_TemporalBuffer.Add(RPCState.FromSnapshot(rpcSnapshot));
    }

    public NetObjectRPCStateSnapshot ToSnapshot() => NetObjectRPCStateSnapshot.FromState(this);

    public override int GetHashCode()
    {
        var hash = 13;
        foreach (var rpc in m_PersistentBuffer)
            hash = (hash * 7) + rpc.GetHashCode();
        foreach (var rpc in m_TemporalBuffer)
            hash = (hash * 7) + rpc.GetHashCode();
        return hash;
    }
}
