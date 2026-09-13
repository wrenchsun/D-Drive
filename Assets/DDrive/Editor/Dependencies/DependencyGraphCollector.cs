using System;
using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DDrive.Editor.Dependencies
{
    // [11_tasks.md] 5-5 — 1 ファイル分の走査。テキストで .asset/.prefab/.unity をパースせず、
    // Unity API(SerializedObject)経由で読む(CLAUDE.md の設計意図に合わせる。要判断は docs/28 参照)。
    // 例外で個々のコンポーネント/ファイルの走査に失敗しても全体を止めない(警告 + スキップ)。
    internal static class DependencyGraphCollector
    {
        // AssetId<TMarker> の SerializedProperty.type は総称引数を含まない "AssetId`1" になる
        // (AssetIdDrawer と同じ実測値。2026-09-14 確認)。
        private const string AssetIdPropertyType = "AssetId`1";
        private const string AssetRefPropertyType = "AssetRef";

        public static List<DependencyEdgeRecord> CollectFromDataAsset(AssetDataBase asset)
        {
            var edges = new List<DependencyEdgeRecord>();
            if (asset == null)
            {
                return edges;
            }

            try
            {
                var so = new SerializedObject(asset);
                WalkProperties(so, string.Empty, asset.GetType().Name, edges);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] DependencyGraph: {AssetDatabase.GetAssetPath(asset)} を走査できませんでした: {e.Message}");
            }

            return edges;
        }

        public static List<DependencyEdgeRecord> CollectFromPrefab(string path)
        {
            var edges = new List<DependencyEdgeRecord>();

            GameObject root;
            try
            {
                root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] DependencyGraph: Prefab を読み込めませんでした({path}): {e.Message}");
                return edges;
            }

            if (root == null)
            {
                return edges;
            }

            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue; // Missing script(CLAUDE.md §0-4: 例外で止めない)
                }

                try
                {
                    var objectPath = TransformPath.GetRelative(root.transform, component.transform);
                    var so = new SerializedObject(component);
                    WalkProperties(so, objectPath, component.GetType().Name, edges);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DDrive] DependencyGraph: {path} の {component.GetType().Name} を走査できませんでした: {e.Message}");
                }
            }

            return edges;
        }

        // 現在開いている / 変更中のシーンには触れない。対象シーンを Additive で開いて読み終えたら必ず閉じる。
        public static List<DependencyEdgeRecord> CollectFromScene(string path)
        {
            var edges = new List<DependencyEdgeRecord>();

            UnityEngine.SceneManagement.Scene scene = default;
            var opened = false;
            try
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                opened = scene.IsValid();

                if (opened)
                {
                    foreach (var rootGo in scene.GetRootGameObjects())
                    {
                        CollectFromGameObjectTree(rootGo, edges, path);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] DependencyGraph: シーンを開けませんでした({path}): {e.Message}");
            }
            finally
            {
                if (opened)
                {
                    try
                    {
                        EditorSceneManager.CloseScene(scene, removeScene: true);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[DDrive] DependencyGraph: シーンを閉じられませんでした({path}): {e.Message}");
                    }
                }
            }

            return edges;
        }

        private static void CollectFromGameObjectTree(GameObject rootGo, List<DependencyEdgeRecord> edges, string scenePath)
        {
            foreach (var component in rootGo.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue;
                }

                try
                {
                    var relative = TransformPath.GetRelative(rootGo.transform, component.transform);
                    var objectPath = string.IsNullOrEmpty(relative) ? rootGo.name : $"{rootGo.name}/{relative}";
                    var so = new SerializedObject(component);
                    WalkProperties(so, objectPath, component.GetType().Name, edges);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DDrive] DependencyGraph: {scenePath} の {component.GetType().Name} を走査できませんでした: {e.Message}");
                }
            }
        }

        // SerializedObject を先頭から深さ優先で全走査する(配列・入れ子の Serializable クラス・
        // [SerializeReference] による多態も NextVisible が自動的にたどるため、型ごとの特別扱いは不要)。
        private static void WalkProperties(SerializedObject so, string objectPath, string componentType, List<DependencyEdgeRecord> edges)
        {
            var prop = so.GetIterator();
            var enterChildren = true;

            while (prop.NextVisible(enterChildren))
            {
                enterChildren = true;

                if (prop.propertyType != SerializedPropertyType.Generic)
                {
                    continue;
                }

                if (prop.type == AssetIdPropertyType)
                {
                    TryAddEdge(prop, "value", "type", objectPath, componentType, edges);
                }
                else if (prop.type == AssetRefPropertyType)
                {
                    TryAddEdge(prop, "Id", "Type", objectPath, componentType, edges);
                }
            }
        }

        private static void TryAddEdge(SerializedProperty prop, string idField, string typeField, string objectPath, string componentType, List<DependencyEdgeRecord> edges)
        {
            var idProp = prop.FindPropertyRelative(idField);
            var typeProp = prop.FindPropertyRelative(typeField);
            if (idProp == null || typeProp == null)
            {
                return;
            }

            var id = idProp.ulongValue;
            if (id == 0)
            {
                return; // 未設定(<None>)は依存として扱わない
            }

            edges.Add(new DependencyEdgeRecord
            {
                ObjectPath = objectPath,
                ComponentType = componentType,
                PropertyPath = prop.propertyPath,
                TargetType = typeProp.intValue,
                TargetId = id,
            });
        }
    }
}
