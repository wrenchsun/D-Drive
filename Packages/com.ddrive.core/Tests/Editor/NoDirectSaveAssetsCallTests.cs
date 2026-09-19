using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [44_review_2026-09-19.md] P1-1 の再発防止。
    //
    // AssetDatabase.SaveAssets() の引数なし直呼びは「呼んだ瞬間にプロジェクト全体で dirty な
    // AssetDataBase すべて」を無差別に版数へ乗せてしまう(VersionStamp.cs:17-20)。本番コード
    // (Packages/com.ddrive.core/Editor/**/*.cs)からは DDriveAssetSave.SaveAllSuppressed() / SaveDirty(obj) の
    // どちらかを必ず経由させ、直呼びが再び紛れ込むのを機械的に検出する。
    //
    // 対象を Packages/com.ddrive.core/Editor/**/*.cs に絞った理由(ForbiddenApiScanner に規則を足す案を採らなかった
    // 理由): Packages/com.ddrive.core/Tests/Editor/**/*.cs は既に 6bba07a で「テスト自身の一時アセット/Addressables
    // 設定の後始末」として `using (VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }` の
    // 形で 55 箇所が意図的に直呼びを含んだまま残っている(この書き方自体は正しい)。ForbiddenApiScanner は
    // ファイル名末尾の完全一致でしか許可リストを持てないため、これら 55+ ファイルを 1 つずつ登録する必要が
    // あり非現実的。加えて VersionStampTests.cs は「保存フック自体の単体テスト」として意図的に非抑止のまま
    // (docs/41 で確認済み)。そのため Tests/Editor は本テストのスコープ外にし、Editor/**/*.cs だけを
    // 「直呼びが 1 件も無い」という単純な不変条件でチェックする。
    public class NoDirectSaveAssetsCallTests
    {
        private const string RootFolder = "Packages/com.ddrive.core/Editor";

        // ヘルパー本体だけが直呼びしてよい。
        private static readonly string[] AllowedFileSuffixes =
        {
            "Editor/Versioning/DDriveAssetSave.cs",
        };

        private static readonly Regex DirectCallPattern = new(@"AssetDatabase\.SaveAssets\s*\(", RegexOptions.Compiled);

        [Test]
        public void EditorProductionCode_HasNoDirectSaveAssetsCall()
        {
            Assert.IsTrue(Directory.Exists(RootFolder), $"{RootFolder} が見つかりません。");

            var violations = new List<string>();

            foreach (var file in Directory.GetFiles(RootFolder, "*.cs", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (IsAllowed(normalized))
                {
                    continue;
                }

                var lines = File.ReadAllLines(normalized);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (line.TrimStart().StartsWith("//"))
                    {
                        continue;
                    }

                    if (DirectCallPattern.IsMatch(line))
                    {
                        violations.Add($"{normalized}:{i + 1}");
                    }
                }
            }

            Assert.IsEmpty(
                violations,
                "AssetDatabase.SaveAssets() の直呼びが見つかりました。DDriveAssetSave.SaveAllSuppressed() / " +
                "SaveDirty(obj) のどちらかに置き換えてください([44_review_2026-09-19.md] P1-1):\n" +
                string.Join("\n", violations));
        }

        private static bool IsAllowed(string path)
        {
            foreach (var suffix in AllowedFileSuffixes)
            {
                if (path.EndsWith(suffix, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
