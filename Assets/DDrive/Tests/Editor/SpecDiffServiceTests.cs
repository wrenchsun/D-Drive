using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Spec;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;

namespace DDrive.Tests.Editor
{
    // ネットワークに出ず、CSV 文字列を直接注入して差分・適用を検証する([27_spec_sheet.md] 5-13 の要件)。
    public class SpecDiffServiceTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempSpecGameData";
        private const string AssetHeader = "種別,カテゴリ,識別子,表示名,状態,担当,仕様,備考\n";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                AssetDatabase.SaveAssets();
            }
        }

        // 識別子は実プロジェクトの既存アセット(例: docs の例として実在する SE_Player_Slash)と
        // 絶対に衝突しない専用の接頭辞にする(衝突すると「新規」のはずが「変更」に化けて誤検出する)。
        private const string TestIdentifier = "SpecDiffTestAlpha";

        [Test]
        public void ComputeDiff_UnmatchedIdentifier_IsNew()
        {
            var csv = AssetHeader + $"Se,Player,{TestIdentifier},剣の斬撃音,仮,,,\n";
            var parsed = SpecSheetParser.ParseAssetSheet(csv);

            var diff = SpecDiffService.ComputeDiff(parsed);

            Assert.IsTrue(diff.New.Any(c => c.Row.Identifier == TestIdentifier && c.Row.Type == AssetType.Se));
        }

        [Test]
        public void ComputeDiff_MatchingExistingAsset_WithNoFieldDifference_IsNotChanged()
        {
            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "剣の斬撃音", "Player", TestIdentifier, gameDataRoot: TestRoot);
            Assert.IsNotNull(asset);

            var csv = AssetHeader + $"Se,Player,{TestIdentifier},剣の斬撃音,,,,\n";
            var parsed = SpecSheetParser.ParseAssetSheet(csv);

            var diff = SpecDiffService.ComputeDiff(parsed);

            Assert.IsFalse(diff.New.Any(c => c.Row.Identifier == TestIdentifier));
            Assert.IsFalse(diff.Changed.Any(c => c.Row.Identifier == TestIdentifier));
        }

        [Test]
        public void ComputeDiff_MatchingExistingAsset_WithFieldDifference_IsChanged()
        {
            AssetCreationService.Create(typeof(SeData), AssetType.Se, "剣の斬撃音(旧)", "Player", TestIdentifier, gameDataRoot: TestRoot);

            var csv = AssetHeader + $"Se,Player,{TestIdentifier},剣の斬撃音(新),仮,よしだ,https://example/spec,3段階で音程を変える\n";
            var parsed = SpecSheetParser.ParseAssetSheet(csv);

            var diff = SpecDiffService.ComputeDiff(parsed);

            var change = diff.Changed.SingleOrDefault(c => c.Row.Identifier == TestIdentifier);
            Assert.IsNotNull(change);
            CollectionAssert.Contains(change.ChangedFields, "表示名");
            CollectionAssert.Contains(change.ChangedFields, "状態");
            CollectionAssert.Contains(change.ChangedFields, "担当");
            CollectionAssert.Contains(change.ChangedFields, "備考");
            CollectionAssert.Contains(change.ChangedFields, "仕様リンク");
        }

        [Test]
        public void ApplyChanged_UpdatesOnlyAllowedFields()
        {
            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "剣の斬撃音(旧)", "Player", TestIdentifier, gameDataRoot: TestRoot);

            var csv = AssetHeader + $"Se,Enemy,{TestIdentifier},剣の斬撃音(新),仮,よしだ,https://example/spec,3段階で音程を変える\n";
            var parsed = SpecSheetParser.ParseAssetSheet(csv);
            var diff = SpecDiffService.ComputeDiff(parsed);
            var change = diff.Changed.Single(c => c.Row.Identifier == TestIdentifier);

            SpecSyncService.ApplyChanged(change);

            Assert.AreEqual("剣の斬撃音(新)", asset.DisplayName);
            Assert.AreEqual("Enemy", asset.Category);
            Assert.AreEqual("仮", SpecStatusTag.GetCurrent(asset.Tags));
            Assert.AreEqual("よしだ", asset.Assignee);
            Assert.AreEqual("3段階で音程を変える", asset.Description);
            Assert.AreEqual("https://example/spec", asset.SpecUrl);
        }

        [Test]
        public void ApplyNew_CreatesPlaceholderWithExtraFields()
        {
            var csv = AssetHeader + $"Se,Player,{TestIdentifier},剣の斬撃音,仮,よしだ,https://example/spec,備考テスト\n";
            var parsed = SpecSheetParser.ParseAssetSheet(csv);
            var diff = SpecDiffService.ComputeDiff(parsed);
            var change = diff.New.Single(c => c.Row.Identifier == TestIdentifier);

            var asset = SpecSyncService.ApplyNew(change, TestRoot);

            Assert.IsNotNull(asset);
            Assert.AreEqual("剣の斬撃音", asset.DisplayName);
            Assert.AreEqual("仮", SpecStatusTag.GetCurrent(asset.Tags));
            Assert.AreEqual("よしだ", asset.Assignee);
            Assert.AreEqual("備考テスト", asset.Description);
            Assert.AreEqual("https://example/spec", asset.SpecUrl);
        }

        [Test]
        public void ApplyNew_AmbiguousDataType_ReturnsNullAndDoesNotThrow()
        {
            const string identifier = "SpecDiffTestSkin";
            var csv = AssetHeader + $"ControlSkin,Button,{identifier},主要ボタン,仮,,,\n";
            var parsed = SpecSheetParser.ParseAssetSheet(csv);
            var diff = SpecDiffService.ComputeDiff(parsed);
            var change = diff.New.Single(c => c.Row.Identifier == identifier);

            AssetDataBase asset = null;
            Assert.DoesNotThrow(() => asset = SpecSyncService.ApplyNew(change, TestRoot));
            Assert.IsNull(asset, "ButtonSkinData/SliderSkinData のどちらか一意に決められないため作成しない");
        }

        [Test]
        public void ComputeDiff_MissingFromSheet_WithSpecUrlSet_IsArchiveCandidate()
        {
            const string oldIdentifier = "SpecDiffTestOld";
            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "旧アセット", "Player", oldIdentifier, gameDataRoot: TestRoot);
            asset.SpecUrl = "https://example/spec"; // 一度でも同期された印
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            var csv = AssetHeader + "Se,Player,SpecDiffTestOther,別のアセット,仮,,,\n";
            var parsed = SpecSheetParser.ParseAssetSheet(csv);

            var diff = SpecDiffService.ComputeDiff(parsed);

            Assert.IsTrue(diff.Archived.Any(a => a.Identifier == oldIdentifier && a.Type == AssetType.Se));
        }

        [Test]
        public void ComputeDiff_MissingFromSheet_WithoutSpecUrl_IsNotArchiveCandidate()
        {
            const string manualIdentifier = "SpecDiffTestManual";
            AssetCreationService.Create(typeof(SeData), AssetType.Se, "手動作成", "Player", manualIdentifier, gameDataRoot: TestRoot);

            var csv = AssetHeader + "Se,Player,SpecDiffTestOther,別のアセット,仮,,,\n";
            var parsed = SpecSheetParser.ParseAssetSheet(csv);

            var diff = SpecDiffService.ComputeDiff(parsed);

            Assert.IsFalse(diff.Archived.Any(a => a.Identifier == manualIdentifier));
        }
    }
}
