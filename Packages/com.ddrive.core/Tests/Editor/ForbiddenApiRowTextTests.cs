using DDrive.Editor.Validation;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // 2026-10-06(docs/43 17-1 / 17-7): 「禁止 API の検査」ウィンドウの行の文言(長いパスの省略・案内文が本文に入る)。
    public class ForbiddenApiRowTextTests
    {
        [Test]
        public void DisplayPath_Short_IsUnchanged_WithLine()
        {
            Assert.AreEqual("Assets/A.cs:12", ForbiddenApiRowText.DisplayPath("Assets/A.cs", 12));
            Assert.AreEqual("Assets/A.cs", ForbiddenApiRowText.DisplayPath("Assets/A.cs", 0));
        }

        [Test]
        public void DisplayPath_Long_KeepsTailAndLineNumber_WithinLimit()
        {
            var path = "Assets/" + new string('d', 120) + "/Deep/SomeVeryLongFileName.cs";
            var text = ForbiddenApiRowText.DisplayPath(path, 345);
            Assert.LessOrEqual(text.Length, ForbiddenApiRowText.DefaultPathChars);
            Assert.IsTrue(text.StartsWith("…"));
            Assert.IsTrue(text.EndsWith("SomeVeryLongFileName.cs:345"), text);
        }

        [Test]
        public void DisplayPath_TinyLimit_StillKeepsLine()
        {
            var text = ForbiddenApiRowText.DisplayPath("Assets/Some/Long/File.cs", 7, 3);
            Assert.IsTrue(text.EndsWith(":7"), text);
        }

        [Test]
        public void ViolationLines_PutsGuidanceInBody_NotOnlyTooltip()
        {
            var message = "Instantiate は禁止。 [許可コメントに理由が必要です。`ddrive-allow: Instantiate(理由)` の形で…]";
            ForbiddenApiRowText.ViolationLines("Instantiate", "Object.Instantiate(x);", message, out var body, out var guidance);
            Assert.AreEqual("[Instantiate] Object.Instantiate(x);", body);
            StringAssert.Contains("許可コメントに理由が必要です", guidance);
        }

        [Test]
        public void ViolationLines_GlobalError_UsesMessageAsBody()
        {
            ForbiddenApiRowText.ViolationLines(null, null, "走査ルートがありません", out var body, out var guidance);
            Assert.AreEqual("走査ルートがありません", body);
            Assert.IsNull(guidance);
        }
    }
}
