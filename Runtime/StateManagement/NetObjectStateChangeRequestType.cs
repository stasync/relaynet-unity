namespace LROTech.Network.StateManagement
{
    public enum NetObjectStateChangeRequestType : byte
    {
        None,
        UnreliableUpdate,
        ReliableUpdate,
        ReliableUpdateOwnerChange,
        ReliableInitializationUpdate,
        ReliableRemoveUpdateSoft,
        ReliableRemoveUpdateHard
    }
}