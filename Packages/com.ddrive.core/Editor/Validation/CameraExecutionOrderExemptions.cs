using System;
using System.Collections.Generic;
using DDrive.Editor.Import;
using DDrive.Editor.Settings;
using DDrive.Foundation.Validation;
using UnityEngine;

namespace DDrive.Editor.Validation
{
    // [26_timeline.md] §4.6.5 契約 G-1 / [42_distribution.md] §5.9・§5.14 E-23(2026-10-06、P-15 確認 Q-4) —
    // 実行順の検査(CameraExecutionOrderValidator)から「カメラを読むだけ」の型を外すための拡張点。
    //
    // 背景: 契約 G-1 は「実行順 1000(Cutscene のカメラ適用)以上の LateUpdate でカメラを書くと Cutscene のカメラが効かない」。
    // 検査は D-Drive 以外の全ランタイムスクリプトの実効実行順だけを見るため、カメラを**読むだけ**で意図して 1000 より後に
    // 動くスクリプト(例: カメラ位置を見て補正するもの)も Warning になる。
    //
    // 属性方式にしない理由: 外部パッケージのランタイムは D-Drive を参照しない(D-Drive の属性を付けられない)。
    // 代わりに、外部パッケージの Editor アセンブリ(ブリッジ)がこのインターフェースを実装して宣言する(自動で発見される)。
    //
    // 発見: `ExtensionPointDiscovery`(IShaderConversionTableProvider 等と同じ規則。public・IsVisible・非 abstract・
    // 引数なしコンストラクタ・`DDrive.Tests*` 除外・型のフルネーム順)。インスタンスはドメインリロードまでキャッシュ。
    // GetExemptions() は検査のたびに呼ぶ(例外は実装ごとに隔離)。理由は必須(空の宣言は無効 + Warning)。
    // もう 1 つの宣言元として、プロジェクト設定(`DDriveProjectSettings.CameraExecutionOrderExemptions`)からも手動で除外できる。
    // 互換: DDrive.Editor の弱い互換面(Editor 契約。docs/42 §5.9 / §5.14 E-23)。追加のみ。
    public interface ICameraExecutionOrderExemptionProvider
    {
        // 実行順の検査から除外する型(理由は必須)。
        IEnumerable<CameraExecutionOrderExemption> GetExemptions();
    }

    // 除外する型 1 件 + 理由(必須)。型は System.Type か完全修飾型名(Namespace.Type。入れ子は Outer+Inner)で指定する。
    public readonly struct CameraExecutionOrderExemption
    {
        public readonly Type Type;
        public readonly string TypeName;
        public readonly string Reason;

        public CameraExecutionOrderExemption(Type type, string reason)
        {
            Type = type;
            TypeName = null;
            Reason = reason;
        }

        public CameraExecutionOrderExemption(string typeName, string reason)
        {
            Type = null;
            TypeName = typeName;
            Reason = reason;
        }
    }

    // 除外の解決(宣言元の統合・無効の判定)。純粋なロジックにして EditMode テストで固定する。
    public static class CameraExecutionOrderExemptions
    {
        public const string CodeExempted = "DD-CAMEXEC-EXEMPT";
        public const string CodeInvalid = "DD-CAMEXEC-EXEMPT-INVALID";
        public const string SettingsSource = "プロジェクト設定";

        public readonly struct Resolved
        {
            public readonly string FullName;
            public readonly string Reason;
            public readonly string Source;

            public Resolved(string fullName, string reason, string source)
            {
                FullName = fullName;
                Reason = reason;
                Source = source;
            }
        }

        public sealed class Resolution
        {
            public readonly List<Resolved> Valid = new();
            public readonly List<string> Problems = new();
            private HashSet<string> _names;
            private HashSet<string> _shortNames;

            // fullTypeName: 実スクリプトの Type.FullName。取れない(テストが短い名前だけ注入した)ときは typeName(短い名前)で照合する。
            public bool IsExempt(string fullTypeName, string typeName)
            {
                if (Valid.Count == 0)
                {
                    return false;
                }

                if (_names == null)
                {
                    _names = new HashSet<string>(StringComparer.Ordinal);
                    _shortNames = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var r in Valid)
                    {
                        _names.Add(r.FullName);
                        var dot = r.FullName.LastIndexOf('.');
                        _shortNames.Add(dot >= 0 ? r.FullName.Substring(dot + 1) : r.FullName);
                    }
                }

