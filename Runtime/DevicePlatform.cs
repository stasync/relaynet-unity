namespace LROTech.Network
{
    /// <summary>
    /// The platform a player runs on, as reported by its own client.
    /// Sent over the wire as an int - the values must not change.
    /// </summary>
    public enum DevicePlatform
    {
        Unknown = -1,
        Standalone = 0,
        Android = 1,
        IOS = 2
    }
}
