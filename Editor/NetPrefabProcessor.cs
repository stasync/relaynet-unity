using System.Collections.Generic;
using Keel.RelayNet.GameObjects;
using UnityEditor;
using UnityEngine;

namespace Keel.RelayNet.Editor
{
    /// <summary>
    /// Keeps the <see cref="NetPrefabDatabase"/> (Assets/Resources/NetPrefabDatabase.asset) in sync with the project:
    /// whenever a prefab carrying a <see cref="NetObjectIdentity"/> is imported or deleted, this AssetPostprocessor adds or
    /// removes it, so the list never needs maintaining by hand. A prefab's id is a hash of its asset GUID, so it survives
    /// renames and moves.
    /// </summary>
    internal sealed class NetPrefabProcessor : AssetPostprocessor
    {
        private static NetPrefabDatabase s_prefabDatabase;
        private static readonly Dictionary<string, NetPrefabDatabase.SerializedData> s_prefabsListPath = new();

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (s_prefabDatabase == null)
            {
                s_prefabDatabase = NetPrefabDatabase.Editor.GetOrCreateNetworkPrefabs(out var newList, addAll: true);
                // A freshly created list already scanned every existing asset.
                if (newList)
                    return;
            }

            s_prefabsListPath.Clear();
            foreach (var prefabEntry in s_prefabDatabase._list)
            {
                var path = AssetDatabase.GetAssetPath(prefabEntry._prefab);
                if (!string.IsNullOrEmpty(path))
                    s_prefabsListPath.TryAdd(path, prefabEntry);
            }

            var markDirty = ProcessImportedAssets(importedAssets);
            markDirty |= ProcessDeletedAssets(deletedAssets);

            if (markDirty)
                EditorUtility.SetDirty(s_prefabDatabase);

            return;

            bool ProcessDeletedAssets(string[] assets)
            {
                var dirty = false;
                var deleted = new List<string>(assets);
                for (var i = s_prefabDatabase._list.Count - 1; i >= 0 && deleted.Count > 0; --i)
                {
                    GameObject prefab;
                    try
                    {
                        prefab = s_prefabDatabase._list[i]._prefab;
                    }
                    catch (MissingReferenceException)
                    {
                        s_prefabDatabase._list.RemoveAt(i);
                        dirty = true;
                        continue;
                    }

                    if (prefab == null)
                    {
                        s_prefabDatabase._list.RemoveAt(i);
                        dirty = true;
                    }
                    else
                    {
                        var path = AssetDatabase.GetAssetPath(prefab);
                        for (var j = assets.Length - 1; j >= 0; --j)
                        {
                            if (path != assets[j])
                                continue;

                            s_prefabDatabase._list.RemoveAt(i);
                            deleted.Remove(assets[j]);
                            dirty = true;
                        }
                    }
                }

                return dirty;
            }

            bool ProcessImportedAssets(string[] assets)
            {
                var dirty = false;
                foreach (var assetPath in assets)
                {
                    // Only care about GameObjects. (Can't use OnPostProcessPrefabs - it isn't called for moves/deletes.)
                    if (AssetDatabase.GetMainAssetTypeAtPath(assetPath) != typeof(GameObject))
                        continue;

                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    if (go != null && go.TryGetComponent<NetObjectIdentity>(out var identity))
                    {
                        // Avoid duplicating an entry we already track.
                        if (s_prefabsListPath.ContainsKey(assetPath))
                        {
                            if (s_prefabsListPath[assetPath]._prefab.GetHashCode() != go.GetHashCode())
                                s_prefabDatabase._list.Remove(s_prefabsListPath[assetPath]);
                            else
                                continue;
                        }

                        s_prefabDatabase._list.Add(new NetPrefabDatabase.SerializedData
                        {
                            _prefab = go,
                            _uid = NetPrefabDatabase.Editor.GetPrefabUid(identity)
                        });
                        dirty = true;
                    }
                }

                return dirty;
            }
        }
    }
}