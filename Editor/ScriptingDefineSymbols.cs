using System.Collections.Generic;
using UnityEditor;

namespace Keel.RelayNet.Debug.Editor
{
    /// <summary>
    /// Toggles a scripting define symbol for the Standalone, Android and iOS build targets at once.
    /// </summary>
    internal static class ScriptingDefineSymbols
    {
        public static bool Contains(string symbolName) =>
            GetSymbolsList(BuildTargetGroup.Standalone).Contains(symbolName);

        public static void SetEnabled(string symbolName, bool value)
        {
            // Every group keeps its own defines - only this one symbol is added or removed.
            foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android, BuildTargetGroup.iOS })
            {
                var defines = GetSymbolsList(group);
                if (value)
                {
                    if (defines.Contains(symbolName))
                        continue;

                    defines.Add(symbolName);
                }
                else
                {
                    if (!defines.Contains(symbolName))
                        continue;

                    while (defines.Contains(symbolName))
                        defines.Remove(symbolName);
                }

                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", defines.ToArray()));
            }
        }

        private static List<string> GetSymbolsList(BuildTargetGroup group) =>
            new(collection: PlayerSettings.GetScriptingDefineSymbolsForGroup(group).Split(';'));
    }
}
