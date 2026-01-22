namespace LROTech.Network.StateManagement
{
    public enum NetWorldRuntimeChangeOp
    {
        PlayerStateUpdate,
        ObjectStateUpdate,
        Rpc,
        WorldCommunication,
        WorldObjectOwnerValidationRequest
    }
}