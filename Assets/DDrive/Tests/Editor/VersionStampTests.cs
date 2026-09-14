using System;
using System.Globalization;
using DDrive.Editor.Inspector;
using DDrive.Editor.Versioning;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] 6-3 — 保存フック(VersionStampProcessor)が Version/Author/UpdatedAt を自動記録すること、
    // 抑止スコープ(VersionStampSuppression)中は加算しないこと、Undo で戻せることを検証する。
    // 実 GameData・カタログ・Addressables には触れない(Tests/Editor 配下の一時アセットのみ。
    // AssetCreationService を経由しないため Addressables 登録も発生しない)。
    public class VersionStampTests
    {
        private const string TempDir = "Assets/DDrive/Tests/Editor/TempVersionStamp";
        private const string AssetPath = TempDir + "/VersionStamp_TestAssetData.asset";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
            {
                AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "TempVersionStamp");
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir))
            {
                AssetDatabase.DeleteAsset(TempDir);
                AssetDatabase.SaveAssets();
            }
        }

        // 作成経路(AssetCreationService.Create)と同じく、CreateAsset の直前に StampNew で v1 を付ける
        // (CreateAsset 直後のアセットは dirty にならず、保存フックでは 0→1 にならないため)。
        private static TestAssetData CreateAndSave()
        {
            var data = ScriptableObject.CreateInstance<TestAssetData>();
            VersionStampProcessor.StampNew(data);
            AssetDatabase.CreateAsset(data, AssetPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<TestAssetData>(AssetPath);
        }

        [Test]
        public void FirstSave_SetsVersionToOne_AndRecordsAuthorAndTimestamp()
        {
            var data = CreateAndSave();

            Assert.AreEqual(1, data.Version, "新規作成 → 初回保存で Version の既定値(0)が 1 になる");
            Assert.AreEqual(Environment.UserName, data.Author);
            Assert.IsTrue(
                DateTime.TryParseExact(data.UpdatedAt, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                $"UpdatedAt は ISO 8601(秒まで)で保存される: '{data.UpdatedAt}'");
        }

        [Test]
        public void CreatedWithoutStamp_FirstEditAndSave_SetsVersionToOne()
        {
            // StampNew を通らない作成経路(Project ウィンドウの Create メニュー等)でも、最初の編集 + 保存で v1 になる。
            var data = ScriptableObject.CreateInstance<TestAssetData>();
            AssetDatabase.CreateAsset(data, AssetPath);
            AssetDatabase.SaveAssets();
            data = AssetDatabase.LoadAssetAtPath<TestAssetData>(AssetPath);
            Assert.AreEqual(0, data.Version, "CreateAsset 直後は dirty ではないため保存フックは動かない");

            Undo.RecordObject(data, "Edit DisplayName");
            data.DisplayName = "Edited";
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            Assert.AreEqual(1, data.Version);
            Assert.AreEqual(Environment.UserName, data.Author);
        }

        [Test]
        public void Save_WithoutChange_DoesNotIncrementVersion()
        {
            var data = CreateAndSave();
            Assert.AreEqual(1, data.Version);
            var updatedAtAfterFirstSave = data.UpdatedAt;

            // dirty ではないので、もう一度 SaveAssets しても何も起きない。
            AssetDatabase.SaveAssets();

            Assert.AreEqual(1, data.Version, "未変更アセットは版数が増えない");
            Assert.AreEqual(updatedAtAfterFirstSave, data.UpdatedAt);
        }

        [Test]
        public void Save_AfterEdit_IncrementsVersionExactlyOnce()
        {
            var data = CreateAndSave();
            Assert.AreEqual(1, data.Version);

            Undo.RecordObject(data, "Edit DisplayName");
            data.DisplayName = "Edited";
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            Assert.AreEqual(2, data.Version, "1 回の保存につき 1 回だけ加算される(二重加算しない)");
        }

        [Test]
        public void Save_WithinSuppressionScope_DoesNotIncrementVersion()
        {
            var data = CreateAndSave();
            Assert.AreEqual(1, data.Version);

            using (VersionStampSuppression.Scope())
            {
                Undo.RecordObject(data, "Bulk Edit");
                data.Category = "BulkTouched";
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();
            }

            Assert.AreEqual(1, data.Version, "抑止スコープ中の保存は版数を上げない");
            Assert.AreEqual("BulkTouched", data.Category, "抑止スコープはフィールドの書き換え自体は妨げない");
        }

        [Test]
        public void SuppressionScope_IsNestingSafe()
        {
            var data = CreateAndSave();

            using (VersionStampSuppression.Scope())
            {
                using (VersionStampSuppression.Scope())
                {
                    Assert.IsTrue(VersionStampSuppression.IsActive);
                }

                Assert.IsTrue(VersionStampSuppression.IsActive, "内側の Dispose だけでは抑止が解除されない");

                Undo.RecordObject(data, "Nested Bulk Edit");
                data.Category = "Nested";
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();
            }

            Assert.IsFalse(VersionStampSuppression.IsActive, "外側の Dispose で抑止が解除される");
            Assert.AreEqual(1, data.Version, "入れ子の間ずっと抑止され続ける");
        }

        [Test]
        public void Save_AfterEdit_UndoRestoresPreviousVersionAuthorAndTimestamp()
        {
            var data = CreateAndSave();
            var versionBefore = data.Version;
            var authorBefore = data.Author;
            var updatedAtBefore = data.UpdatedAt;

            Undo.IncrementCurrentGroup();
            Undo.RecordObject(data, "Edit DisplayName");
            data.DisplayName = "Edited";
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            Assert.AreEqual(versionBefore + 1, data.Version, "編集 + 保存で加算される");

            Undo.PerformUndo();

            Assert.AreEqual(versionBefore, data.Version, "Undo でバージョン加算も戻る");
            Assert.AreEqual(authorBefore, data.Author);
            Assert.AreEqual(updatedAtBefore, data.UpdatedAt);
        }

        // ── 表示フォーマット(Unity アセットを介さない純粋なユニットテスト) ──

        [Test]
        public void FormatTimestamp_ProducesIso8601_AndDisplayFormatDropsSeconds()
        {
            var when = new DateTime(2026, 9, 15, 14, 3, 27);

            var iso = VersionStampProcessor.FormatTimestamp(when);
            Assert.AreEqual("2026-09-15T14:03:27", iso);

            Assert.AreEqual("2026-09-15 14:03", VersionStampGui.FormatForDisplay(iso));
        }

        [Test]
        public void FormatForDisplay_ReturnsRawValue_WhenNotParseable()
        {
            Assert.AreEqual("not-a-date", VersionStampGui.FormatForDisplay("not-a-date"));
            Assert.AreEqual("-", VersionStampGui.FormatForDisplay(null));
        }
    }
}
