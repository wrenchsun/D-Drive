using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DDrive.Editor.Codegen;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Dependencies
{
    // [11_tasks.md] 5-6 — 「削除しようとしている ID の生成済み定数(SEID.PlayerSlash 等)がコードから
    // 使われているか」の簡易チェック。DependencyGraphService はデータ側(Data/Prefab/Scene)の参照しか
    // 追わないため、コード側の参照はこの grep ベースの best-effort な補助でしか警告できない
    // (要判断: docs/28 参照。誤検知/見逃しがあり得るので、削除を止めるのではなく確認ダイアログの文言で注意喚起するだけに留める)。
    // 2026-09-17(docs/41_phase6_review_2026-09-17.md P2-7): internal → public。
    // `DeleteExecutionResult.PerAssetResult.CodeReferenceHits` が `Hit` を公開フィールドで持つため、
    // テスト asmdef(InternalsVisibleTo 未設定)から結果を検証できるようにする
    // (`SpecDiffService.BuildExistingIndex` 等と同じ理由)。
    public static class CodeReferenceScan
    {
        private const int MaxHits = 5;

        // P5 レビュー対応(2026-09-14): 削除のたびに Assets 配下の全 .cs を同期で全文読み込んでいた
        // (安全な削除・依存ツリーからの一括削除で件数が増えるほど重くなる)。対策 2 点:
        //   - 走査対象を「自前コード」(Assets/DDrive・Assets/Generated)に限定する(TextMesh Pro 等の
        //     同梱サンプルコードは対象外。生成された ID 定数の利用箇所はこの 2 フォルダにしか無い前提)。
        //   - ファイル内容をこのクラスの static キャッシュ(更新時刻キー)に保持し、同じ Editor セッション内で
        //     複数回呼ばれても変更が無いファイルは再読み込みしない(Library を再構築しても消える程度の
        //     エディタ限定キャッシュなので Undo/永続化は不要)。
        private static readonly string[] ScanRoots = { "DDrive", "Generated" };
        private static readonly Dictionary<string, (DateTime writeTimeUtc, string text)> FileCache = new();

        // 見つからない/判定できない場合は null。見つかった場合は確認ダイアログにそのまま載せられる文言を返す。
        public static string FindPossibleReferences(AssetDataBase asset, string assetPath)
        {
            try
            {
                var pattern = BuildConstantReference(asset, assetPath);
                if (string.IsNullOrEmpty(pattern))
                {
                    return null;
                }

                var hits = new List<string>();
                var dataPath = Application.dataPath.Replace('\\', '/');

                foreach (var root in ScanRoots)
                {
                    var rootPath = Path.Combine(Application.dataPath, root);
                    if (!Directory.Exists(rootPath))
                    {
                        continue;
                    }

                    foreach (var file in Directory.GetFiles(rootPath, "*.cs", SearchOption.AllDirectories))
                    {
                        var normalized = file.Replace('\\', '/');
                        if (normalized.EndsWith("/Generated/AssetIds.g.cs", StringComparison.Ordinal))
                        {
                            continue; // 生成ファイル自身に定数が並ぶのは当然なので除外
                        }

                        if (!TryReadCached(normalized, out var text))
                        {
                            continue; // 読めないファイルはスキップ(CLAUDE.md §0-4)
                        }

                        if (text.IndexOf(pattern, StringComparison.Ordinal) >= 0)
                        {
                            var relative = "Assets" + normalized.Substring(dataPath.Length);
                            hits.Add(relative);
                            if (hits.Count >= MaxHits)
                            {
                                break;
                            }
                        }
                    }

                    if (hits.Count >= MaxHits)
                    {
                        break;
                    }
                }

                if (hits.Count == 0)
                {
                    return null;
                }

                return $"生成された ID 定数 '{pattern}' を参照しているコードが見つかりました({string.Join(", ", hits)} 等)。" +
                       "削除するとコンパイルエラーになる可能性があります(簡易チェックのため見逃し/誤検知の余地あり)。";
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] CodeReferenceScan: 走査に失敗しました: {e.Message}");
                return null;
            }
        }

        // 削除の確認画面(2026-09-14)の結果画面向け: ファイル:行 単位のヒット一覧(クリックでエディタを開く用)。
        // FindPossibleReferences と同じ走査(キャッシュ・対象フォルダ)を共有し、行番号だけ追加で数える。
        public readonly struct Hit
        {
            public readonly string RelativePath;
            public readonly int Line; // 1-based

            public Hit(string relativePath, int line)
            {
                RelativePath = relativePath;
                Line = line;
            }
        }

        public static List<Hit> FindPossibleReferenceHits(AssetDataBase asset, string assetPath)
        {
            var result = new List<Hit>();

            try
            {
                var pattern = BuildConstantReference(asset, assetPath);
                if (string.IsNullOrEmpty(pattern))
                {
                    return result;
                }

                var dataPath = Application.dataPath.Replace('\\', '/');

                foreach (var root in ScanRoots)
                {
                    var rootPath = Path.Combine(Application.dataPath, root);
                    if (!Directory.Exists(rootPath))
                    {
                        continue;
                    }

                    foreach (var file in Directory.GetFiles(rootPath, "*.cs", SearchOption.AllDirectories))
                    {
                        var normalized = file.Replace('\\', '/');
                        if (normalized.EndsWith("/Generated/AssetIds.g.cs", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (!TryReadCached(normalized, out var text))
                        {
                            continue;
                        }

                        if (text.IndexOf(pattern, StringComparison.Ordinal) < 0)
                        {
                            continue;
                        }

                        var relative = "Assets" + normalized.Substring(dataPath.Length);
                        var lines = text.Split('\n');
                        for (var i = 0; i < lines.Length; i++)
                        {
                            if (lines[i].IndexOf(pattern, StringComparison.Ordinal) >= 0)
                            {
                                result.Add(new Hit(relative, i + 1));
                                if (result.Count >= MaxHits)
                                {
                                    return result;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] CodeReferenceScan: 走査に失敗しました: {e.Message}");
            }

            return result;
        }

        private static bool TryReadCached(string normalizedPath, out string text)
        {
            try
            {
                var writeTime = File.GetLastWriteTimeUtc(normalizedPath);
                if (FileCache.TryGetValue(normalizedPath, out var cached) && cached.writeTimeUtc == writeTime)
                {
                    text = cached.text;
                    return true;
                }

                text = File.ReadAllText(normalizedPath);
                FileCache[normalizedPath] = (writeTime, text);
                return true;
            }
            catch (Exception)
            {
                text = null;
                return false;
            }
        }

        // "SEID.PlayerSlash" のような文字列を、AssetIdGenerator と同じ規則(ファイル名→定数名)で組み立てる。
        private static string BuildConstantReference(AssetDataBase asset, string assetPath)
        {
            var dataType = asset.GetType();
            AssetIdDefinitionAttribute attr = null;
            while (dataType != null && attr == null)
            {
                attr = dataType.GetCustomAttribute<AssetIdDefinitionAttribute>();
                dataType = dataType.BaseType;
            }

            if (attr == null || string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            var fileName = Path.GetFileNameWithoutExtension(assetPath);
            var constName = AssetIdGenerator.ToConstantName(fileName);
            return $"{attr.ConstantsClassName}.{constName}";
        }
    }
}
