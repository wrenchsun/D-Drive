using DDrive.Editor.Cutscene;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [26_timeline.md] §5.1/§5.4/§5.5(6-10c) — CutsceneShotParser(命名規則の純ロジック)のテスト。
    public class CutsceneShotParserTests
    {
        [Test]
        public void ParseFileName_CameraPropsFile_IsNotCharacter()
        {
            CutsceneShotParser.ParseFileName("Opening01", out var shot, out var isCharacter, out var modelRaw);

            Assert.AreEqual("Opening01", shot);
            Assert.IsFalse(isCharacter);
            Assert.IsNull(modelRaw);
        }

        [Test]
        public void ParseFileName_CharacterFile_SplitsShotAndModelIdentifier()
        {
            CutsceneShotParser.ParseFileName("Opening01__Hero", out var shot, out var isCharacter, out var modelRaw);

            Assert.AreEqual("Opening01", shot);
            Assert.IsTrue(isCharacter);
            Assert.AreEqual("Hero", modelRaw);
        }

        [Test]
        public void ParseFileName_DuplicateCharacter_KeepsRawSuffix()
        {
            CutsceneShotParser.ParseFileName("Opening01__Hero_2", out var shot, out var isCharacter, out var modelRaw);

            Assert.AreEqual("Opening01", shot);
            Assert.IsTrue(isCharacter);
            Assert.AreEqual("Hero_2", modelRaw);
        }

        [Test]
        public void StripDuplicateSuffix_WithNumericSuffix_StripsIt()
        {
            Assert.AreEqual("Hero", CutsceneShotParser.StripDuplicateSuffix("Hero_2"));
            Assert.AreEqual("EnemyBoss", CutsceneShotParser.StripDuplicateSuffix("EnemyBoss_10"));
        }

        [Test]
        public void StripDuplicateSuffix_WithoutNumericSuffix_ReturnsAsIs()
        {
            Assert.AreEqual("Hero", CutsceneShotParser.StripDuplicateSuffix("Hero"));
        }

        [Test]
        public void StripPropPrefix_WithPrpPrefix_StripsIt()
        {
            Assert.AreEqual("Sword", CutsceneShotParser.StripPropPrefix("PRP_Sword"));
        }

        [Test]
        public void StripPropPrefix_WithNamespace_StripsAfterColon()
        {
            Assert.AreEqual("Sword", CutsceneShotParser.StripPropPrefix("Sword:root"));
        }

        [Test]
        public void StripPropPrefix_PlainName_ReturnsAsIs()
        {
            Assert.AreEqual("Door", CutsceneShotParser.StripPropPrefix("Door"));
        }
    }
}
