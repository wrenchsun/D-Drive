using System;
using System.IO;
using System.Text;
using DDrive.Editor;
using UnityEditor;

namespace DDrive.Editor.Codegen
{
    // [42_distribution.md] §2.3-7/§3.4/§5.7/§7 A-8(P-6、2026-09-20) — `Assets/Generated/`(既定)配下に
    // asmdef が無いと、asmdef 付きのゲームコード(`Assembly-CSharp` を参照できない)から生成された
    // ID 定数(`SEID` 等)を参照できない事故を先回りする。`AssetIdGenerator.Regenerate` の既定呼び出し
    // (出力先を明示しない = メニュー/ウィザードからの通常実行)からだけ呼ばれる想定で、
    // `DDriveProjectSettings.EmitGeneratedAsmdef`(既定 ON)で無効化できる。
    //
    // 既存の(手で作った/以前生成した)asmdef を尊重し、フォルダに asmdef が 1 つでもあれば何もしない
    // (ファイル名を問わない: プロジェクトによっては `DDrive.Generated` ではない名前へリネームして
    // 使っている可能性があるため上書きしない)。
    public static class GeneratedAsmdefWriter
    {
        public const string AsmdefFileName = "DDrive.Generated.asmdef";

        // 純粋関数(EditMode テストで内容を固定する用)。実際に書き出す内容そのもの。
        public static string BuildAsmdefJson()
        {
            var sb = new StringBuilder();
            sb.Append('{').Append('\n');
            sb.Append("    \"name\": \"DDrive.Generated\",\n");
            sb.Append("    \"rootNamespace\": \"\",\n");
            sb.Append("    \"references\": [\n");
            sb.Append("        \"DDrive.Foundation\",\n");
            sb.Append("        \"DDrive.Runtime\"\n");
            sb.Append("    ],\n");
            sb.Append("    \"includePlatforms\": [],\n");
            sb.Append("    \"excludePlatforms\": [],\n");
            sb.Append("    \"allowUnsafeCode\": false,\n");
            sb.Append("    \"overrideReferences\": false,\n");
            sb.Append("    \"precompiledReferences\": [],\n");
            sb.Append("    \"autoReferenced\": true,\n");
            sb.Append("    \"defineConstraints\": [],\n");
            sb.Append("    \"versionDefines\": [],\n");
            sb.Append("    \"noEngineReferences\": false\n");
            sb.Append('}').Append('\n');
            return sb.ToString();
        }

        // folder: asmdef を置くフォルダ(通常は Generated 出力フォルダ、例 "Assets/Generated")。
        // emit=false、フォルダが Assets 配下でない(テスト用一時フォルダ等)、または既に asmdef が
        // あるときは何もせず false を返す。
        public static bool EnsureAsmdef(string folder, bool emit)
        {
            if (!emit || string.IsNullOrEmpty(folder))
            {
                return false;
            }

            var normalized = folder.Replace('\\', '/').TrimEnd('/');
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) && normalized != "Assets")
            {
                return false; // Packages 配下のテスト用一時出力先等は対象外。
            }

            if (HasAnyAsmdef(normalized))
            {
                return false;
            }

            var assetPath = normalized + "/" + AsmdefFileName;
            var absolutePath = Path.GetFullPath(assetPath);
            var dir = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(absolutePath, BuildAsmdefJson(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            return true;
        }

        private static bool HasAnyAsmdef(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return false;
            }

            return AssetSearch.FindAssets("t:AssemblyDefinitionAsset", new[] { folder }).Length > 0;
        }
    }
}
