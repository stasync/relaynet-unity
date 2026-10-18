using System;
using UnityEngine.Scripting;

namespace Keel.RelayNet.StateManagement
{
    [RequireAttributeUsages]
    [AttributeUsage(validOn: AttributeTargets.Method)]
    public sealed class AutoRegisterRpcHandler : Attribute
    {
    }
}