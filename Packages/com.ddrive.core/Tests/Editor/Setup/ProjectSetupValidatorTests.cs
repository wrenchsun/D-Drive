using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DDrive.Editor.Settings;
using DDrive.Editor.Validation;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor.Setup
{
    // [42_distribution.md] §3.6/§6 P-6(2026-09-20) — ProjectSetupValidator(IUniversalValidator)の
    // 回帰テスト。このリポジトリ(開発リポジトリ)は既にセットアップ済みのため、
    // 現状のまま実行すると Warning 0 件になることを固定する(= 検査ロジック自体が誤検出していないこと
    // の確認)。IUniversalValidator は ValidatorRegistry.RunAll が「1 件以上の AssetDataBase がある
    // ときにだけ」呼ぶ既存の制約があるため、メモリ上だけの(保存しない)ダミー Data を 1 件用意する。
    public class ProjectSetupValidatorTests
    {
        private SeData _dummy;

        [SetUp]
        public void SetUp()
        {
            _dummy = ScriptableObject.CreateInstance<SeData>(); // 保存しない(メモリ上のみ)。
        }

        [TearDown]
        public void TearDown()
        {
            if (_dummy != null)
            {
                Object.DestroyImmediate(_dummy);
            }
        }

        // [48_p11_install_test_2026-09-20.md] フォローアップ(2026-09-20) — `ProjectSetupValidator` は
        // manifest.json の依存・URP/Input System/API Level・Addressables 初期化・GameData 等のフォルダ・
        // `IsDevelopmentRepo` という複数のグローバルなプロジェクト状態を集約するだけで、注入口が無い。
        // 「持ち込み先の素のプロジェクトでも Warning 0 件」を主張するテストではなく「この開発リポジトリは
        // 常に Warning 0 件であるべき」という回帰テストなので DevRepoOnly にする
        // (manifest.json 編集・Addressables 初期化・ProjectSettings 変更をテスト内で行って復元する方式は、
        // 失敗時に実プロジェクトの設定を壊しかねずリスクが高いため見送った)。
        [Test]
        [Category("DevRepoOnly")]
        public void Validate_DevRepoFullyConfigured_ReturnsNoWarnings()
        {
            DevRepoOnlyGuard.SkipUnlessDevRepo();

            var ctx = new ValidationContext(new List<AssetDataBase> { _dummy });
            var validator = new ProjectSetupValidator();

            var results = validator.Validate(_dummy, ctx).ToList();

            Assert.IsEmpty(results, "開発リポジトリは既にセットアップ済みのため Warning が出ないこと: " +
                string.Join(", ", results.Select(r => $"{r.Code}:{r.Message}")));
        }

        // [48_p11_install_test_2026-09-20.md] フォローアップ(2026-09-20 修正) — このテストの本旨は
        // 「同じ ValidationContext で 2 回目は必ず空(= 重複報告しない)」ことで、`ProjectSetupValidator`
        // の `_reportedForCtx` ガードにより 1 回目に何件警告が出るか(= 環境依存)に関係なく成立する。
        // もとは 1 回目も Warning 0 件であることを assert していたため、持ち込み先の未セットアップな
        // 環境では 1 回目が非 0 件になって Fail していた。DevRepoOnly にする必要は無く、1 回目の件数を
        // 見ないことで環境非依存にできる。
        [Test]
        public void Validate_SecondCallSameContext_DoesNotDuplicateReport()
        {
            var ctx = new ValidationContext(new List<AssetDataBase> { _dummy });
            var validator = new ProjectSetupValidator();

            validator.Validate(_dummy, ctx).ToList();
            var second = validator.Validate(_dummy, ctx).ToList();

            Assert.IsEmpty(second, "同じ ValidationContext で 2 回呼んでも重複報告しないこと(2 回目は必ず空)");
        }

        [Test]
        public void Target_IsNone()
        {
            Assert.AreEqual(DDrive.Foundation.Identity.AssetType.None, new ProjectSetupValidator().Target);
        }

        // [42_distribution.md] §6 P-8(2026-09-20) — 「更新が未適用」の Warning(DD-SETUP-UPDATE-PENDING)。
        // 開発リポジトリ(IsDevelopmentRepo=true)は対象外にしているため、フィールドを直接書き換えて
        // (プロパティのセッターを経由しない = Save を呼ばない)一時的に「持ち込み先」を模擬する。
        // ディスクの ProjectSettings/DDriveProjectSettings.asset には一切書き込まれない(finally で復元)。
        [Test]
        public void Validate_NotDevelopmentRepo_WithOlderLastAppliedVersion_ReturnsUpdatePendingWarning()
        {
            var settings = DDriveProjectSettings.instance;
            var isDevField = typeof(DDriveProjectSettings).GetField("_isDevelopmentRepo", BindingFlags.NonPublic | BindingFlags.Instance);
            var lastAppliedField = typeof(DDriveProjectSettings).GetField("_lastAppliedVersion", BindingFlags.NonPublic | BindingFlags.Instance);
            var originalIsDev = (bool)isDevField.GetValue(settings);
            var originalLastApplied = (string)lastAppliedField.GetValue(settings);

            try
            {
                isDevField.SetValue(settings, false);
                lastAppliedField.SetValue(settings, "0.0.1");

                var ctx = new ValidationContext(new List<AssetDataBase> { _dummy });
                var validator = new ProjectSetupValidator();
                var results = validator.Validate(_dummy, ctx).ToList();

                Assert.IsTrue(results.Any(r => r.Code == "DD-SETUP-UPDATE-PENDING"),
                    "前回適用した版(0.0.1)が現在の版より古いので Warning が出ること: " + string.Join(", ", results.Select(r => $"{r.Code}:{r.Message}")));
            }
            finally
            {
                isDevField.SetValue(settings, originalIsDev);
                lastAppliedField.SetValue(settings, originalLastApplied);
            }
        }

        [Test]
        public void Validate_NotDevelopmentRepo_WithEmptyLastAppliedVersion_ReturnsUpdatePendingWarning()
        {
            var settings = DDriveProjectSettings.instance;
            var isDevField = typeof(DDriveProjectSettings).GetField("_isDevelopmentRepo", BindingFlags.NonPublic | BindingFlags.Instance);
            var lastAppliedField = typeof(DDriveProjectSettings).GetField("_lastAppliedVersion", BindingFlags.NonPublic | BindingFlags.Instance);
            var originalIsDev = (bool)isDevField.GetValue(settings);
            var originalLastApplied = (string)lastAppliedField.GetValue(settings);

            try
            {
                isDevField.SetValue(settings, false);
                lastAppliedField.SetValue(settings, string.Empty);

                var ctx = new ValidationContext(new List<AssetDataBase> { _dummy });
                var validator = new ProjectSetupValidator();
                var results = validator.Validate(_dummy, ctx).ToList();

                Assert.IsTrue(results.Any(r => r.Code == "DD-SETUP-UPDATE-PENDING"),
                    "「未適用」(空文字)のときも Warning が出ること");
            }
            finally
            {
                isDevField.SetValue(settings, originalIsDev);
                lastAppliedField.SetValue(settings, originalLastApplied);
            }
        }
    }
}
