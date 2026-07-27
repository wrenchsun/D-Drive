using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace DDrive.Editor.Validation
{
    // [00_requirements.md] §5 の禁止事項をソーステキストの静的走査で検出する。
    // 専用 Roslyn Analyzer への置き換えは将来の改善余地(現状はテキストパターン一致の簡易版)。
    public static class ForbiddenApiScanner
    {
        private sealed class Rule
        {
            public string Pattern;
            public string Message;
            public string[] AllowedFileSuffixes = Array.Empty<string>();
        }

        // 注意: このファイル自身の Message 文字列が各パターンにマッチしてしまうため、
        // 全ルールの許可リストに "ForbiddenApiScanner.cs" を含める(自己検出の偽陽性防止)。
        private static readonly Rule[] Rules =
        {
            new Rule
            {
                Pattern = @"\bTime\.(time|deltaTime|unscaledDeltaTime|timeAsDouble|unscaledTime)\b",
                Message = "UnityEngine.Time を直接参照しない。ITimeSource を使う([02_core_framework.md] §9.5)",
                AllowedFileSuffixes = new[] { "LocalTimeSource.cs", "NetworkTimeSource.cs", "GameLoopDriver.cs", "ForbiddenApiScanner.cs" },
            },
            new Rule
            {
                Pattern = @"\bResources\.Load\b",
                Message = "Resources.Load は禁止。Addressables 経由(IAssetLoader)を使う([00_requirements.md] §5)",
                AllowedFileSuffixes = new[] { "ForbiddenApiScanner.cs" },
            },
            new Rule
            {
                Pattern = @"\bAddressables\.Load\w*\b",
                Message = "Addressables への直接アクセスは禁止。IAssetLoader 経由にする([00_requirements.md] §5)",
                AllowedFileSuffixes = new[] { "AddressablesAssetLoader.cs", "ForbiddenApiScanner.cs" },
            },
            new Rule
            {
                // PreviewService: 非破壊ループ試聴のための ScriptableObject コピー(GameObject 生成ではない)を許可。
                Pattern = @"\b(?:UnityEngine\.)?Object\.Instantiate\s*\(|(?<![.\w])Instantiate\s*\(",
                Message = "Instantiate の直接呼び出しは禁止。PoolService 経由にする([00_requirements.md] §5)",
                AllowedFileSuffixes = new[] { "PoolService.cs", "PreviewService.cs", "ForbiddenApiScanner.cs" },
            },
            new Rule
            {
                Pattern = @"\bAudioSource\.Play\b",
                Message = "AudioSource.Play の直接呼び出しは禁止。AudioManager 経由にする([00_requirements.md] §5)",
                AllowedFileSuffixes = new[] { "ForbiddenApiScanner.cs" },
            },
        };

        public readonly struct Violation
        {
            public readonly string FilePath;
            public readonly int Line;
            public readonly string Message;

            public Violation(string filePath, int line, string message)
            {
                FilePath = filePath;
                Line = line;
                Message = message;
            }
        }

        public static List<Violation> Scan(string rootFolder)
        {
            var violations = new List<Violation>();

            if (!Directory.Exists(rootFolder))
            {
                return violations;
            }

            var files = Directory.GetFiles(rootFolder, "*.cs", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                var normalized = file.Replace('\\', '/');

                // Samples/ は製品コードの規約対象外(手動確認用のデモスクリプト置き場)。
                if (normalized.Contains("/Samples/"))
                {
                    continue;
                }

                var lines = File.ReadAllLines(normalized);

                for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    var line = lines[lineIndex];
                    if (line.TrimStart().StartsWith("//"))
                    {
                        continue;
                    }

                    foreach (var rule in Rules)
                    {
                        if (IsAllowedFile(normalized, rule.AllowedFileSuffixes))
                        {
                            continue;
                        }

                        if (Regex.IsMatch(line, rule.Pattern))
                        {
                            violations.Add(new Violation(normalized, lineIndex + 1, rule.Message));
                        }
                    }
                }
            }

            return violations;
        }

        private static bool IsAllowedFile(string path, string[] suffixes)
        {
            foreach (var suffix in suffixes)
            {
                if (path.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
