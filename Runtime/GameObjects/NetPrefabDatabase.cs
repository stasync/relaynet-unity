using System;
using System.Collections.Generic;
using UnityEngine;

namespace Keel.RelayNet.GameObjects
{
    /// <summary>
    /// A ScriptableObject holding a list of network prefabs. This is the serialized "source of
    /// truth" that ships with the project and can be shared between systems / managers.
    /// </summary>
    internal sealed class NetPrefabDatabase : ScriptableObject
    {
        [SerializeField] internal List<SerializedData> _list = new();
        private Dictionary<uint, GameObject> _prefabs;

        [Serializable]
        internal struct SerializedData : IEquatable<SerializedData>
        {
            [SerializeField] internal GameObject _prefab;
            [SerializeField] internal uint _uid;

            public bool Equals(SerializedData other) =>
                _prefab == other._prefab;
        }

        private static NetPrefabDatabase s_instance;

        private static void EnsureInstanceLoaded()
        {
            if (s_instance != null)
                return;

#if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isPlaying)
                Debug.LogWarning($"{nameof(NetPrefabDatabase)}.asset will be loaded synchronously in play mode. Consider using {nameof(LoadAsync)} instead.");
#endif
            s_instance = Resources.Load<NetPrefabDatabase>(path: nameof(NetPrefabDatabase));
            if (s_instance == null)
                throw new InvalidOperationException("Could not find NetworkPrefabDatabase.asset");
        }

        internal static ResourceRequest LoadAsync()
        {
            if (s_instance != null)
                return null;

            var resourceRequest = Resources.LoadAsync<NetPrefabDatabase>(path: nameof(NetPrefabDatabase));
            resourceRequest.completed += OnCompleted;
            return resourceRequest;

            void OnCompleted(AsyncOperation asyncOp)
            {
                asyncOp.completed -= OnCompleted;

                // Just a sanity check in case multiple requests are made at once.
                if (s_instance == null)
                    s_instance = resourceRequest.asset as NetPrefabDatabase;
            }
        }

        internal static bool TryGetPrefab(uint uid, out GameObject result)
        {
            EnsureInstanceLoaded();

            if (s_instance._prefabs == null || s_instance._prefabs.Count != s_instance._list.Count)
            {
                s_instance._prefabs = new Dictionary<uint, GameObject>(s_instance._list.Count);
                foreach (var data in s_instance._list)
                    s_instance._prefabs.TryAdd(data._uid, data._prefab);

#if UNITY_EDITOR
                if (s_instance._list.Count != s_instance._prefabs.Count)
                    Debug.LogError($"{nameof(NetPrefabDatabase)}.asset contains duplicate prefabs");
#endif
            }

            return s_instance._prefabs.TryGetValue(uid, out result);
        }

        internal static uint FindUidByGameObject(GameObject prefab)
        {
            EnsureInstanceLoaded();

            foreach (var serializedData in s_instance._list)
            {
                if (serializedData._prefab == prefab)
                    return serializedData._uid;
            }

            return 0;
        }

#if UNITY_EDITOR
        internal static class Editor
        {
            private static readonly string s_prefabDatabaseAssetPath = $"Assets/Resources/{nameof(NetPrefabDatabase)}.asset";

            public static NetPrefabDatabase GetOrCreateNetworkPrefabs(out bool isNew, bool addAll)
            {
                var defaultPrefabs = UnityEditor.AssetDatabase.LoadAssetAtPath<NetPrefabDatabase>(s_prefabDatabaseAssetPath);
                if (defaultPrefabs != null)
                {
                    isNew = false;
                    return defaultPrefabs;
                }

                isNew = true;
                defaultPrefabs = CreateInstance<NetPrefabDatabase>();
                EnsureParentFolderExists(s_prefabDatabaseAssetPath);
                UnityEditor.AssetDatabase.CreateAsset(defaultPrefabs, s_prefabDatabaseAssetPath);

                if (addAll)
                    defaultPrefabs._list = FindAll();

                UnityEditor.EditorUtility.SetDirty(defaultPrefabs);
                UnityEditor.AssetDatabase.SaveAssetIfDirty(defaultPrefabs);
                return defaultPrefabs;
            }

            private static void EnsureParentFolderExists(string assetPath)
            {
                var folder = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(folder) || UnityEditor.AssetDatabase.IsValidFolder(folder))
                    return;

                var parts = folder!.Split('/');
                var current = parts[0]; // "Assets"
                for (var i = 1; i < parts.Length; i++)
                {
                    var next = $"{current}/{parts[i]}";
                    if (!UnityEditor.AssetDatabase.IsValidFolder(next))
                        UnityEditor.AssetDatabase.CreateFolder(current, parts[i]);

                    current = next;
                }
            }

            private static List<SerializedData> FindAll()
            {
                var list = new List<SerializedData>();

                foreach (var guid in UnityEditor.AssetDatabase.FindAssets(filter: "t:GameObject"))
                {
                    var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

                    if (go != null && go.TryGetComponent<NetObjectIdentity>(out var identity))
                    {
                        list.Add(new SerializedData
                        {
                            _prefab = go,
                            _uid = GetPrefabUid(identity)
                        });
                    }
                }

                return list;
            }

            public static uint GetPrefabUid(NetObjectIdentity gameObjectIdentity)
            {
                var result = 0U;
                var targetGameObject = gameObjectIdentity.gameObject;

                if (targetGameObject.scene.IsValid())
                {
                    var sourceAssetObject = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(targetGameObject);
                    if (sourceAssetObject != null)
                    {
                        var guid = UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(sourceAssetObject));
                        if (!string.IsNullOrWhiteSpace(guid))
                            result = GetStableHashCode(guid);
                    }
                }
                else
                {
                    var guid = UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(targetGameObject));
                    result = GetStableHashCode(guid);
                }

                return result;
            }

            private static uint GetStableHashCode(string inputString)
            {
                unchecked
                {
                    var hash = (uint)inputString.Length + 32;
                    for (var i = 0; i != inputString.Length; i++)
                        hash = (hash << 5) - hash + inputString[i];

                    return hash;
                }
            }
        }
#endif
    }
}