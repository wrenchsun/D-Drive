using DDrive.Editor.Settings;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [42_distribution.md] §3.4 / §4.3 / §7 B-6(P-4、2026-09-20) — 出力先パス設定の土台。
    // 現状は既定値が変わらないことだけを確認する(実際の参照差し替えは P-5)。
    public class DDriveProjectSettingsTests
    {
        [Test]
        public void Defaults_MatchCurrentHardcodedPaths()
        {
            var settings = DDriveProjectSettings.instance;

            Assert.AreEqual("Assets/GameData", settings.GameDataRoot);
            Assert.AreEqual("Assets/Generated", settings.GeneratedRoot);
            Assert.AreEqual("Assets/SourceAssets", settings.SourceAssetsRoot);
            Assert.AreEqual("Specs", settings.SpecsRoot);
        }
    }
}
