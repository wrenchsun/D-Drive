using DDrive.Editor.AssetBrowser;
using DDrive.Foundation.Identity;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    public class AssetNamingServiceTests
    {
        [TestCase(AssetType.Se, "SE")]
        [TestCase(AssetType.Bgm, "BGM")]
        [TestCase(AssetType.Vfx, "VFX")]
        [TestCase(AssetType.Presentation, "PRES")]
        public void GetTypePrefix_MatchesConvention(AssetType type, string expected)
        {
            Assert.AreEqual(expected, AssetNamingService.GetTypePrefix(type));
        }

        [TestCase("PlayerSlash", true)]
        [TestCase("A", true)]
        [TestCase("Slash01", true)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("playerSlash", false)] // 先頭小文字
        [TestCase("Player_Slash", false)] // 記号
        [TestCase("剣の斬撃", false)] // 日本語は識別子には使えない(表示名に入れる)
        [TestCase("1Slash", false)] // 数字始まり
        public void IsValidIdentifier_EnforcesPascalCase(string identifier, bool expected)
        {
            Assert.AreEqual(expected, AssetNamingService.IsValidIdentifier(identifier));
        }

        [Test]
        public void BuildFileName_WithCategory_UsesPrefixCategoryIdentifier()
        {
            Assert.AreEqual("SE_Player_Slash", AssetNamingService.BuildFileName(AssetType.Se, "Player", "Slash"));
        }

        [Test]
        public void BuildFileName_WithoutCategory_OmitsCategorySegment()
        {
            Assert.AreEqual("BGM_Battle", AssetNamingService.BuildFileName(AssetType.Bgm, "", "Battle"));
        }

        [Test]
        public void BuildFileName_HierarchicalCategory_UsesLastSegmentOnly()
        {
            Assert.AreEqual("SE_Player_Slash", AssetNamingService.BuildFileName(AssetType.Se, "Audio/SE/Player", "Slash"));
        }

        [Test]
        public void CategorySegmentForFileName_StripsNonAlphanumeric()
        {
            Assert.AreEqual("Player", AssetNamingService.CategorySegmentForFileName("プレイヤーPlayer!"));
        }

        [Test]
        public void CategorySegmentForFileName_AllJapanese_BecomesEmpty()
        {
            Assert.AreEqual(string.Empty, AssetNamingService.CategorySegmentForFileName("プレイヤー"));
        }

        [TestCase("Player", "Player")]
        [TestCase("Player/Attack", "Player/Attack")]
        [TestCase("プレイヤー", "")] // 全滅セグメントは畳む
        [TestCase("Player/日本語/Sub", "Player/Sub")]
        [TestCase(" Player / Attack ", "Player/Attack")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void CategoryFolderPath_SanitizesEachSegment(string category, string expected)
        {
            Assert.AreEqual(expected, AssetNamingService.CategoryFolderPath(category));
        }

        [Test]
        public void GetTargetFolder_WithCategory_AppendsCategoryHierarchy()
        {
            Assert.AreEqual("Audio/SE/Player/Attack", AssetNamingService.GetTargetFolder(AssetType.Se, "Player/Attack"));
        }

        [Test]
        public void GetTargetFolder_WithEmptyOrJapaneseCategory_StaysAtTypeRoot()
        {
            Assert.AreEqual("Audio/SE", AssetNamingService.GetTargetFolder(AssetType.Se, ""));
            Assert.AreEqual("Audio/BGM", AssetNamingService.GetTargetFolder(AssetType.Bgm, "ボス戦"));
        }
    }
}
