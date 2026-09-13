using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using DDrive.Editor.Inspectors;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEditor;
using UnityEngine;
using AssetSearch = DDrive.Editor.AssetSearch;

namespace DDrive.Editor.Dependencies
{
    // [11_tasks.md] 5-5 — 依存関係グラフ(収集/キャッシュ/差分更新)。5-6(使用箇所検索 / 未使用検出 /
    // 依存ツリー UI)・5-7(Preload 自動集計)・7-1(MissingAssetLog)がこの API を使う想定。
    //
    // 収集対象: 全 Data(.asset の AssetDataBase)・Prefab・Scene 内の AssetId<TMarker> / AssetRef フィールド。
    // 保存先: Library/DDriveDeps/(コミットしない。DependencyGraphCache 参照)。
    // 更新: RebuildAll() で全件、UpdatePaths() で差分(DependencyGraphPostprocessor が AssetPostprocessor から呼ぶ)。
    //
    // 実装メモ(2026-09-14、5-5): 起動時/ドメインリロード時の自動再構築はしない(全シーンの Open/Close は
    // 重く、デザイナーの作業を止めないという方針(CLAUDE.md §0-4)に反するため)。Library のキャッシュは
    // Unity 自身の再インポート検知(Editor 起動時に外部変更されたファイルを再インポートし
    // OnPostprocessAllAssets が呼ばれる)と DependencyGraphPostprocessor の差分更新に任せる。
    // 初回(Library が無い状態)は空なので、Tools > D-Drive > Generate > 依存関係グラフを再構築 を
    // 一度手動で実行する必要がある(要判断。docs/28 参照)。
    public static class DependencyGraphService
    {
        private static Dictionary<string, DependencyFileRecord> _byGuid;
        private static Dictionary<string, string> _guidByPath;
        private static Dictionary<(AssetType, ulong), List<DependencyReference>> _reverseIndex;

        // ── クエリ API(5-6/5-7 向け) ──

        // 「この ID を使っている場所一覧」
        public static IReadOnlyList<DependencyReference> FindUsages(AssetType type, ulong id)
        {
            EnsureLoaded();
            return _reverseIndex.TryGetValue((type, id), out var list)
                ? list
                : Array.Empty<DependencyReference>();
        }

        // 「このアセットが参照する ID 一覧」(assetPath は .asset/.prefab/.unity のいずれか)
        public static IReadOnlyList<DependencyReference> FindReferencesIn(string assetPath)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(assetPath) || !_guidByPath.TryGetValue(assetPath, out var guid)
                || !_byGuid.TryGetValue(guid, out var record))
            {
                return Array.Empty<DependencyReference>();
            }

            var result = new List<DependencyReference>(record.Edges.Count);
            foreach (var edge in record.Edges)
            {
                result.Add(ToReference(record.Path, edge));
            }

