using GameSparks.RT;
using System;
using System.Collections.Generic;

public sealed class GSRPCUtils
{
    GSRPCUtils() { }
    /// <summary>
    /// Converts action to RPC code.
    /// </summary>
    /// <param name="a"></param>
    /// <returns></returns>
    public static string ActionToString(Action<RTData> a)
    {
        /// Anonymous delegate method name usually looks like '<Foo>m__0';
        /// TODO: Mo 'beautiful' way to know is delegate is anonymous.
        if (a.Method.Name.StartsWith("<"))
            throw new InvalidOperationException("Unable to create RPC code with anonymous delegates");

        return string.Format("{0}.{1}", a.Method.DeclaringType.FullName, a.Method.Name);
    }

    static Dictionary<string, int> s_RPCHashMap = new Dictionary<string, int>();

    public static string TryFindStringByHash(int hash)
    {
        foreach (var kvp in s_RPCHashMap)
            if (kvp.Value == hash)
                return kvp.Key;

        return null;
    }

    /// <summary>
    /// TODO: Make it private and not static;
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    public static int NameToHash(string s)
    {
        if (s == null)
            return 0;

        var hash = s.Length;
        if (s_RPCHashMap.TryGetValue(s, out hash))
            return hash;

        for (var i = 0; i != s.Length; ++i)
            hash = (hash << 5) - hash + s[i];

        s_RPCHashMap.Add(s, hash);
        return hash;
    }
}
