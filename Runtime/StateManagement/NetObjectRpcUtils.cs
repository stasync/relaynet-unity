using System;
using System.Collections.Generic;

public static class RPCUtils
{
    /// <summary>
    /// Converts action to RPC code.
    /// </summary>
    /// <param name="a"></param>
    /// <returns></returns>
    public static string ActionToString(Action<byte[]> a)
    {
        // Anonymous delegate method name usually looks like '<Foo>m__0';
        // TODO: More 'beautiful' way to know is delegate is anonymous.
        if (a.Method.Name.StartsWith("<"))
            throw new InvalidOperationException("Unable to create RPC code with anonymous delegates");

        return $"{a.Method.DeclaringType!.FullName}.{a.Method.Name}";
    }

    static readonly Dictionary<string, int> s_RPCHashMap = new();

    public static string TryFindStringByHash(int hash)
    {
        foreach (var kvp in s_RPCHashMap)
            if (kvp.Value == hash)
                return kvp.Key;

        return string.Empty;
    }

    /// <summary>
    /// TODO: Make it private and not static;
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    public static int NameToHash(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return 0;

        if (s_RPCHashMap.TryGetValue(s, out var hash))
            return hash;

        hash = s.Length;
        for (var i = 0; i != s.Length; ++i)
            hash = (hash << 5) - hash + s[i];

        s_RPCHashMap.Add(s, hash);
        return hash;
    }
}
