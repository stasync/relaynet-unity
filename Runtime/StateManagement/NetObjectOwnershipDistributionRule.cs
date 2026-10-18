using UnityEngine.Scripting;

namespace Keel.RelayNet.StateManagement
{
    [RequireDerived]
    public abstract class NetObjectOwnershipDistributionRule
    {
        public abstract bool CanRequestOwnership(NetObjectState state);
    }
}