public static class MessageCodes
{
    public const uint Min = 0;

    /// <summary>
    /// Send client info to other clients.
    /// </summary>
    public const uint PlayerStateUpdate = Min + 1;

    /// <summary>
    /// Create, destroy or update object.
    /// </summary>
    public const uint ObjectStateUpdate = Min + 2;

    /// <summary>
    /// Remote procedure call.
    /// </summary>
    public const uint Rpc = Min + 4;

    /// <summary>
    /// Generic custom message.
    /// </summary>
    public const uint CustomMessage = Min + 6;

    /// <summary>
    /// Master client session update.
    /// </summary>
    public const uint MasterClientSessionUpdate = Min + 7;


    public const uint WorldCommunication = Min + 1000;
}
