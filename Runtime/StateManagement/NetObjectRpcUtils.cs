using System;
using System.Collections.Generic;
using System.Reflection;

public static class RPCUtils
{
    private static readonly Dictionary<MethodInfo, bool> s_MethodValidityCache = new();
    private static readonly Dictionary<MethodInfo, int> s_RPCHashMap = new();

    /// <summary>
    /// Converts action to RPC code.
    /// </summary>
    /// <param name="a"></param>
    /// <returns></returns>
    internal static int ActionToHash(Action<byte[]> a)
    {
        if (!IsValidMethod(a.Method))
            throw new InvalidOperationException("Unable to create RPC code with anonymous delegates.");
        
        if (s_RPCHashMap.TryGetValue(a.Method, out var result))
            return result;

        result = NameToHash($"{a.Method.DeclaringType!.FullName}.{a.Method.Name}");
        s_RPCHashMap.Add(a.Method, result);
        return result;
    }

    private static bool IsValidMethod(MethodInfo method)
    {
        if (s_MethodValidityCache.TryGetValue(method, out var result)) 
            return result;
        
        // Anonymous delegate method name usually looks like '<Foo>m__0';
        // TODO: More 'beautiful' way to know is delegate is anonymous.
        s_MethodValidityCache.Add(method, result = !method.Name.StartsWith("<"));
        return result;
    }

    /// <summary>
    /// Calculates a stable hash code for the string.
    /// </summary>
    /// <param name="inputString"></param>
    private static int NameToHash(string inputString)
    {
        unchecked
        {
            var hash = inputString.Length + 32;
            for (var i = 0; i != inputString.Length; i++)
                hash = (hash << 5) - hash + inputString[i];

            return hash;
        }
    }
    
    /// <summary>
    /// Backwards slow version to get method info by the hash.
    /// May return the valid <see cref="MethodInfo"/>, if it was registered once.
    /// Should only be used for debug matters.
    /// </summary>
    public static MethodInfo TryFindMethodInfoByHashSlow(int hash)
    {
        foreach (var kvp in s_RPCHashMap)
        {
            if (kvp.Value == hash)
                return kvp.Key;
        }

        return null;
    }
}
