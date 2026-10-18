namespace Keel.RelayNet.StateManagement
{
    /// <summary>
    /// Type of PRC that is being sent without authority.
    /// </summary>
    public enum NoAuthorityRpcType : byte
    {
        ToOwner = 0,
        ToMaster = 1,
        ToAll = 2
    }
}