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
    // DDriveSpecSettings はプロジェクトに 1 個だけの実 SO のため、テスト前後で状態を保存/復元する。
    // 識別子は実プロジェクトの既存アセットと衝突しない専用の接頭辞 "ZzTest5016" を使う(過去に本番アセットを
    // 汚した事故があったため)。
    public class NewAssetDialogSpecPickerTests
    {
        private const string AssetHeader = "種別,カテゴリ,識別子,表示名,状態,担当,仕様,備考\n";

        private SpecParseResult<SpecAssetRow> _prevAssetRows;
        private SpecParseResult<SpecTuningRow> _prevTuningRows;
        private SpecDiffResult _prevDiff;
        private string _prevWarning;
        private string _prevError;

        private bool _settingsAssetExistedBefore;
        private bool _settingsFolderExistedBefore;
        private string _prevSpreadsheetUrl;

        private const string SettingsFolder = "Assets/GameData/Settings";

        [SetUp]
        public void SetUp()
        {
            _prevAssetRows = SpecCache.LastAssetRows;
            _prevTuningRows = SpecCache.LastTuningRows;
            _prevDiff = SpecCache.LastDiff;
            _prevWarning = SpecCache.LastWarning;
            _prevError = SpecCache.LastError;

            var existing = DDriveSpecSettings.Load();
            _settingsAssetExistedBefore = existing != null;
            _prevSpreadsheetUrl = existing != null ? existing.SpreadsheetUrl : null;
            // GetOrCreate() が初回に Assets/GameData/Settings フォルダを新設する場合があるため、
            // 元々あったかどうかも記録して後始末する(このプロジェクトはまだ一度も仕様書同期を
            // 試していないため、初回はフォルダ自体が無い。docs/28 の要判断も参照)。
            _settingsFolderExistedBefore = AssetDatabase.IsValidFolder(SettingsFolder);

            // 「仕様書から選ぶ」の一覧を表示させるには URL が非空である必要がある(空だと案内文だけの分岐になる)。
            // 取得自体は行わない(SpecCache は直接 Set するのでネットへは出ない)。
            var settings = DDriveSpecSettings.GetOrCreate();
            settings.SpreadsheetUrl = "https://example.com/ddrive-test-5016-spec";
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<NewAssetDialog>())
            {
                window.Close();
            }

            SpecCache.Set(_prevAssetRows, _prevTuningRows, _prevDiff, _prevWarning, _prevError);

            if (_settingsAssetExistedBefore)
            {
                var settings = DDriveSpecSettings.Load();
                if (settings != null)
                {
                    settings.SpreadsheetUrl = _prevSpreadsheetUrl;
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                }
            }
            else if (AssetDatabase.LoadAssetAtPath<DDriveSpecSettings>(DDriveSpecSettings.DefaultPath) != null)
            {
                AssetDatabase.DeleteAsset(DDriveSpecSettings.DefaultPath);
                AssetDatabase.SaveAssets();

                // このテストで初めて作られたフォルダなら、空になったはずなので後始末する
                // (AssetDatabase.FindAssets はフィルタ空文字だと不安定なため、ファイルシステムで直接確認する)。
                if (!_settingsFolderExistedBefore && AssetDatabase.IsValidFolder(SettingsFolder))
                {
                    var hasOtherContents = System.IO.Directory.Exists(SettingsFolder)
                        && System.IO.Directory.GetFileSystemEntries(SettingsFolder).Any(p => !p.EndsWith(".meta"));
                    if (!hasOtherContents)
                    {
                        AssetDatabase.DeleteAsset(SettingsFolder);
                    }
                }
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
            // このテストだけ URL を空に戻して「未設定」分岐を確認する(他テストの SetUp が入れた URL を上書き)。
            var settings = DDriveSpecSettings.GetOrCreate();
            settings.SpreadsheetUrl = string.Empty;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

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
        // NewAssetDialog.CreateAsset は gameDataRoot を差し替えられない(常に Assets/GameData に作る)ため、
        // このテストだけ実際に Assets/GameData 配下へアセットを作り、カタログ/Addressables エントリも含めて
        // 確実に後始末する(要判断: docs/28 参照)。
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

            try
            {
                Assert.AreEqual("仮", SpecStatusTag.GetCurrent(asset.Tags));
                Assert.AreEqual("よしだ", asset.Assignee);
                Assert.AreEqual("備考テスト", asset.Description);
                Assert.AreEqual("https://example/spec", asset.SpecUrl);

                Assert.IsFalse(SpecCache.GetUncreatedRows(AssetType.Se).Any(r => r.Identifier == identifier),
                    "作成後は「仕様書から選ぶ」一覧から消えるはず(SpecCache.RecomputeDiff)");
            }
            finally
            {
                CleanUpRealAsset(asset, AssetType.Se);
            }
        }

        // Assets/GameData 配下に実際に作られたテスト用アセットを、カタログ登録・Addressables エントリも
        // 含めて元に戻す(このダイアログには TestRoot を渡す口が無いため、他の Spec テストのように
        // gameDataRoot: TestRoot で隔離できない。要判断: docs/28 参照)。
        private static void CleanUpRealAsset(AssetDataBase asset, AssetType assetType)
        {
            var path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var catalogPath = $"{AssetCreationService.DefaultGameDataRoot}/Catalogs/{AssetCreationService.GetCatalogName(assetType)}.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>(catalogPath);
            if (catalog != null)
            {
                var filtered = catalog.Entries.Where(e => e.Id != asset.Id).ToList();
                catalog.SetEntries(filtered);
                EditorUtility.SetDirty(catalog);
            }

            AddressablesSync.RemoveEntry(asset);
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
        }
    }
}
