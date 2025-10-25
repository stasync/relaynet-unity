using System;

namespace LROTech.Network.StateManagement
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AutoRegisterRPCHandler : Attribute
    {
    }
}
