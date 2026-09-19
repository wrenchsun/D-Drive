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
        private const string TestRoot = "Packages/com.ddrive.core/Tests/Editor/TempSpecGameData";
        private const string AssetHeader = "種別,カテゴリ,識別子,表示名,状態,担当,仕様,備考\n";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
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

        // ── 2026-09-17([41] P1-7)の回帰テスト ──
        // Web(GAS)の JSON を SpecWebParser で読み、「受注者・リファレンスが空の発注」を同期しても
        // 既存の Assignee / Description / 状態 / 仕様リンクが空で上書きされないことを確認する。
        // 元の不具合は「D-Drive が旧キー(assignee/note)を読んでいて常に空だった」ことだが、
        // 同じ事故の被害を最小にする防御(空は変更なし)そのものをここで固定する。
        private const string WebIdentifier = "SpecDiffTestWebEmpty";

        private static string WebAssetsJson(string displayName, string status, string contractor, string referenceMd)
            => "{\"ok\":true,\"items\":[{" +
               "\"id\":\"Se::" + WebIdentifier + "\",\"assetType\":\"Se\",\"category\":\"Player\"," +
               "\"identifier\":\"" + WebIdentifier + "\",\"displayName\":\"" + displayName + "\"," +
               "\"status\":\"" + status + "\",\"contractor\":\"" + contractor + "\"," +
               "\"referenceMd\":\"" + referenceMd + "\"}]}";

        private static AssetDataBase CreateWebTestAsset()
        {
            var asset = AssetCreationService.Create(
                typeof(SeData), AssetType.Se, "斬撃音", "Player", WebIdentifier, gameDataRoot: TestRoot);
            asset.Assignee = "よしだ";
            asset.Description = "D-Drive 側で書いた説明";
            asset.Tags = SpecStatusTag.WithStatus(asset.Tags, "納品済");
            asset.SpecUrl = "https://example/spec?page=order&id=Se%3A%3A" + WebIdentifier;
            EditorUtility.SetDirty(asset);
            using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            return asset;
        }

        [Test]
        public void ComputeDiff_WebRowWithEmptyContractorAndReference_IsNotChanged()
        {
            CreateWebTestAsset();
            var parsed = SpecWebParser.ParseAssets(WebAssetsJson("斬撃音", string.Empty, string.Empty, string.Empty));

            var diff = SpecDiffService.ComputeDiff(parsed);

            Assert.IsFalse(diff.Changed.Any(c => c.Row.Identifier == WebIdentifier),
                "Web 側が空の項目は差分に含めない(空で既存値を消さない)");
        }

        [Test]
        public void ApplyChanged_WebRowWithEmptyContractorAndReference_KeepsExistingValues()
        {
            var asset = CreateWebTestAsset();
            // 表示名だけが変わった発注(受注者・リファレンスは未入力)。
            var parsed = SpecWebParser.ParseAssets(WebAssetsJson("斬撃音(改)", string.Empty, string.Empty, string.Empty));
            var diff = SpecDiffService.ComputeDiff(parsed);
            var change = diff.Changed.Single(c => c.Row.Identifier == WebIdentifier);
            CollectionAssert.Contains(change.ChangedFields, "表示名");
            CollectionAssert.DoesNotContain(change.ChangedFields, "担当");
            CollectionAssert.DoesNotContain(change.ChangedFields, "備考");
            CollectionAssert.DoesNotContain(change.ChangedFields, "状態");

            SpecSyncService.ApplyChanged(change);

            Assert.AreEqual("斬撃音(改)", asset.DisplayName, "表示名は反映する");
            Assert.AreEqual("よしだ", asset.Assignee, "担当が空で上書きされていない");
            Assert.AreEqual("D-Drive 側で書いた説明", asset.Description, "説明が空で上書きされていない");
            Assert.AreEqual("納品済", SpecStatusTag.GetCurrent(asset.Tags), "状態タグが消えていない");
            Assert.IsFalse(string.IsNullOrEmpty(asset.SpecUrl), "仕様リンクが消えていない");
        }

        [Test]
        public void ApplyChanged_WebRowWithNewSchemaValues_IsApplied()
        {
            var asset = CreateWebTestAsset();
            var parsed = SpecWebParser.ParseAssets(WebAssetsJson("斬撃音(改)", "インポート済", "たなか", "新しいリファレンス"));
            var diff = SpecDiffService.ComputeDiff(parsed);
            var change = diff.Changed.Single(c => c.Row.Identifier == WebIdentifier);

            SpecSyncService.ApplyChanged(change);

            Assert.AreEqual("斬撃音(改)", asset.DisplayName);
            Assert.AreEqual("たなか", asset.Assignee, "contractor が Assignee に入る");
            Assert.AreEqual("新しいリファレンス", asset.Description, "referenceMd が Description に入る");
            Assert.AreEqual("インポート済", SpecStatusTag.GetCurrent(asset.Tags));
        }

        [Test]
        public void ComputeDiff_MissingFromSheet_WithSpecUrlSet_IsArchiveCandidate()
        {
            const string oldIdentifier = "SpecDiffTestOld";
            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "旧アセット", "Player", oldIdentifier, gameDataRoot: TestRoot);
            asset.SpecUrl = "https://example/spec"; // 一度でも同期された印
            EditorUtility.SetDirty(asset);
            using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }

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
