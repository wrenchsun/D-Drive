using System.Collections.Generic;
using DDrive.Editor;
using DDrive.Foundation.Validation;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    public class CIJUnitXmlTests
    {
        [Test]
        public void BuildJUnitXml_CountsFailuresAsErrorsOnly()
        {
            var entries = new List<(string assetPath, ValidationResult result)>
            {
                ("Assets/A.asset", ValidationResult.Error("missing ref")),
                ("Assets/B.asset", ValidationResult.Warning("loop without pool")),
                ("Assets/C.asset", ValidationResult.Info("fyi")),
            };

            var xml = CI.BuildJUnitXml(entries);

            StringAssert.Contains("tests=\"3\"", xml);
            StringAssert.Contains("failures=\"1\"", xml);
            StringAssert.Contains("<failure message=\"missing ref\">Error</failure>", xml);
            StringAssert.DoesNotContain("<failure message=\"loop without pool\"", xml);
            StringAssert.Contains("classname=\"Assets/A.asset\"", xml);
        }

        [Test]
        public void BuildJUnitXml_EscapesXmlSpecialCharacters()
        {
            var entries = new List<(string assetPath, ValidationResult result)>
            {
                ("Assets/<Weird & Name>.asset", ValidationResult.Error("bad \"quote\"")),
            };

            var xml = CI.BuildJUnitXml(entries);

            StringAssert.Contains("Assets/&lt;Weird &amp; Name&gt;.asset", xml);
            StringAssert.Contains("bad &quot;quote&quot;", xml);
        }

        [Test]
        public void BuildJUnitXml_Empty_ProducesZeroCounts()
        {
            var xml = CI.BuildJUnitXml(new List<(string assetPath, ValidationResult result)>());

            StringAssert.Contains("tests=\"0\"", xml);
            StringAssert.Contains("failures=\"0\"", xml);
        }

        // [42_distribution.md] §2.3-2(P-4、2026-09-20) — 開発リポジトリ(P-5 未実施 = Assets/DDrive が
        // 通常の Assets フォルダ)では PackageInfo が null になり、既存どおり "Assets/DDrive" にフォールバックする。
        [Test]
        public void ResolveForbiddenApiScanRoot_FallsBackToAssetsDDrive_WhenNotPackaged()
        {
            Assert.AreEqual("Assets/DDrive", CI.ResolveForbiddenApiScanRoot());
        }
    }
}
