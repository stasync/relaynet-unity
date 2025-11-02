namespace LROTech.Network.Editor
{
    public static class NetworkEditorConfiguration
    {
        [UnityEditor.MenuItem("System/Network/Disable debug")]
        public static void DisableNetworkDebug() =>
            Core.Editor.EditorScriptingDefineSymbolsManager.SetEnabled(symbolName: "NETWORK_DEBUG", false);

        [UnityEditor.MenuItem("System/Network/Disable debug", isValidateFunction: true)]
        private static bool DisableNetworkDebugValidate() =>
            Core.Editor.EditorScriptingDefineSymbolsManager.Contains(symbolName: "NETWORK_DEBUG");

        [UnityEditor.MenuItem("System/Network/Enable debug")]
        public static void EnableNetworkDebug() =>
            Core.Editor.EditorScriptingDefineSymbolsManager.SetEnabled(symbolName: "NETWORK_DEBUG", true);

        [UnityEditor.MenuItem("System/Network/Enable debug", isValidateFunction: true)]
        static bool EnableNetworkDebugValidate() =>
            !Core.Editor.EditorScriptingDefineSymbolsManager.Contains(symbolName: "NETWORK_DEBUG");
    }
}