using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GSRPC
{
    /*static Dictionary<string, int> s_RPCHashMap = new Dictionary<string, int>();

    int RPCNameToHash(string s)
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
    }*/
}
