using System;
using UnityEngine.Scripting;

namespace LROTech.Network.StateManagement
{
    [RequireAttributeUsages]
    [AttributeUsage(validOn: AttributeTargets.Method)]
    public sealed class AutoRegisterRpcHandler : Attribute
    {
    }
}