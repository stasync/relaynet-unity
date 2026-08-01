using UnityEngine.Scripting;

namespace LROTech.Network.StateManagement
{
    [RequireDerived]
    public abstract class NetObjectOwnershipDistributionRule
    {
        public abstract bool CanRequestOwnership(NetObjectState state);
    }
}