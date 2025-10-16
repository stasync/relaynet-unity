public static class NetworkEditorConfiguration
{
#if UNITY_EDITOR
    [UnityEditor.MenuItem("System/Network/Disable debug")]
    public static void DisableNetworkDebug() => 
        Core.Editor.EditorScriptingDefineSymbolsManager.SetEnabled("NETWORK_DEBUG", false);

    [UnityEditor.MenuItem("System/Network/Disable debug", true)]
    private static bool DisableNetworkDebugValidate() => 
        Core.Editor.EditorScriptingDefineSymbolsManager.Contains("NETWORK_DEBUG");

    [UnityEditor.MenuItem("System/Network/Enable debug")]
    public static void EnableNetworkDebug() => 
        Core.Editor.EditorScriptingDefineSymbolsManager.SetEnabled("NETWORK_DEBUG", true);

    [UnityEditor.MenuItem("System/Network/Enable debug", true)]
    static bool EnableNetworkDebugValidate() => 
        !Core.Editor.EditorScriptingDefineSymbolsManager.Contains("NETWORK_DEBUG");
#endif
}