                return !string.IsNullOrEmpty(fullTypeName)
                    ? _names.Contains(Normalize(fullTypeName))
                    : !string.IsNullOrEmpty(typeName) && _shortNames.Contains(typeName);
            }

            // 検査の結果(無効 = Warning を 1 件ずつ、有効な除外があれば Info を 1 件)。除外が 1 つも無く無効も無いときは空。
            public IEnumerable<ValidationResult> ToResults()
            {
                foreach (var problem in Problems)
                {
                    yield return ValidationResult.Warning(problem, code: CodeInvalid);
                }

                if (Valid.Count == 0)
                {
                    yield break;
                }

                var parts = new List<string>(Valid.Count);
                foreach (var r in Valid)
                {
                    parts.Add($"{r.FullName}(理由: {r.Reason}。宣言元: {r.Source})");
                }

                yield return ValidationResult.Info(
                    $"実行順の検査から除外: {Valid.Count} 型 — {string.Join("、", parts)}。" +
                    "これらは「カメラを読むだけ」と宣言されており、実行順 1000 以上でも Warning を出しません([26_timeline.md] §4.6.5 契約 G-1)。",
                    code: CodeExempted);
            }
        }

        private static List<ICameraExecutionOrderExemptionProvider> _providers;

        // 発見結果を捨てる(テスト専用。テスト用ダミー提供口が static フラグで切り替わるため)。
        public static void ResetProviderCacheForTests() => _providers = null;

        // 実際の宣言元(外部の提供口 + プロジェクト設定)から解決する。
        public static Resolution ResolveCurrent()
        {
            _providers ??= ExtensionPointDiscovery.Instantiate<ICameraExecutionOrderExemptionProvider>();
            return Resolve(_providers, DDriveProjectSettings.instance.CameraExecutionOrderExemptions, TypeExistsInLoadedAssemblies);
        }

        // providers / settings から有効な除外を集める。同じ型が複数の宣言元にあれば先に並んだもの(提供口 → 設定の順)を使う。
        // 無効(理由が空・型が指定されていない・実在しない型名)は除外せず Problems に入れる(例外は投げない)。
        public static Resolution Resolve(
            IReadOnlyList<ICameraExecutionOrderExemptionProvider> providers,
            IReadOnlyList<CameraExecutionOrderExemptionEntry> settingsEntries,
            Func<string, bool> typeExists)
        {
            var resolution = new Resolution();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (providers != null)
            {
                foreach (var provider in providers)
                {
                    var source = provider.GetType().FullName;
                    try
                    {
                        var exemptions = provider.GetExemptions();
                        if (exemptions == null)
                        {
                            continue;
                        }

                        foreach (var e in exemptions)
                        {
                            var name = e.Type != null ? (e.Type.FullName ?? e.Type.Name) : e.TypeName;
                            Add(resolution, seen, name, e.Reason, source, e.Type != null, typeExists);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
            }

            if (settingsEntries != null)
            {
                for (var i = 0; i < settingsEntries.Count; i++)
                {
                    var entry = settingsEntries[i];
                    Add(resolution, seen, entry?.TypeName, entry?.Reason, $"{SettingsSource}(#{i + 1})", false, typeExists);
                }
            }

            return resolution;
        }

        private static void Add(
            Resolution resolution, HashSet<string> seen, string typeName, string reason, string source, bool isRealType, Func<string, bool> typeExists)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                resolution.Problems.Add($"実行順の検査の除外({source})に型が指定されていません(この除外は無効です)。");
                return;
            }

            var name = Normalize(typeName.Trim());
            if (string.IsNullOrWhiteSpace(reason))
            {
                resolution.Problems.Add($"実行順の検査の除外 '{name}'({source})に理由がありません。理由は必須です(この除外は無効です)。");
                return;
            }

            if (!isRealType && typeExists != null && !typeExists(typeName.Trim()))
            {
                resolution.Problems.Add($"実行順の検査の除外 '{name}'({source})の型が見つかりません。完全修飾名(名前空間付き)で指定してください(この除外は無効です)。");
                return;
            }

            if (seen.Add(name))
            {
                resolution.Valid.Add(new Resolved(name, reason.Trim(), source));
            }
        }

        // 入れ子の型の区切り(Outer+Inner)を '.' にそろえる(スクリプトの Type.FullName との照合用)。
        private static string Normalize(string fullName) => fullName.Replace('+', '.');

        public static bool TypeExistsInLoadedAssemblies(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.GetType(fullName, false) != null)
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    // 読み込めないアセンブリは無視する。
                }
            }

            return false;
        }
    }
}
