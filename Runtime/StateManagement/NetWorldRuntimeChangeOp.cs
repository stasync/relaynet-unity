namespace Keel.RelayNet.StateManagement
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