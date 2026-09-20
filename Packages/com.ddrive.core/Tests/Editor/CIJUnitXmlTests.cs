using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor;
using DDrive.Editor.Settings;
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

        // [42_distribution.md] §2.3-2(P-4/P-5、2026-09-20) — 開発リポジトリは P-5 でパッケージ化済みの
        // ため PackageInfo が解決でき、走査ルートは常にパッケージの実パス(絶対パス)になる。
        // "Assets/DDrive" へのフォールバックは(このリポジトリでは再現できないが)パッケージ化されていない
        // 消費側環境向けの防御コードとして CI.cs 側に残っている。
        // [48_p11_install_test_2026-09-20.md] フォローアップ(2026-09-20 修正) — もともと
        // `DDriveProjectSettings.instance.IsDevelopmentRepo` が(この開発リポジトリでは
        // `DevRepoSettingsSync` が `DDRIVE_DEV_REPO` 定義から自動で true にする)前提で書かれており、
        // 持ち込み先で testables を有効にして実行すると(`DDRIVE_DEV_REPO` が無いため)false のままで
        // Fail していた。`ResolveForbiddenApiScanRoot_NotDevelopmentRepo_ReturnsAssets` と同じ流儀で
        // フィールドを直接書き換えて模擬し、環境に依存しない自己完結テストにする。
        [Test]
        public void ResolveForbiddenApiScanRoot_ResolvesToPackagedPath_InThisRepo()
        {
            var settings = DDriveProjectSettings.instance;
            var isDevField = typeof(DDriveProjectSettings).GetField("_isDevelopmentRepo", BindingFlags.NonPublic | BindingFlags.Instance);
            var originalIsDev = (bool)isDevField.GetValue(settings);

            try
            {
                isDevField.SetValue(settings, true);

                var root = CI.ResolveForbiddenApiScanRoot();

                StringAssert.EndsWith("com.ddrive.core", root.Replace('\\', '/').TrimEnd('/'));
            }
            finally
            {
                isDevField.SetValue(settings, originalIsDev);
            }
        }

        // [47_review_p_tickets_2026-09-20.md] P1-2 — 持ち込み先(IsDevelopmentRepo=false)では
        // D-Drive 自身のパッケージではなく "Assets"(ゲームコード全体)を走査する。
        // ProjectSetupValidatorTests と同じ流儀でフィールドを直接書き換えて模擬する(Save を呼ばない)。
        [Test]
        public void ResolveForbiddenApiScanRoot_NotDevelopmentRepo_ReturnsAssets()
        {
            var settings = DDriveProjectSettings.instance;
            var isDevField = typeof(DDriveProjectSettings).GetField("_isDevelopmentRepo", BindingFlags.NonPublic | BindingFlags.Instance);
            var originalIsDev = (bool)isDevField.GetValue(settings);

            try
            {
                isDevField.SetValue(settings, false);

                var root = CI.ResolveForbiddenApiScanRoot();

                Assert.AreEqual("Assets", root);
            }
            finally
            {
                isDevField.SetValue(settings, originalIsDev);
            }
        }
    }
}
