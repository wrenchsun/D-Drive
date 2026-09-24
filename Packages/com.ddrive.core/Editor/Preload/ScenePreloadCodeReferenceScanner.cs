using System;
using System.Collections.Generic;
using System.IO;
using DDrive.Editor.Codegen;
using DDrive.Editor.Dependencies;
using DDrive.Editor.Settings;
using DDrive.Foundation.Data;
using DDrive.Runtime.Loading;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Preload
{
    // [11_tasks.md] M-1b(2026-09-25) — ScenePreloadAggregator はシーン/Prefab の参照グラフからしか
    // 集計できないため、ゲームコードが生成 ID 定数(SEID.PlayerSlash 等)を直接呼ぶだけで、シーン/Prefab に
    // 一切参照が無い ID を見逃す(TeamNotes 2026-09-25「ScenePreloadList が ID 直呼びを拾えない」)。
    // AssetIdGenerator.CollectConstantEntries(プロジェクト内の全 Data から「定数名 → (Type, Id)」を
    // Regenerate() と同じ規則で再現したもの)と CodeReferenceScan.ScanFiles(5-6 のコード参照チェックと
    // 走査エンジンを共有)を組み合わせて、コード全文に定数名が出現するかを判定する。
    //
    // 誤検知/見逃しの扱い: 部分一致(サブ文字列)で判定するため、コメント・文字列リテラル内の出現も
    // ヒット扱いになる(誤検知の余地はあるが、Preload の既定を安全側に倒す目的なので「多めに Preload される」
    // 方が「参照されているのに Preload されない」より安全。CodeReferenceScan.FindPossibleReferences と同じ方針)。
    // 逆に、定数を変数に代入して間接的に使う・reflection 経由で組み立てる等の参照は見逃す(静的なテキスト走査の限界)。
    public static class ScenePreloadCodeReferenceScanner
    {
        // 純関数: ファイルテキスト群の中に各定数参照がいくつ出現するかを数える。IO を持たないため
        // EditMode テストで直接検証できる(実ファイルを用意しなくても文字列だけで判定ロジックを確認できる)。
        //
        // 判定方針(部分一致・コメント内・文字列内の扱い): コメント/文字列リテラル内かどうかは区別しない
        // (誤検知の余地はあるが見逃しを避ける安全側。CodeReferenceScan.FindPossibleReferences と同じ方針)。
        // ただし「単語境界」だけは見る: 前後が C# 識別子文字(英数字/アンダースコア)だと部分一致になり、
        // 例えば "SEID.PlayerSlash" が "SEID.PlayerSlashHeavy" にヒットしてしまう(逆に "SEID.PlayerSlash" が
        // 別の定数のプレフィックスとして誤検出される)ため、境界チェックで除外する
        // ("Foo.SEID.PlayerSlash" のような完全修飾名の後半一致は '.' が区切りなので許容される)。
        public static Dictionary<string, int> CountReferences(IReadOnlyList<string> constantReferences, IReadOnlyList<string> fileTexts)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (constantReferences == null || fileTexts == null)
            {
                return counts;
            }

            foreach (var pattern in constantReferences)
            {
                if (string.IsNullOrEmpty(pattern))
                {
                    continue;
                }

                var total = 0;
                foreach (var text in fileTexts)
                {
                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }

                    var index = 0;
                    while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
                    {
                        var matchEnd = index + pattern.Length;
                        var boundaryBefore = index == 0 || !IsIdentifierChar(text[index - 1]);
                        var boundaryAfter = matchEnd >= text.Length || !IsIdentifierChar(text[matchEnd]);
                        if (boundaryBefore && boundaryAfter)
                        {
                            total++;
                        }

                        index = matchEnd;
                    }
                }

                if (total > 0)
                {
                    counts[pattern] = total;
                }
            }

            return counts;
        }

        private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        public readonly struct ScanReport
        {
            public readonly List<PreloadEntry> Entries;
            public readonly int HitCount; // ヒットした「定数の種類数」(出現回数の合計ではない)

            public ScanReport(List<PreloadEntry> entries, int hitCount)
            {
                Entries = entries;
                HitCount = hitCount;
            }
        }

        // 実際のプロジェクト走査(IO)。DDriveProjectSettings.CodeScanRoot(既定 "Assets")を絶対パスにして
        // 走査し、GeneratedRoot 配下(定数の定義ファイル自身。素通しすると自明な自己参照になる)と
        // Packages/ 配下(D-Drive 自身を含む。[11_tasks.md] M-1b で明示的に対象外)を除外する。
        public static ScanReport ScanProject(bool includeTestAssemblies = false)
        {
            var constantEntries = AssetIdGenerator.CollectConstantEntries(includeTestAssemblies);
            if (constantEntries.Count == 0)
            {
                return new ScanReport(new List<PreloadEntry>(), 0);
            }

            var projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
            var scanRootAbsolute = $"{projectRoot}/{DDriveProjectSettings.instance.CodeScanRoot}".Replace('\\', '/');
            var generatedRootAbsolute = $"{projectRoot}/{DDriveProjectSettings.instance.GeneratedRoot}".Replace('\\', '/');

            var files = CodeReferenceScan.ScanFiles(new[] { scanRootAbsolute }, absolutePath =>
                absolutePath.StartsWith(generatedRootAbsolute, StringComparison.OrdinalIgnoreCase) ||
                absolutePath.Contains("/Packages/", StringComparison.OrdinalIgnoreCase));

            var texts = new List<string>(files.Count);
            foreach (var file in files)
            {
                texts.Add(file.Text);
            }

            var references = new List<string>(constantEntries.Count);
            foreach (var entry in constantEntries)
            {
                references.Add(entry.ConstantReference);
            }

            var counts = CountReferences(references, texts);
            var result = new List<PreloadEntry>();

            foreach (var entry in constantEntries)
            {
                if (!counts.ContainsKey(entry.ConstantReference))
                {
                    continue;
                }

                var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(entry.AssetPath);
                var displayName = DependencyAssetResolver.DisplayNameOrFileName(asset, entry.AssetPath);
                result.Add(new PreloadEntry(entry.Type, entry.Id, displayName));
            }

            return new ScanReport(result, counts.Count);
        }
    }
}
