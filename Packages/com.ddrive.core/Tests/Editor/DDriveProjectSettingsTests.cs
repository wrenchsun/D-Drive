using System.Reflection;
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

        // [42_distribution.md] §4.3/§6 P-7(2026-09-20) — LastAppliedVersion/AppliedMigrationIds は
        // 実 ProjectSettings/DDriveProjectSettings.asset(git 管理下)に永続化される。
        // AppliedMigrationIds に削除 API が無い(台帳は増える一方でよい設計)ため、読み取り専用の
        // フィールド確認はリフレクションで行い Save を発生させない(ディスクに一切触れない)。
        [Test]
        public void HasAppliedMigration_And_AppliedMigrationIds_ReflectUnderlyingField()
        {
            var settings = DDriveProjectSettings.instance;
            var field = typeof(DDriveProjectSettings).GetField("_appliedMigrationIds", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "_appliedMigrationIds フィールドが見つかりません(実装が変わっていないか確認)。");

            var original = (string[])field.GetValue(settings);
            try
            {
                field.SetValue(settings, new[] { "test-only-p7-migration-id" });

                Assert.IsTrue(settings.HasAppliedMigration("test-only-p7-migration-id"));
                Assert.IsFalse(settings.HasAppliedMigration("test-only-p7-migration-id-does-not-exist"));
                CollectionAssert.AreEqual(new[] { "test-only-p7-migration-id" }, settings.AppliedMigrationIds);
            }
            finally
            {
                // Save を経由しない(field.SetValue のみ)ため、ディスク上の
                // ProjectSettings/DDriveProjectSettings.asset には一切書き込まれていない。
                field.SetValue(settings, original);
            }
        }

        [Test]
        public void HasAppliedMigration_WithNullOrEmptyId_ReturnsFalse()
        {
            var settings = DDriveProjectSettings.instance;
            Assert.IsFalse(settings.HasAppliedMigration(null));
            Assert.IsFalse(settings.HasAppliedMigration(string.Empty));
        }

        // MarkMigrationApplied / LastAppliedVersion のセッターは実際に Save(true) を呼ぶため、
        // このテストだけは一時的に実ファイルへ書き込む。finally で元のフィールド値へ戻したうえで
        // 「復元後の値」で再度 Save を発生させ(LastAppliedVersion セッター経由)、ディスクの内容も
        // 元通りにする(git diff に残さない)。
        [Test]
        public void MarkMigrationApplied_IsIdempotent_ThenRestoresPersistedState()
        {
            var settings = DDriveProjectSettings.instance;
            var appliedIdsField = typeof(DDriveProjectSettings).GetField("_appliedMigrationIds", BindingFlags.NonPublic | BindingFlags.Instance);
            var lastAppliedBefore = settings.LastAppliedVersion;
            var appliedIdsBefore = (string[])appliedIdsField.GetValue(settings);

            try
            {
                Assert.IsFalse(settings.HasAppliedMigration("test-only-p7-migration-id"));

                settings.MarkMigrationApplied("test-only-p7-migration-id");
                Assert.IsTrue(settings.HasAppliedMigration("test-only-p7-migration-id"));

                var countAfterFirst = settings.AppliedMigrationIds.Count;
                settings.MarkMigrationApplied("test-only-p7-migration-id");
                Assert.AreEqual(countAfterFirst, settings.AppliedMigrationIds.Count, "同じ Id の再記録は増えない(冪等)");

                settings.LastAppliedVersion = "9.9.9-test-only";
                Assert.AreEqual("9.9.9-test-only", settings.LastAppliedVersion);
            }
            finally
            {
                // まずフィールドを直接メモリ上で元に戻し、
                appliedIdsField.SetValue(settings, appliedIdsBefore);
                // その状態のまま LastAppliedVersion のセッター(内部で Save(true) する)を通して
                // ディスクへも復元後の内容(元の AppliedMigrationIds + 元の LastAppliedVersion)を書き戻す。
                settings.LastAppliedVersion = lastAppliedBefore;

                Assert.IsFalse(settings.HasAppliedMigration("test-only-p7-migration-id"), "テスト後は元の状態に復元されている");
            }
        }

        // [42_distribution.md] §4.2/§6 P-14(2026-09-20) — 更新ウィンドウの「更新チェック」が manifest を
        // 差し替える直前に退避する値。セッターは実際に Save(true) を呼ぶため、上と同じく finally で
        // 実ファイルの内容を元に戻す。
        [Test]
        public void PreviousPackageRef_DefaultsToEmpty_AndRoundTrips_ThenRestoresPersistedState()
        {
            var settings = DDriveProjectSettings.instance;
            var before = settings.PreviousPackageRef;

            try
            {
                settings.PreviousPackageRef = "git+https://github.com/wrenchsun/D-Drive.git?path=Packages/com.ddrive.core#v1.0.0";
                Assert.AreEqual(
                    "git+https://github.com/wrenchsun/D-Drive.git?path=Packages/com.ddrive.core#v1.0.0",
                    settings.PreviousPackageRef);

                settings.PreviousPackageRef = null;
                Assert.AreEqual(string.Empty, settings.PreviousPackageRef, "null を設定すると空文字になる(LastAppliedVersion と同じ流儀)");
            }
            finally
            {
                settings.PreviousPackageRef = before;
            }
        }
    }
}
