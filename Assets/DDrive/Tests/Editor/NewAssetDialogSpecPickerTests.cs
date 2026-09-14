using System.Linq;
using System.Reflection;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Spec;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Tests.Editor
{
    // 5-16 — NewAssetDialog の「仕様書から選ぶ」。ネットワークには出ない(SpecSheetParser で CSV 文字列を
    // 直接パースし、SpecCache に注入する。実際の取得(SpecFetcher/SpecAutoSync.Run)は呼ばない)。
    // 識別子は実プロジェクトの既存アセットと衝突しない専用の接頭辞 "ZzTest5016" を使う(過去に本番アセットを
    // 汚した事故があったため)。
    //
    // P5 テスト隔離(2026-09-14): NewAssetDialog は元々 DDriveSpecSettings.Load()(実シングルトン
    // Assets/GameData/Settings/DDriveSpecSettings.asset)と AssetCreationService.DefaultGameDataRoot
    // (実 Assets/GameData、実カタログ、実 Addressables グループ)を直接参照していたため、このテストは
    // 実データを一時的に書き換えて後始末する作りになっていた。ImportRuleServiceTests/SpecCacheTests と
    // 同じ「差し替え口を引数化する」流儀に揃え、NewAssetDialog に internal テスト専用フック
    // (TestSpecSettingsOverride / TestGameDataRootOverride)を追加して、実データに一切触れないようにした。
    public class NewAssetDialogSpecPickerTests
    {
        private const string AssetHeader = "種別,カテゴリ,識別子,表示名,状態,担当,仕様,備考\n";
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempNewAssetDialogGameData";

        private SpecParseResult<SpecAssetRow> _prevAssetRows;
        private SpecParseResult<SpecTuningRow> _prevTuningRows;
        private SpecDiffResult _prevDiff;
        private string _prevWarning;
        private string _prevError;

        [SetUp]
        public void SetUp()
        {
            _prevAssetRows = SpecCache.LastAssetRows;
            _prevTuningRows = SpecCache.LastTuningRows;
            _prevDiff = SpecCache.LastDiff;
            _prevWarning = SpecCache.LastWarning;
            _prevError = SpecCache.LastError;

            // 「仕様書から選ぶ」の一覧を表示させるには URL が非空である必要がある(空だと案内文だけの分岐になる)。
            // 実 DDriveSpecSettings.asset には一切触れず、メモリ上だけのインスタンスを差し替える。
            var settings = ScriptableObject.CreateInstance<DDriveSpecSettings>();
            settings.SpreadsheetUrl = "https://example.com/ddrive-test-5016-spec";
            NewAssetDialog.TestSpecSettingsOverride = settings;
            NewAssetDialog.TestGameDataRootOverride = TestRoot;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<NewAssetDialog>())
            {
                window.Close();
            }

            SpecCache.Set(_prevAssetRows, _prevTuningRows, _prevDiff, _prevWarning, _prevError);

            if (NewAssetDialog.TestSpecSettingsOverride != null)
            {
                Object.DestroyImmediate(NewAssetDialog.TestSpecSettingsOverride);
            }

            NewAssetDialog.TestSpecSettingsOverride = null;
            NewAssetDialog.TestGameDataRootOverride = null;

            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                AssetDatabase.SaveAssets();
            }
        }

        private static void SeedCache(string csv)
        {
            var parsed = SpecSheetParser.ParseAssetSheet(csv);
            var diff = SpecDiffService.ComputeDiff(parsed);
            SpecCache.Set(parsed, new SpecParseResult<SpecTuningRow>(), diff, null, null);
        }

        private static NewAssetDialog OpenUnlocked()
        {
            NewAssetDialog.Open();
            return EditorWindow.GetWindow<NewAssetDialog>();
        }

        private static T GetPrivateField<T>(object instance, string name)
        {
            var field = typeof(NewAssetDialog).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"NewAssetDialog.{name} が見つかりません(実装が変わった場合はテストを追従させてください)");
            return (T)field.GetValue(instance);
        }

        private static void InvokePrivate(object instance, string methodName, params object[] args)
        {
            var method = typeof(NewAssetDialog).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"NewAssetDialog.{methodName} が見つかりません(実装が変わった場合はテストを追従させてください)");
            method.Invoke(instance, args);
        }

        private static System.Collections.Generic.List<string> LabelTexts(VisualElement container)
        {
            var texts = new System.Collections.Generic.List<string>();
            container.Query<Label>().ForEach(l => texts.Add(l.text));
            return texts;
        }

        [Test]
        public void SelectingSpecRow_FillsFormFields()
        {
            const string identifier = "ZzTest5016Pick";
            SeedCache(AssetHeader + $"Se,Player,{identifier},テストSE,未着手,よしだ,https://example/spec,備考テスト\n");

            var window = OpenUnlocked();
            var row = SpecCache.GetUncreatedRows(AssetType.Se).Single(r => r.Identifier == identifier);

            InvokePrivate(window, "OnSpecRowSelected", row);

            Assert.AreEqual("Player", GetPrivateField<TextField>(window, "_categoryField").value);
            Assert.AreEqual(identifier, GetPrivateField<TextField>(window, "_identifierField").value);
            Assert.AreEqual("テストSE", GetPrivateField<TextField>(window, "_displayNameField").value);
            Assert.AreEqual("備考テスト", GetPrivateField<TextField>(window, "_noteField").value);
            Assert.AreEqual("https://example/spec", GetPrivateField<TextField>(window, "_specLinkField").value);
        }

        [Test]
        public void RenderSpecList_LockedType_OnlyShowsMatchingType()
        {
            const string seIdentifier = "ZzTest5016LockSe";
            const string vfxIdentifier = "ZzTest5016LockVfx";
            SeedCache(AssetHeader
                + $"Se,Player,{seIdentifier},テストSE,未着手,,,\n"
                + $"Vfx,Skill,{vfxIdentifier},テストVFX,未着手,,,\n");

            AssetDataBase created = null;
            NewAssetDialog.Open(new[] { typeof(SeData) }, a => created = a);
            var window = EditorWindow.GetWindow<NewAssetDialog>();

            var listContainer = GetPrivateField<VisualElement>(window, "_specListContainer");
            Assert.IsNotNull(listContainer);

            var texts = LabelTexts(listContainer);
            Assert.IsTrue(texts.Any(t => t.Contains(seIdentifier)));
            Assert.IsFalse(texts.Any(t => t.Contains(vfxIdentifier)));
            Assert.IsNull(created);
        }

        [Test]
        public void RenderSpecList_SearchFilters_ByDisplayNameOrIdentifier()
        {
            const string matchIdentifier = "ZzTest5016SearchMatch";
            const string otherIdentifier = "ZzTest5016SearchOther";
            SeedCache(AssetHeader
                + $"Se,Player,{matchIdentifier},剣の斬撃音,未着手,,,\n"
                + $"Se,Player,{otherIdentifier},爆発音,未着手,,,\n");

            var window = OpenUnlocked();

            var searchField = GetPrivateField<TextField>(window, "_specSearchField");
            Assert.IsNotNull(searchField);
            searchField.value = "斬撃";

            var listContainer = GetPrivateField<VisualElement>(window, "_specListContainer");
            var texts = LabelTexts(listContainer);
            Assert.IsTrue(texts.Any(t => t.Contains(matchIdentifier)));
            Assert.IsFalse(texts.Any(t => t.Contains(otherIdentifier)));
        }

        [Test]
        public void RebuildSpecSection_NoUncreatedRows_ShowsEmptyMessage_NotError()
        {
            SeedCache(AssetHeader); // ヘッダのみ(0 行)

            var window = OpenUnlocked();
            var listContainer = GetPrivateField<VisualElement>(window, "_specListContainer");

            Assert.IsNotNull(listContainer);
            Assert.Greater(listContainer.childCount, 0); // 「該当する未作成行はありません」ラベル
        }

        [Test]
        public void RebuildSpecSection_NoSpreadsheetUrl_ShowsGuidanceOnly_NoListContainer()
        {
            // このテストだけ URL を空にして「未設定」分岐を確認する(メモリ上のオーバーライドを直接書き換えるだけ)。
            NewAssetDialog.TestSpecSettingsOverride.SpreadsheetUrl = string.Empty;

            var window = OpenUnlocked();

            var listContainer = GetPrivateField<VisualElement>(window, "_specListContainer");
            Assert.IsNull(listContainer, "URL 未設定時は一覧を組み立てないはず");

            var specSection = GetPrivateField<VisualElement>(window, "_specSection");
            var helpBoxCount = 0;
            specSection.Query<HelpBox>().ForEach(_ => helpBoxCount++);
            Assert.Greater(helpBoxCount, 0, "案内文(HelpBox)が出ていない");
        }

        // 5-16 の本丸: ダイアログの「作成」を仕様書の行を選んだ状態で押すと、
        // SpecSyncService.ApplyExtraFields と同じ結果(状態タグ/Assignee/Description/SpecUrl)になり、
        // 作成後はその行が SpecCache の「未作成」一覧から消える。
        // TestGameDataRootOverride(= TestRoot)により実 Assets/GameData には一切書き込まれない。
        // SpecDiffService.BuildExistingIndex はプロジェクト全体を t:AssetDataBase で検索するため、
        // TestRoot 配下に作られたアセットでも「既存」として正しく見つかる。
        [Test]
        public void CreateFromSelectedSpecRow_AppliesExtraFields_AndRemovesRowFromCache()
        {
            const string identifier = "ZzTest5016CreateFromSpec";
            SeedCache(AssetHeader + $"Se,Player,{identifier},テストSE,仮,よしだ,https://example/spec,備考テスト\n");

            var window = OpenUnlocked();
            var row = SpecCache.GetUncreatedRows(AssetType.Se).Single(r => r.Identifier == identifier);
            InvokePrivate(window, "OnSpecRowSelected", row);

            InvokePrivate(window, "CreateAsset");

            var index = SpecDiffService.BuildExistingIndex();
            Assert.IsTrue(index.TryGetValue("Se::" + identifier, out var asset), "作成されたはずのアセットが見つかりません");

            Assert.IsTrue(AssetDatabase.GetAssetPath(asset).StartsWith(TestRoot),
                "テスト用の一時フォルダ配下に作られているはず(実 Assets/GameData には書き込まない)");
            Assert.AreEqual("仮", SpecStatusTag.GetCurrent(asset.Tags));
            Assert.AreEqual("よしだ", asset.Assignee);
            Assert.AreEqual("備考テスト", asset.Description);
            Assert.AreEqual("https://example/spec", asset.SpecUrl);

            Assert.IsFalse(SpecCache.GetUncreatedRows(AssetType.Se).Any(r => r.Identifier == identifier),
                "作成後は「仕様書から選ぶ」一覧から消えるはず(SpecCache.RecomputeDiff)");
        }

        // P5 レビュー対応(2026-09-14) editor #4 — 仕様書の行を選んだ後に識別子を手で書き換えたら、
        // 選択を解除して「作成」時に別アセットへ Status/Assignee が付かないようにする
        // (review1_editor.md #4 の要判断だった部分の修正)。
        [Test]
        public void ManuallyEditingIdentifierAfterSelectingSpecRow_ClearsSelection_AndCreateDoesNotApplyExtraFields()
        {
            const string identifier = "ZzTest5016EditAfterPick";
            const string renamedIdentifier = "ZzTest5016EditAfterPickRenamed";
            SeedCache(AssetHeader + $"Se,Player,{identifier},テストSE,仮,よしだ,https://example/spec,備考テスト\n");

            var window = OpenUnlocked();
            var row = SpecCache.GetUncreatedRows(AssetType.Se).Single(r => r.Identifier == identifier);
            InvokePrivate(window, "OnSpecRowSelected", row);

            Assert.IsNotNull(GetPrivateField<object>(window, "_selectedSpecRow"), "選択直後は選択が保持されているはず");

            // ユーザーが識別子を手で書き換える(表示名・カテゴリでも同様に解除される想定だが、代表として識別子を確認する)。
            var identifierField = GetPrivateField<TextField>(window, "_identifierField");
            identifierField.value = renamedIdentifier;

            Assert.IsNull(GetPrivateField<object>(window, "_selectedSpecRow"), "識別子を手で書き換えたら選択が解除されるはず");

            InvokePrivate(window, "CreateAsset");

            var index = SpecDiffService.BuildExistingIndex();
            Assert.IsTrue(index.TryGetValue("Se::" + renamedIdentifier, out var asset), "書き換えた識別子でアセットが作られているはず");
            Assert.AreEqual(string.Empty, SpecStatusTag.GetCurrent(asset.Tags), "選択解除後の作成なので Status は付かない");
            Assert.IsTrue(string.IsNullOrEmpty(asset.Assignee), "選択解除後の作成なので Assignee も付かない(元の仕様書行のものが誤って付かない)");

            // 選択解除後も、元の仕様書行は「未作成」のまま一覧に残っているはず(手で書き換えた分とは別アセットのため)。
            Assert.IsTrue(SpecCache.GetUncreatedRows(AssetType.Se).Any(r => r.Identifier == identifier),
                "選択解除により、元の仕様書行は SpecCache.RecomputeDiff の対象にならず未作成のまま残るはず");
        }

        [Test]
        public void ClearSelectedSpecRow_Button_RemovesIndicator_AndUnboldsListRow()
        {
            const string identifier = "ZzTest5016ClearButton";
            SeedCache(AssetHeader + $"Se,Player,{identifier},テストSE,未着手,,,\n");

            var window = OpenUnlocked();
            var row = SpecCache.GetUncreatedRows(AssetType.Se).Single(r => r.Identifier == identifier);
            InvokePrivate(window, "OnSpecRowSelected", row);

            var indicator = GetPrivateField<VisualElement>(window, "_selectedSpecRowIndicator");
            Assert.IsNotNull(indicator);
            Assert.Greater(indicator.childCount, 0, "選択中は行の説明 + 解除ボタンが表示されるはず");

            InvokePrivate(window, "ClearSelectedSpecRow");

            Assert.IsNull(GetPrivateField<object>(window, "_selectedSpecRow"));
            Assert.AreEqual(0, indicator.childCount, "解除後はインジケータが空になるはず");
        }
    }
}
