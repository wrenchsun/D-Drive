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
    public sealed class CutsceneTimelineScriptReferenceMigration : IProjectMigration
    {
        public string Id => "cutscene-timeline-monoscript-v1";

        // 書き換え対象の .playable のパス(ドライラン・テスト・ログ用。対象が無ければ空)。
        public static List<string> FindTargetPaths()
        {
            var result = new List<string>();
            var resolver = new ScriptGuidResolver();
            foreach (var guid in AssetDatabase.FindAssets("t:TimelineAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".playable", StringComparison.OrdinalIgnoreCase))
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
                if (path.EndsWith(".playable", StringComparison.OrdinalIgnoreCase))
                {
                    paths.Add(path);
                }
            }

            MigratePaths(context, paths);
        }

        // 指定した .playable だけを書き換える(テスト・FindTargetPaths 用に Migrate から分けた)。
        public static void MigratePaths(MigrationContext context, IEnumerable<string> paths)
        {
            var resolver = new ScriptGuidResolver();
            var changedPaths = new List<string>();
            foreach (var path in paths)
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

                File.WriteAllText(Path.GetFullPath(path), rewritten, new UTF8Encoding(false));
                changedPaths.Add(path);
                context?.Note($"{path}: スクリプト参照を {count} 件、MonoScript の GUID 参照に書き換えました");
            }

            foreach (var path in changedPaths)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
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

                    // 形式: `<アセンブリ>:<名前空間>:<クラス名>`(クラス名とファイル名が違う型。名前空間が空なら `<アセンブリ>::<クラス名>`)
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
                        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
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
