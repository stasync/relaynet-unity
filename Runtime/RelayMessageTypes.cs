namespace Keel.RelayNet
{
    /// <summary>
    /// Wire codes of the messages sent by a client to the relay server.
    /// </summary>
    internal enum RelayClientMessageType : byte
    {
        OnAuth,
        OnPacket,
    }

    /// <summary>
    /// Wire codes of the messages sent by the relay server back to the clients.
    /// </summary>
    internal enum RelayServerMessageType : byte
    {
        OnJoined,
        OnPacket,
        OnDisconnect,
    }
}