            return result;
        }

        // 「どこからも参照されない ID 一覧」。登録済み ID の全体は AssetIdLookup(1-8 で導入済みの列挙ロジック)を
        // 再利用して求める(このメソッド専用の列挙を新設しない)。
        public static IReadOnlyList<UnusedAssetId> FindUnusedIds()
        {
            EnsureLoaded();
            var result = new List<UnusedAssetId>();

            foreach (var (dataType, assetType) in AssetIdLookup.GetAllDefinitions())
            {
                foreach (var guid in AssetSearch.FindAssets("t:" + dataType.Name))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var asset = AssetDatabase.LoadAssetAtPath(path, dataType) as AssetDataBase;
                    if (asset == null || asset.GetType() != dataType || asset.Id == 0)
                    {
                        continue;
                    }

                    if (_reverseIndex.ContainsKey((assetType, asset.Id)))
                    {
                        continue;
                    }

                    var name = string.IsNullOrEmpty(asset.DisplayName)
                        ? Path.GetFileNameWithoutExtension(path)
                        : asset.DisplayName;
                    result.Add(new UnusedAssetId(assetType, asset.Id, path, name));
                }
            }

            return result;
        }

        public static int CachedFileCount
        {
            get
            {
                EnsureLoaded();
                return _byGuid.Count;
            }
        }

        public static int CachedEdgeCount
        {
            get
            {
                EnsureLoaded();
                var count = 0;
                foreach (var record in _byGuid.Values)
                {
                    count += record.Edges.Count;
                }

                return count;
            }
        }

        // ── 更新 API ──

        // 全再構築(Tools > D-Drive > Generate > 依存関係グラフを再構築、またはテストの前提づくり)。
        // 全 Prefab / Scene を開閉するため重い(プロジェクト規模次第で数秒〜数十秒)。自動実行しない。
        public static void RebuildAll()
        {
            DependencyGraphCache.ClearAll();
            _byGuid = new Dictionary<string, DependencyFileRecord>();
            _guidByPath = new Dictionary<string, string>();
            _reverseIndex = new Dictionary<(AssetType, ulong), List<DependencyReference>>();

            var fileCount = 0;

            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(AssetDataBase)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(path);
                if (asset == null)
                {
                    continue;
                }

                SaveAndIndex(BuildRecord(guid, path, DependencyGraphCollector.CollectFromDataAsset(asset)));
                fileCount++;
            }

            foreach (var guid in AssetSearch.FindAssets("t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                SaveAndIndex(BuildRecord(guid, path, DependencyGraphCollector.CollectFromPrefab(path)));
                fileCount++;
            }

            foreach (var guid in AssetSearch.FindAssets("t:Scene"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                SaveAndIndex(BuildRecord(guid, path, DependencyGraphCollector.CollectFromScene(path)));
                fileCount++;
            }

            Debug.Log($"[DDrive] DependencyGraph: 再構築完了(対象 {fileCount} ファイル, 参照 {CachedEdgeCount} 件)");
        }

        // 差分更新(DependencyGraphPostprocessor が AssetPostprocessor から呼ぶ。テストからも直接呼べる)。
        // changedPaths: 追加・変更・移動後のパス。deletedPaths: 削除・移動前のパス。
        public static void UpdatePaths(IReadOnlyList<string> changedPaths, IReadOnlyList<string> deletedPaths)
        {
            EnsureLoaded();

            if (deletedPaths != null)
            {
                foreach (var path in deletedPaths)
                {
                    if (!IsTargetPath(path))
                    {
                        continue;
                    }

                    if (_guidByPath.TryGetValue(path, out var guid) && _byGuid.TryGetValue(guid, out var existing))
                    {
                        Unindex(existing);
                        DependencyGraphCache.Delete(guid);
                    }
                }
            }

            if (changedPaths != null)
            {
                foreach (var path in changedPaths)
                {
                    if (!IsTargetPath(path))
                    {
                        continue;
                    }

                    var guid = AssetDatabase.AssetPathToGUID(path);
                    if (string.IsNullOrEmpty(guid))
                    {
                        continue; // 既に削除済み等(削除は deletedPaths 側で扱う)
                    }

                    if (_byGuid.TryGetValue(guid, out var previous))
                    {
                        Unindex(previous); // パス・中身が変わっている可能性があるため一旦外して作り直す
                    }

                    var edges = CollectEdgesFor(path, guid, out var isTarget);
                    if (!isTarget)
                    {
                        continue; // Data 以外の .asset(カタログ等)は対象外
                    }

                    SaveAndIndex(BuildRecord(guid, path, edges));
                }
            }
        }

        // テスト専用: プロセス内キャッシュを空にする(Library 上のファイルは変更しない。
        // 次回 EnsureLoaded で Library から読み直される)。実 Assets を伴わないため本番からは呼ばない。
        // public(NewAssetDialog.TestGameDataRootOverride と同じ理由: InternalsVisibleTo 未設定のため、
        // テスト asmdef から直接呼べるようにする)。
        public static void ResetInMemoryCacheForTests()
        {
            _byGuid = null;
            _guidByPath = null;
            _reverseIndex = null;
        }

        private static List<DependencyEdgeRecord> CollectEdgesFor(string path, string guid, out bool isTarget)
        {
            isTarget = true;

            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return DependencyGraphCollector.CollectFromPrefab(path);
            }

            if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                return DependencyGraphCollector.CollectFromScene(path);
            }

            if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(path);
                if (asset == null)
                {
                    DependencyGraphCache.Delete(guid); // 以前は Data だったが型が変わった等の後始末
                    isTarget = false;
                    return null;
                }

                return DependencyGraphCollector.CollectFromDataAsset(asset);
            }

            isTarget = false;
            return null;
        }

        private static bool IsTargetPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureLoaded()
        {
            if (_byGuid != null)
            {
                return;
            }

            _byGuid = new Dictionary<string, DependencyFileRecord>();
            _guidByPath = new Dictionary<string, string>();
            _reverseIndex = new Dictionary<(AssetType, ulong), List<DependencyReference>>();

            foreach (var record in DependencyGraphCache.LoadAll())
            {
                Index(record);
            }
        }

        private static DependencyFileRecord BuildRecord(string guid, string path, List<DependencyEdgeRecord> edges)
        {
            return new DependencyFileRecord
            {
                Guid = guid,
                Path = path,
                ContentHash = ComputeHash(path),
                Edges = edges ?? new List<DependencyEdgeRecord>(),
            };
        }

        private static void SaveAndIndex(DependencyFileRecord record)
        {
            DependencyGraphCache.Save(record);
            Index(record);
        }

        private static void Index(DependencyFileRecord record)
        {
            _byGuid[record.Guid] = record;
            _guidByPath[record.Path] = record.Guid;

            foreach (var edge in record.Edges)
            {
                var key = ((AssetType)edge.TargetType, edge.TargetId);
                if (!_reverseIndex.TryGetValue(key, out var list))
                {
                    list = new List<DependencyReference>();
                    _reverseIndex[key] = list;
                }

                list.Add(ToReference(record.Path, edge));
            }
        }

        private static void Unindex(DependencyFileRecord record)
        {
            _byGuid.Remove(record.Guid);
            _guidByPath.Remove(record.Path);

            foreach (var edge in record.Edges)
            {
                var key = ((AssetType)edge.TargetType, edge.TargetId);
                if (!_reverseIndex.TryGetValue(key, out var list))
                {
                    continue;
                }

                list.RemoveAll(r => r.SourcePath == record.Path
                    && r.ObjectPath == edge.ObjectPath
                    && r.PropertyPath == edge.PropertyPath);

                if (list.Count == 0)
                {
                    _reverseIndex.Remove(key);
                }
            }
        }

        private static DependencyReference ToReference(string sourcePath, DependencyEdgeRecord edge)
        {
            return new DependencyReference(sourcePath, edge.ObjectPath, edge.ComponentType, edge.PropertyPath, (AssetType)edge.TargetType, edge.TargetId);
        }

        // 変更検知用のコンテンツハッシュ(DependencyFileRecord.ContentHash に保存するのみで、現状の
        // 差分更新ロジックはこれを読まない。AssetPostprocessor が「変わったファイル」を教えてくれるため
        // 二重チェックは省いた。将来 Library が壊れた場合の健全性確認用に残す)。
        private static string ComputeHash(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (!File.Exists(full))
                {
                    return string.Empty;
                }

                using var md5 = MD5.Create();
                using var stream = File.OpenRead(full);
                var hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] DependencyGraph: ハッシュ計算に失敗しました({path}): {e.Message}");
                return string.Empty;
            }
        }
    }
}
