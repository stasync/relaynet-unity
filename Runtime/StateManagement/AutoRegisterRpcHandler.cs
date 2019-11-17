using System;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AutoRegisterRPCHandler : Attribute
{
    public AutoRegisterRPCHandler() { }
}
