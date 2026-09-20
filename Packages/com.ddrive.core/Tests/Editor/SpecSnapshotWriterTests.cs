using System;
using System.IO;
using DDrive.Editor.Settings;
using DDrive.Editor.Spec;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [32_spec_web.md] §1.4/§8 W-11 — Specs/*.json への書き出し。
    // 実プロジェクトの repo 直下(Specs/)には絶対に書かない。一時フォルダに書き出して検証する
    // (依頼元の指示: 「テスト前後で git status --porcelain が増えないこと」)。
    public class SpecSnapshotWriterTests
    {
        private string _tempRepoRoot;

        // [42_distribution.md] §2.3 #12(P-12 で発見、docs/49) — SpecSnapshotWriter.Write は
        // DDriveProjectSettings.instance.SpecsRoot(置き場所プリセット、B-6)から書き出し先を組み立てるため、
        // 持ち込み先が SpecsRoot を "Specs" 以外に変更していると "Specs" 決め打ちの検証パスとズレて Fail する。
        // 実際の SpecsRoot から検証パスを組み立てる。
        private static string SpecsRoot => DDriveProjectSettings.instance.SpecsRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRepoRoot = Path.Combine(Path.GetTempPath(), "DDriveSpecSnapshotTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRepoRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempRepoRoot))
            {
                Directory.Delete(_tempRepoRoot, recursive: true);
            }
        }

        [Test]
        public void Write_ValidAssetsJson_WritesSortedAssetsFile()
        {
            const string assetsJson = "{\"ok\":true,\"items\":[" +
                "{\"id\":\"Se::Zeta\",\"assetType\":\"Se\",\"identifier\":\"Zeta\",\"revision\":1}," +
                "{\"id\":\"Se::Alpha\",\"assetType\":\"Se\",\"identifier\":\"Alpha\",\"revision\":2}" +
                "]}";

            var result = SpecSnapshotWriter.Write(assetsJson, null, null, _tempRepoRoot);

            Assert.IsTrue(result.AssetsWritten);
            var path = Path.Combine(_tempRepoRoot, SpecsRoot, "assets.json");
            Assert.IsTrue(File.Exists(path));

            var text = File.ReadAllText(path);
            // id でソートされているため Alpha が Zeta より先に出る。
            Assert.Less(text.IndexOf("Se::Alpha", StringComparison.Ordinal), text.IndexOf("Se::Zeta", StringComparison.Ordinal));
        }

        [Test]
        public void Write_SameInputTwice_ProducesIdenticalBytes()
        {
            const string assetsJson = "{\"ok\":true,\"items\":[{\"id\":\"Se::Alpha\",\"zField\":1,\"aField\":2}]}";

            SpecSnapshotWriter.Write(assetsJson, null, null, _tempRepoRoot);
            var path = Path.Combine(_tempRepoRoot, SpecsRoot, "assets.json");
            var first = File.ReadAllText(path);

            SpecSnapshotWriter.Write(assetsJson, null, null, _tempRepoRoot);
            var second = File.ReadAllText(path);

            Assert.AreEqual(first, second, "同じ入力なら常に同じバイト列になるはず(実質的な変更が無い同期で git diff が空になる)");
            // キーがアルファベット順に並び替えられていることも確認する(aField が zField より先)。
            Assert.Less(first.IndexOf("aField", StringComparison.Ordinal), first.IndexOf("zField", StringComparison.Ordinal));
        }

        [Test]
        public void Write_TuningScalarsAndTables_WritesCombinedTuningFile()
        {
            const string scalarsJson = "{\"ok\":true,\"items\":{\"Combat/HitStopSec\":{\"kind\":\"scalar\",\"valueType\":\"float\",\"value\":0.05}}}";
            const string tablesJson = "{\"ok\":true,\"items\":{\"Enemy/Params\":{\"kind\":\"table\",\"columns\":[],\"rows\":[]}}}";

            var result = SpecSnapshotWriter.Write(null, scalarsJson, tablesJson, _tempRepoRoot);

            Assert.IsTrue(result.TuningWritten);
            var path = Path.Combine(_tempRepoRoot, SpecsRoot, "tuning.json");
            var text = File.ReadAllText(path);
            StringAssert.Contains("Combat/HitStopSec", text);
            StringAssert.Contains("Enemy/Params", text);
            StringAssert.Contains("\"scalars\"", text);
            StringAssert.Contains("\"tables\"", text);
        }

        [Test]
        public void Write_ErrorEnvelope_DoesNotWriteFileAndReturnsWarning()
        {
            const string errorJson = "{\"ok\":false,\"status\":401,\"error\":\"トークンが無効です\"}";

            var result = SpecSnapshotWriter.Write(errorJson, null, null, _tempRepoRoot);

            Assert.IsFalse(result.AssetsWritten);
            Assert.IsFalse(File.Exists(Path.Combine(_tempRepoRoot, SpecsRoot, "assets.json")));
            StringAssert.Contains("トークンが無効です", result.Warning);
        }

        // 2026-09-17(docs/41_phase6_review_2026-09-17.md P2-5) — scalars / tables の
        // 片方だけ失敗したとき、失敗した側を空配列 `[]` で書き出していた(§8 W-11 の「失敗した部分は
        // 書き込まず警告を返す」と食い違い、git diff に「全スカラー削除」が現れ SpecDiffValidator の
        // 範囲チェックも黙って無効になっていた)。既存ファイルの該当配列を温存する。
        [Test]
        public void Write_TablesFail_KeepsExistingScalarsInFile()
        {
            const string scalarsJson = "{\"ok\":true,\"items\":{\"Combat/HitStopSec\":{\"kind\":\"scalar\",\"valueType\":\"float\",\"value\":0.05}}}";
            const string tablesJson = "{\"ok\":true,\"items\":{\"Enemy/Params\":{\"kind\":\"table\",\"columns\":[],\"rows\":[]}}}";

            // 1 回目: 両方成功。
            var first = SpecSnapshotWriter.Write(null, scalarsJson, tablesJson, _tempRepoRoot);
            Assert.IsTrue(first.TuningWritten);

            // 2 回目: tuningTableList だけ ok:false(トークン切れ・レート制限等)。
            const string tablesError = "{\"ok\":false,\"status\":429,\"error\":\"rate limited\"}";
            var second = SpecSnapshotWriter.Write(null, scalarsJson, tablesError, _tempRepoRoot);

            Assert.IsTrue(second.TuningWritten);
            var text = File.ReadAllText(Path.Combine(_tempRepoRoot, SpecsRoot, "tuning.json"));
            StringAssert.Contains("Combat/HitStopSec", text, "成功した側は更新される");
            StringAssert.Contains("Enemy/Params", text, "失敗した側は前回の内容を温存するはず(空配列で消してはいけない)");
            StringAssert.Contains("rate limited", second.Warning);
        }

        [Test]
        public void Write_ScalarsFail_KeepsExistingScalarsInFile()
        {
            const string scalarsJson = "{\"ok\":true,\"items\":{\"Combat/HitStopSec\":{\"kind\":\"scalar\",\"valueType\":\"float\",\"value\":0.05}}}";
            const string tablesJson = "{\"ok\":true,\"items\":{\"Enemy/Params\":{\"kind\":\"table\",\"columns\":[],\"rows\":[]}}}";

            SpecSnapshotWriter.Write(null, scalarsJson, tablesJson, _tempRepoRoot);

            const string scalarsError = "{\"ok\":false,\"status\":401,\"error\":\"unauthorized\"}";
            var second = SpecSnapshotWriter.Write(null, scalarsError, tablesJson, _tempRepoRoot);

            Assert.IsTrue(second.TuningWritten);
            var text = File.ReadAllText(Path.Combine(_tempRepoRoot, SpecsRoot, "tuning.json"));
            StringAssert.Contains("Combat/HitStopSec", text, "失敗した側(scalars)は前回の内容を温存するはず");
            StringAssert.Contains("Enemy/Params", text);
        }

        [Test]
        public void Write_AllNull_DoesNothingWithoutThrowing()
        {
            SpecSnapshotWriter.Result result = null;
            Assert.DoesNotThrow(() => result = SpecSnapshotWriter.Write(null, null, null, _tempRepoRoot));

            Assert.IsFalse(result.AssetsWritten);
            Assert.IsFalse(result.TuningWritten);
            Assert.IsFalse(Directory.Exists(Path.Combine(_tempRepoRoot, SpecsRoot)));
        }
    }
}
