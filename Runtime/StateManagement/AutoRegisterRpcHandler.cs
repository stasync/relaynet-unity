using System;

namespace LROTech.Network.StateManagment
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AutoRegisterRPCHandler : Attribute
    {
    }
}
