using DDrive.Editor.Settings;
using DDrive.Editor.Spec;
using NUnit.Framework;
using UnityEditor;

namespace DDrive.Tests.Editor
{
    // [32_spec_web.md] §5.2/§7 W-9 — API トークンは .asset(git 管理)ではなく EditorPrefs
    // (マシンごと)に保存する。実機の EditorPrefs を汚さないよう、SetUp/TearDown で
    // 元の値を保存・復元する(このテストは開発者の実際のマシンで走る)。
    public class DDriveSpecSettingsTests
    {
        private string _prevReadToken;
        private string _prevWriteToken;

        [SetUp]
        public void SetUp()
        {
            _prevReadToken = DDriveSpecSettings.ReadToken;
            _prevWriteToken = DDriveSpecSettings.WriteToken;
        }

        [TearDown]
        public void TearDown()
        {
            DDriveSpecSettings.ReadToken = _prevReadToken;
            DDriveSpecSettings.WriteToken = _prevWriteToken;
        }

        [Test]
        public void ReadToken_RoundTrips()
        {
            DDriveSpecSettings.ReadToken = "test-read-token-value";
            Assert.AreEqual("test-read-token-value", DDriveSpecSettings.ReadToken);
        }

        [Test]
        public void WriteToken_RoundTrips()
        {
            DDriveSpecSettings.WriteToken = "test-write-token-value";
            Assert.AreEqual("test-write-token-value", DDriveSpecSettings.WriteToken);
        }

        [Test]
        public void ReadToken_And_WriteToken_AreIndependent()
        {
            DDriveSpecSettings.ReadToken = "read-value";
            DDriveSpecSettings.WriteToken = "write-value";

            Assert.AreEqual("read-value", DDriveSpecSettings.ReadToken);
            Assert.AreEqual("write-value", DDriveSpecSettings.WriteToken);
        }

        [Test]
        public void ReadToken_Unset_DefaultsToEmptyString()
        {
            DDriveSpecSettings.ReadToken = null;
            Assert.AreEqual(string.Empty, DDriveSpecSettings.ReadToken);
        }

        // [42_distribution.md] §2.3 #11(P-12 で発見、docs/49) — DefaultPath/DefaultTuningTablePath は
        // 以前 const のハードコードで DDriveProjectSettings.GameDataRoot(置き場所プリセット)を無視していた。
        // 純粋なロジック(ResolveSettingsPathCore)だけを、AssetDatabase・実 ScriptableSingleton に触れずに検証する。
        [Test]
        public void ResolveSettingsPathCore_LegacyAssetExists_ReturnsLegacyPath_RegardlessOfGameDataRoot()
        {
            var legacyPath = "Assets/GameData/Settings/DDriveSpecSettings.asset";

            var resolved = DDriveSpecSettings.ResolveSettingsPathCore(
                "DDriveSpecSettings.asset", legacyPath, legacyAssetExists: true, gameDataRoot: "Assets/_Project/DDrive/GameData");

            Assert.AreEqual(legacyPath, resolved, "既に既定パスに存在するなら移動せずそれを使う");
        }

        [Test]
        public void ResolveSettingsPathCore_NoLegacyAsset_DefaultGameDataRoot_MatchesLegacyPath()
        {
            var legacyPath = "Assets/GameData/Settings/DDriveSpecSettings.asset";

            var resolved = DDriveSpecSettings.ResolveSettingsPathCore(
                "DDriveSpecSettings.asset", legacyPath, legacyAssetExists: false, gameDataRoot: "Assets/GameData");

            Assert.AreEqual(legacyPath, resolved, "GameDataRoot が既定値のままなら今までと同じパスになる(既存アセットが迷子にならない)");
        }

        [Test]
        public void ResolveSettingsPathCore_NoLegacyAsset_CustomGameDataRoot_FollowsGameDataRoot()
        {
            var legacyPath = "Assets/GameData/Settings/DDriveSpecSettings.asset";

            var resolved = DDriveSpecSettings.ResolveSettingsPathCore(
                "DDriveSpecSettings.asset", legacyPath, legacyAssetExists: false, gameDataRoot: "Assets/_Project/DDrive/GameData");

            Assert.AreEqual("Assets/_Project/DDrive/GameData/Settings/DDriveSpecSettings.asset", resolved,
                "既定パスに何も無ければ DDriveProjectSettings.GameDataRoot(置き場所プリセット)配下に置く");
        }

        // 実プロパティ(DefaultPath/DefaultTuningTablePath)が ResolveSettingsPathCore と実際の
        // プロジェクト状態(legacy アセットの有無・DDriveProjectSettings.instance.GameDataRoot)から
        // 一貫して求まることを確認する(GameDataRoot を変更済みの持ち込み先でも成立する。値をハードコードしない)。
        [Test]
        public void DefaultPath_And_DefaultTuningTablePath_AreConsistentWithActualProjectState()
        {
            const string legacySpecPath = "Assets/GameData/Settings/DDriveSpecSettings.asset";
            const string legacyTuningPath = "Assets/GameData/Settings/DDriveTuningTable.asset";
            var gameDataRoot = DDriveProjectSettings.instance.GameDataRoot;

            var expectedSpecPath = DDriveSpecSettings.ResolveSettingsPathCore(
                "DDriveSpecSettings.asset", legacySpecPath,
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(legacySpecPath) != null, gameDataRoot);
            var expectedTuningPath = DDriveSpecSettings.ResolveSettingsPathCore(
                "DDriveTuningTable.asset", legacyTuningPath,
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(legacyTuningPath) != null, gameDataRoot);

            Assert.AreEqual(expectedSpecPath, DDriveSpecSettings.DefaultPath);
            Assert.AreEqual(expectedTuningPath, DDriveSpecSettings.DefaultTuningTablePath);
        }
    }
}
