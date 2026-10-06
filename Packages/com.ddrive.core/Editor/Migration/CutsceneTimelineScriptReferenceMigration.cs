using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;

namespace DDrive.Editor.Migration
{
    // [26_timeline.md] 実装メモ(M-6、2026-10-06) / [42_distribution.md] §4.3 — Timeline(.playable)に保存された D-Drive の
    // トラック / マーカーのスクリプト参照を直すプロジェクトマイグレーション。
    //
    // 背景: v1.0.0〜v1.3.1 の D-Drive は、トラック / マーカー型の一部(CutsceneSignalTrack 等)を「クラス名と違う名前のファイル」に
    // 定義していた。Unity はそのような型に MonoScript を持てず、Timeline のサブアセットは
    // `m_Script: {fileID: 0}` + `m_EditorClassIdentifier: DDrive.Runtime:<名前空間>:<クラス名>` で保存される。
    // Editor は m_EditorClassIdentifier で型を解決するので動くが、Player(開発ビルド)は解決できず
    // 「The referenced script on this Behaviour is missing!」でトラック / マーカーが読み込まれない。
    // 型を同名ファイルへ分けた(M-6)後も、既存の .playable は `fileID: 0` のまま(再インポート・
    // ForceReserializeAssets・SerializedObject での再設定ではいずれも直らない = null のスクリプトのオブジェクトは
    // 「破棄済み」扱いで触れない)ため、このマイグレーションが YAML 内の参照行だけを MonoScript の GUID 参照へ書き換える。
    //
    // 規則: `m_Script: {fileID: 0}` の行の近く(同じオブジェクトの中)に `m_EditorClassIdentifier: DDrive.*:<名前空間>:<クラス名>` が
    // あり、その型の MonoScript が見つかるときだけ書き換える(他の行・他のアセンブリの型は触らない)。冪等(書き換え後は対象が無い)。
    //
    // [64_review_m6_2026-10-06.md] GF-R-01〜04 の対応: (1) IRepeatableProjectMigration — Id が記録済みでも旧形式が残っていれば再度計画に入る。
    // (2) 走査は Assets/ だけ(PackageCache は対象外)。(3) ファイル単位で続行し、失敗は MigrationContext.Warn でまとめて報告(失敗があれば Id を記録しない)。
    // (4) 開いて未保存の Timeline は書き換えない(先に保存してもらう)。
    public sealed class CutsceneTimelineScriptReferenceMigration : IProjectMigration, IRepeatableProjectMigration
    {
        public string Id => "cutscene-timeline-monoscript-v1";

        // GF-R-04 — 対象 .playable が開かれていて未保存の変更を持つかの判定。
        // **テスト専用の差し替え口で、契約ではない**(GF-R-15。持ち込み先・外部パッケージのコードは触らない。変更・削除は互換性の対象外)。
        // 既定: メインアセットが読み込み済みで、そのアセットかサブアセットのどれかが dirty。
        // 書き換え直後の SaveAssets がメモリ上の古い内容を書き戻して「適用済みなのに元に戻る」ことを避ける。
        public static Func<string, bool> IsDirtyProbe = DefaultIsDirty;

        // GF-R-03 — 走査の範囲は Assets/ だけ。Packages/ は PackageCache(read-only)や他パッケージのファイルを含むため対象外。
        private static bool IsTargetPath(string path)
            => path.StartsWith("Assets/", StringComparison.Ordinal)
               && path.EndsWith(".playable", StringComparison.OrdinalIgnoreCase);

        // GF-R-01(c) — Id が記録済みでも、旧形式の .playable が残っていれば未適用扱いにする(Runner.Plan が呼ぶ)。
        public bool HasPendingWork() => FindTargetPaths().Count > 0;

        // 書き換え対象の .playable のパス(ドライラン・テスト・ログ用。対象が無ければ空)。
        public static List<string> FindTargetPaths()
        {
            var result = new List<string>();
            var resolver = new ScriptGuidResolver();
            foreach (var guid in AssetDatabase.FindAssets("t:TimelineAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsTargetPath(path))
                {
                    continue;
                }

                var text = ReadText(path);
                if (text != null && Rewrite(text, resolver.Resolve, out _) != null)
                {
                    result.Add(path);
                }
            }

            return result;
        }

        public void Migrate(MigrationContext context)
        {
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:TimelineAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsTargetPath(path))
                {
                    paths.Add(path);
                }
            }

            MigratePaths(context, paths);
        }

