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
            var defines = GetSymbolsList(BuildTargetGroup.Standalone);
            if (value)
            {
                if (!defines.Contains(symbolName))
                    defines.Add(symbolName);
            }
            else
            {
                if (!defines.Contains(symbolName))
                    return;

                while (defines.Contains(symbolName))
                    defines.Remove(symbolName);
            }

            var definesString = string.Join(";", defines.ToArray());
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, definesString);
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android, definesString);
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.iOS, definesString);
        }

        private static List<string> GetSymbolsList(BuildTargetGroup group) =>
            new(collection: PlayerSettings.GetScriptingDefineSymbolsForGroup(group).Split(';'));
    }
}
