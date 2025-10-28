using System;

namespace LROTech.Network.StateManagement
{
    [AttributeUsage(validOn: AttributeTargets.Method)]
    public sealed class AutoRegisterRpcHandler : Attribute
    {
    }
}
