namespace Keel.RelayNet.Debug.Editor
{
    public static class NetworkEditorConfiguration
    {
        [UnityEditor.MenuItem("Tools/RelayNet/Disable debug")]
        public static void DisableNetworkDebug() =>
            ScriptingDefineSymbols.SetEnabled(symbolName: "NETWORK_DEBUG", false);

        [UnityEditor.MenuItem("Tools/RelayNet/Disable debug", isValidateFunction: true)]
        private static bool DisableNetworkDebugValidate() =>
            ScriptingDefineSymbols.Contains(symbolName: "NETWORK_DEBUG");

        [UnityEditor.MenuItem("Tools/RelayNet/Enable debug")]
        public static void EnableNetworkDebug() =>
            ScriptingDefineSymbols.SetEnabled(symbolName: "NETWORK_DEBUG", true);

        [UnityEditor.MenuItem("Tools/RelayNet/Enable debug", isValidateFunction: true)]
        static bool EnableNetworkDebugValidate() =>
            !ScriptingDefineSymbols.Contains(symbolName: "NETWORK_DEBUG");
    }
}