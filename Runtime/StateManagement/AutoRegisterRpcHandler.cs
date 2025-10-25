using System;

namespace LROTech.Network.State
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AutoRegisterRPCHandler : Attribute
    {
    }
}
