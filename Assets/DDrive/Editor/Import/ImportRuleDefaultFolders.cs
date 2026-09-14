using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Menu;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Import
{
    // [11_tasks.md] 5-11 追加分(2026-09-14、オーケストレーター要望「デフォルトのディレクトリ構成を作って」) —
    // ImportRuleService が監視する種別フォルダ(Se/Bgm/Texture/Model/Anim/Anim2D/Prefab/Canvas/Vfx)を
    // Assets/SourceAssets/ 直下に用意し、置き方を説明する README.md を添える。
    // git は空フォルダを保存できず、.meta だけが残ると clone 先で Unity が警告して消してしまうため、
    // 各フォルダに README.md(Unity では TextAsset として読み込まれるだけの内容)を入れて「空」にしない。
    // 種別一覧は ImportRuleService.Handlers から取る(ハードコードしない。Cutscene(6-10c)等の追加が自動で反映される)。
    public static class ImportRuleDefaultFolders
    {
        public const string ReadmeFileName = "README.md";

        public sealed class Report
        {
            public int CreatedFolders;
            public int CreatedReadmes;
            public readonly List<string> Lines = new();

            public void Log(string line) => Lines.Add(line);

            public override string ToString()
                => $"フォルダ新規 {CreatedFolders} / README 新規 {CreatedReadmes}\n" + string.Join("\n", Lines);
        }

        [MenuItem(DDriveMenu.Generate + "SourceAssets の既定フォルダを作成")]
        private static void CreateDefaultFoldersMenuItem()
        {
            var report = EnsureDefaultFolders();
            Debug.Log($"[DDrive] SourceAssets 既定フォルダ: {report}");
        }

        // テストからも直接呼べる中核処理(冪等: 既にあるフォルダ/README には触らない)。
        // sourceRoot を引数化しているのは ImportRuleServiceTests 等と同じ「実データを汚さない」ため。
        public static Report EnsureDefaultFolders(string sourceRoot = ImportRuleService.DefaultSourceRoot)
        {
            var report = new Report();

            if (!AssetDatabase.IsValidFolder(sourceRoot))
            {
                var parent = Path.GetDirectoryName(sourceRoot)?.Replace('\\', '/');
                var folderName = Path.GetFileName(sourceRoot);
                if (string.IsNullOrEmpty(parent) || !AssetDatabase.IsValidFolder(parent))
                {
                    report.Log($"失敗: {sourceRoot} の親フォルダが見つかりません");
                    return report;
                }

                AssetDatabase.CreateFolder(parent, folderName);
                report.CreatedFolders++;
                report.Log($"新規フォルダ: {sourceRoot}");
            }

            EnsureReadme(sourceRoot, BuildRootReadme(), report);

            foreach (var handler in ImportRuleService.Handlers)
            {
                var folderPath = $"{sourceRoot}/{handler.TypeFolder}";
                if (!AssetDatabase.IsValidFolder(folderPath))
                {
                    AssetDatabase.CreateFolder(sourceRoot, handler.TypeFolder);
                    report.CreatedFolders++;
                    report.Log($"新規フォルダ: {folderPath}");
                }
                else if (IsCaseCollision(folderPath, out var existingPath))
                {
                    // Windows 等の大文字小文字を区別しないファイルシステムでは、綴りは合っていても
                    // 既存の別フォルダ(例: 小文字の "model")に解決されてしまうことがある。
                    // 誤ってその既存フォルダ(無関係なファイルが入っている可能性がある)を README で
                    // 汚さないよう、この種別はスキップして案内だけ残す(2026-09-14、実行時に model/README.md
                    // へ誤って書き込んでしまった事故の再発防止)。
                    report.Log(
                        $"スキップ: {folderPath} は既存の '{existingPath}' と大文字小文字違いで衝突しています" +
                        "(README は作成しません。どちらかのフォルダ名を変えて解消してください)");
                    continue;
                }

                EnsureReadme(folderPath, BuildTypeReadme(handler), report);
            }

            AssetDatabase.SaveAssets();
            return report;
        }

        // Windows 等の大文字小文字を区別しないファイルシステムでは、AssetDatabase.IsValidFolder(folderPath) が
        // true を返しても、実体は綴りの違う既存フォルダ(例: "Model" のつもりが既存の "model")のことがある。
        // GUID から正規のパスを引き直し、指定した綴りと違えば衝突とみなす。
        private static bool IsCaseCollision(string folderPath, out string existingPath)
        {
            var guid = AssetDatabase.AssetPathToGUID(folderPath);
            existingPath = string.IsNullOrEmpty(guid) ? folderPath : AssetDatabase.GUIDToAssetPath(guid);
            return !string.Equals(existingPath, folderPath, StringComparison.Ordinal);
        }

        // 既存の README は上書きしない(デザイナーが書き換えている可能性があるため)。無ければ作る。
        private static void EnsureReadme(string folderPath, string content, Report report)
        {
            var assetPath = $"{folderPath}/{ReadmeFileName}";
            var absolutePath = Path.GetFullPath(assetPath);
            if (File.Exists(absolutePath))
            {
                return;
            }

            File.WriteAllText(absolutePath, content, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            report.CreatedReadmes++;
            report.Log($"新規 README: {assetPath}");
        }

        private static string BuildRootReadme()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SourceAssets");
            sb.AppendLine();
            sb.AppendLine("ここに元ファイル(音源・画像・FBX・アニメーション・Prefab)を置くと、");
            sb.AppendLine("`ImportRule` が自動で Data・ID・カタログ・Addressables 登録まで行います。");
            sb.AppendLine();
            sb.AppendLine("**1 階層目のフォルダ名が種別として認識されます(大文字小文字も区別)。**");
            sb.AppendLine("`SourceAssets` の直下に直接ファイルを置いたり、種別フォルダの上に別のフォルダを");
            sb.AppendLine("挟んだりすると認識されません(Console に案内の警告が出ます)。");
            sb.AppendLine();
            sb.AppendLine("## 種別フォルダ一覧");
            sb.AppendLine();
            foreach (var handler in ImportRuleService.Handlers)
            {
                sb.AppendLine($"- `{handler.TypeFolder}/` — 詳細は `{handler.TypeFolder}/{ReadmeFileName}` を参照");
            }

            sb.AppendLine();
            sb.AppendLine("詳しい仕様は `docs/09_editor_tools.md` §1.1 / `docs/10_workflow.md` §3.3 を参照してください。");
            return sb.ToString();
        }

        private static string BuildTypeReadme(IImportRuleHandler handler)
        {
            const string sampleCategory = "Sample";
            const string sampleFile = "Foo";
            var targetFolder = AssetNamingService.GetTargetFolder(handler.Target, sampleCategory);
            var fileName = AssetNamingService.BuildFileName(handler.Target, sampleCategory, sampleFile);
            var sampleExt = handler.Extensions.Length > 0 ? handler.Extensions[0] : string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine($"# SourceAssets/{handler.TypeFolder}");
            sb.AppendLine();
            sb.AppendLine($"このフォルダには **{handler.Target}** の元ファイルを置きます。");
            sb.AppendLine();
            sb.AppendLine("## 対応拡張子");
            sb.AppendLine();
            sb.AppendLine(string.Join(" / ", handler.Extensions));
            sb.AppendLine();
            sb.AppendLine("## 置き方");
            sb.AppendLine();
            sb.AppendLine($"`{handler.TypeFolder}/<カテゴリ.../>ファイル名` に置いてください。");
            sb.AppendLine("カテゴリは省略できます(この場合は種別フォルダの直下に置きます)。");
            sb.AppendLine("カテゴリは `Player/Attack` のように複数階層にもできます。");
            sb.AppendLine();
            sb.AppendLine("## 生成される Data の例");
            sb.AppendLine();
            sb.AppendLine($"`Assets/SourceAssets/{handler.TypeFolder}/{sampleCategory}/{sampleFile}{sampleExt}` を置くと、");
            sb.AppendLine($"`Assets/GameData/{targetFolder}/{fileName}.asset` が自動的に作られます。");
            sb.AppendLine();
            sb.AppendLine("## 注意");
            sb.AppendLine();
            sb.AppendLine("元ファイルを削除しても Data 自体は消えません" +
                "(参照が「未設定(または Missing)」になるだけです。Validation の一覧に欠落として表示されます)。");
            return sb.ToString();
        }
    }
}
