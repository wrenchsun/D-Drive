using System;
using System.IO;
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
            var path = Path.Combine(_tempRepoRoot, "Specs", "assets.json");
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
            var path = Path.Combine(_tempRepoRoot, "Specs", "assets.json");
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
            var path = Path.Combine(_tempRepoRoot, "Specs", "tuning.json");
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
            Assert.IsFalse(File.Exists(Path.Combine(_tempRepoRoot, "Specs", "assets.json")));
            StringAssert.Contains("トークンが無効です", result.Warning);
        }

        [Test]
        public void Write_AllNull_DoesNothingWithoutThrowing()
        {
            SpecSnapshotWriter.Result result = null;
            Assert.DoesNotThrow(() => result = SpecSnapshotWriter.Write(null, null, null, _tempRepoRoot));

            Assert.IsFalse(result.AssetsWritten);
            Assert.IsFalse(result.TuningWritten);
            Assert.IsFalse(Directory.Exists(Path.Combine(_tempRepoRoot, "Specs")));
        }
    }
}
