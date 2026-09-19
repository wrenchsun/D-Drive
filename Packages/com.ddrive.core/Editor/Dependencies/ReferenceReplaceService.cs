using System.Collections.Generic;
using DDrive.Editor.Versioning;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Dependencies
{
    // [削除の確認画面(2026-09-14、UE の Delete Assets / Replace References 相当)] —
    // (Type,OldId) への参照を (Type,NewId) に書き換える。対象は Data(.asset)と Prefab のみ。
    // Scene 内の参照は自動で書き換えない([設計方針] 開いているシーンを勝手に保存しない)。
    // 呼び出し元(AssetDeleteWindow)は RemainingSceneUsages を「手動で直す」一覧として表示し、
    // Scene 参照が残っている間は削除を保留する。
    public readonly struct ReplacementPlan
    {
        public readonly AssetType Type;
        public readonly ulong OldId;
        public readonly ulong NewId;

        public ReplacementPlan(AssetType type, ulong oldId, ulong newId)
        {
            Type = type;
            OldId = oldId;
            NewId = newId;
        }
    }

    public sealed class ReferenceReplaceResult
    {
        public readonly List<string> ChangedDataPaths = new();
        public readonly List<string> ChangedPrefabPaths = new();
        public readonly List<DependencyReference> RemainingSceneUsages = new();
        public int ReplacedCount;
    }

    public static class ReferenceReplaceService
    {
        // Data 側は SerializedObject + Undo.RecordObject + SetDirty(CLAUDE.md §0-5)。Ctrl+Z で戻せる。
        // Prefab 側は PrefabUtility.LoadPrefabContents → 書き換え → SaveAsPrefabAsset → UnloadPrefabContents
        // (通常の Undo スタックには乗らない。呼び出し元の案内文言で明記すること。[31_phase5_decisions.md] 要判断)。
        public static ReferenceReplaceResult Replace(IReadOnlyList<ReplacementPlan> plans)
        {
            var result = new ReferenceReplaceResult();
            if (plans == null || plans.Count == 0)
            {
                return result;
            }

            var changedDataPaths = new HashSet<string>();
            var prefabEdits = new Dictionary<string, List<(DependencyReference usage, ReplacementPlan plan)>>();

            foreach (var plan in plans)
            {
                foreach (var usage in DependencyGraphService.FindUsages(plan.Type, plan.OldId))
                {
                    var kind = ClassifiedReference.ClassifyPath(usage.SourcePath);
                    switch (kind)
                    {
                        case ReferenceFileKind.Scene:
                            result.RemainingSceneUsages.Add(usage);
                            break;

                        case ReferenceFileKind.Data:
                            if (ReplaceInData(usage, plan))
                            {
                                changedDataPaths.Add(usage.SourcePath);
                                result.ReplacedCount++;
                            }
                            break;

                        case ReferenceFileKind.Prefab:
                            if (!prefabEdits.TryGetValue(usage.SourcePath, out var list))
                            {
                                list = new List<(DependencyReference, ReplacementPlan)>();
                                prefabEdits[usage.SourcePath] = list;
                            }

                            list.Add((usage, plan));
                            break;
                    }
                }
            }

            var changedPrefabPaths = new List<string>();
            foreach (var kv in prefabEdits)
            {
                var replaced = ReplaceInPrefab(kv.Key, kv.Value);
                if (replaced > 0)
                {
                    changedPrefabPaths.Add(kv.Key);
                    result.ReplacedCount += replaced;
                }
            }

            result.ChangedDataPaths.AddRange(changedDataPaths);
            result.ChangedPrefabPaths.AddRange(changedPrefabPaths);

            var allChanged = new List<string>(changedDataPaths);
            allChanged.AddRange(changedPrefabPaths);
            if (allChanged.Count > 0)
            {
                // [44_review_2026-09-19.md] P1-1: 実際に書き換えた Data だけを保存する(Prefab は
                // SaveAsPrefabAsset で既に書き込み済み)。参照差し替えは内容の変更そのものなので、
                // 対象の Version は通常どおり進む(他の無関係な実アセットは巻き込まない)。
                foreach (var path in changedDataPaths)
                {
                    DDriveAssetSave.SaveDirty(AssetDatabase.LoadAssetAtPath<AssetDataBase>(path));
                }

                DependencyGraphService.UpdatePaths(allChanged, null);
            }

            return result;
        }

        private static bool ReplaceInData(DependencyReference usage, ReplacementPlan plan)
        {
            var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(usage.SourcePath);
            if (asset == null)
            {
                return false;
            }

            var so = new SerializedObject(asset);
            var prop = so.FindProperty(usage.PropertyPath);
            if (prop == null || !DependencyGraphCollector.TryGetIdTypeFieldNames(prop, out var idField, out var typeField))
            {
                return false;
            }

            var idProp = prop.FindPropertyRelative(idField);
            var typeProp = prop.FindPropertyRelative(typeField);
            if (idProp == null || typeProp == null)
            {
                return false;
            }

            Undo.RecordObject(asset, "D-Drive: 参照を差し替え");
            idProp.ulongValue = plan.NewId;
            typeProp.intValue = (int)plan.Type;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            return true;
        }

        // 戻り値: 実際に書き換えた件数(0 なら SaveAsPrefabAsset を呼ばない = 無駄な再インポートを避ける)。
        private static int ReplaceInPrefab(string path, List<(DependencyReference usage, ReplacementPlan plan)> edits)
        {
            GameObject root;
            try
            {
                root = PrefabUtility.LoadPrefabContents(path);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[DDrive] ReferenceReplaceService: Prefab を読み込めませんでした({path}): {e.Message}");
                return 0;
            }

            if (root == null)
            {
                return 0;
            }

            var changed = 0;
            try
            {
                foreach (var (usage, plan) in edits)
                {
                    var target = string.IsNullOrEmpty(usage.ObjectPath) ? root : FindChild(root, usage.ObjectPath);
                    if (target == null)
                    {
                        Debug.LogWarning($"[DDrive] ReferenceReplaceService: {path} 内に '{usage.ObjectPath}' が見つかりませんでした。");
                        continue;
                    }

                    Component component = null;
                    foreach (var c in target.GetComponents<Component>())
                    {
                        if (c != null && c.GetType().Name == usage.ComponentType)
                        {
                            component = c;
                            break;
                        }
                    }

                    if (component == null)
                    {
                        continue;
                    }

                    var so = new SerializedObject(component);
                    var prop = so.FindProperty(usage.PropertyPath);
                    if (prop == null || !DependencyGraphCollector.TryGetIdTypeFieldNames(prop, out var idField, out var typeField))
                    {
                        continue;
                    }

                    var idProp = prop.FindPropertyRelative(idField);
                    var typeProp = prop.FindPropertyRelative(typeField);
                    if (idProp == null || typeProp == null)
                    {
                        continue;
                    }

                    idProp.ulongValue = plan.NewId;
                    typeProp.intValue = (int)plan.Type;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    changed++;
                }

                if (changed > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[DDrive] ReferenceReplaceService: {path} の書き換えに失敗しました: {e.Message}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return changed;
        }

        private static GameObject FindChild(GameObject root, string objectPath)
        {
            var found = root.transform.Find(objectPath);
            return found != null ? found.gameObject : null;
        }
    }
}