        // 指定した .playable だけを書き換える(テスト・FindTargetPaths 用に Migrate から分けた)。
        // ファイル単位で続行する(GF-R-03): 1 件の失敗(read-only・ロック・未保存の変更)で全体を止めず、成功した分は必ず再インポートし、
        // 失敗は context.Warn(ファイルごとに 1 件 = WarningCount は失敗件数)で集め、最後のまとめは Note で報告する。失敗が 1 件でもあれば Runner は Id を記録しない(= 次回も計画に入る)。
        // 戻り値: 書き換えたファイル数。
        public static int MigratePaths(MigrationContext context, IEnumerable<string> paths)
        {
            var resolver = new ScriptGuidResolver();
            var changedPaths = new List<string>();
            var failed = 0;
            foreach (var path in paths)
            {
                try
                {
                    var text = ReadText(path);
                    if (text == null)
                    {
                        continue;
                    }

                    var rewritten = Rewrite(text, resolver.Resolve, out var count);
                    if (rewritten == null)
                    {
                        continue;
                    }

                    // GF-R-04 — 開いて未保存の Timeline は書き換えない(先に保存してもらう)。
                    if (IsDirtyProbe != null && IsDirtyProbe(path))
                    {
                        failed++;
                        context?.Warn($"{path}: 開いていて未保存の変更があるため書き換えませんでした。保存(または破棄)してから、もう一度マイグレーションを実行してください");
                        continue;
                    }

                    AssetDatabase.MakeEditable(path); // バージョン管理のロック(Perforce 等)。取れなくても書き込みを試みる
                    File.WriteAllText(Path.GetFullPath(path), rewritten, new UTF8Encoding(false));
                    changedPaths.Add(path);
                    context?.Note($"{path}: スクリプト参照を {count} 件、MonoScript の GUID 参照に書き換えました");
                }
                catch (Exception e)
                {
                    failed++;
                    context?.Warn($"{path}: 書き換えに失敗しました({e.GetType().Name}: {e.Message})。読み取り専用・ロック中でないか確認して、もう一度実行してください");
                }
            }

            foreach (var path in changedPaths)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            if (failed > 0)
            {
                context?.Note($"{failed} 件の .playable を書き換えられませんでした(書き換えた {changedPaths.Count} 件は反映済み)");
            }

            return changedPaths.Count;
        }

        private static bool DefaultIsDirty(string path)
        {
            try
            {
                if (!AssetDatabase.IsMainAssetAtPathLoaded(path))
                {
                    return false;
                }

                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (o != null && EditorUtility.IsDirty(o))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // 判定できなければ書き換えを進める(例外で止めない)
            }

            return false;
        }

        private static string ReadText(string assetPath)
        {
            try
            {
                return File.ReadAllText(Path.GetFullPath(assetPath));
            }
            catch (Exception)
            {
                return null; // 読めないファイルは対象外(例外で止めない)
            }
        }

        // YAML テキストの `m_Script: {fileID: 0}` を、直後(次の `--- ` までの数行)の `m_EditorClassIdentifier: DDrive.*:<ns>:<class>` から
        // 解決した GUID 参照に書き換える。1 件も書き換えなければ null。resolveGuid(assembly, "ns.class") は MonoScript の GUID(無ければ null)。
        public static string Rewrite(string yaml, Func<string, string, string> resolveGuid, out int count)
        {
            count = 0;
            if (string.IsNullOrEmpty(yaml) || yaml.IndexOf("m_Script: {fileID: 0}", StringComparison.Ordinal) < 0)
            {
                return null;
            }

            var nl = yaml.Contains("\r\n") ? "\r\n" : "\n";
            var lines = yaml.Split('\n');
            var cr = nl == "\r\n";
            for (var i = 0; i < lines.Length; i++)
            {
                var line = cr ? lines[i].TrimEnd('\r') : lines[i];
                if (line.Trim() != "m_Script: {fileID: 0}")
                {
                    continue;
                }

                for (var j = i + 1; j < lines.Length && j <= i + 6; j++)
                {
                    var next = cr ? lines[j].TrimEnd('\r') : lines[j];
                    if (next.StartsWith("--- ", StringComparison.Ordinal))
                    {
                        break;
                    }

                    var trimmed = next.Trim();
                    const string key = "m_EditorClassIdentifier: ";
                    if (!trimmed.StartsWith(key, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // 形式: `<アセンブリ>:<名前空間>:<クラス名>`(旧形式。Unity 6 が MonoScript ありで書く `<アセンブリ>::<フルネーム>` は名前空間の位置が空 = 3 つ目にフルネームを持つ)
                    var parts = trimmed.Substring(key.Length).Split(':');
                    if (parts.Length == 3 && parts[0].StartsWith("DDrive.", StringComparison.Ordinal) && parts[2].Length > 0)
                    {
                        var fullName = parts[1].Length > 0 ? parts[1] + "." + parts[2] : parts[2];
                        var guid = resolveGuid(parts[0], fullName);
                        if (!string.IsNullOrEmpty(guid))
                        {
                            var indent = line.Substring(0, line.IndexOf('m'));
                            lines[i] = indent + "m_Script: {fileID: 11500000, guid: " + guid + ", type: 3}" + (cr ? "\r" : string.Empty);
                            count++;
                        }
                    }

                    break;
                }
            }

            return count == 0 ? null : string.Join("\n", lines);
        }

        // (アセンブリ名, 型のフルネーム) → MonoScript の GUID。プロジェクト内の全 MonoScript を 1 回だけ走査して引く。
        private sealed class ScriptGuidResolver
        {
            private Dictionary<string, string> _byAssemblyAndName;

            public string Resolve(string assemblyName, string fullName)
            {
                if (_byAssemblyAndName == null)
                {
                    _byAssemblyAndName = new Dictionary<string, string>();
                    foreach (var guid in AssetDatabase.FindAssets("t:MonoScript"))
                    {
                        var scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                        if (!scriptPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // fileID 11500000 は .cs の MonoScript の値(DLL の型には使えない。GF-R-09)
                        }

                        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                        var type = script != null ? script.GetClass() : null;
                        if (type != null)
                        {
                            _byAssemblyAndName[type.Assembly.GetName().Name + "|" + type.FullName] = guid;
                        }
                    }
                }

                return _byAssemblyAndName.TryGetValue(assemblyName + "|" + fullName, out var g) ? g : null;
            }
        }
    }
}
