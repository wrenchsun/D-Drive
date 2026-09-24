using System;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Validation;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Anim2D;
using DDrive.Runtime.Audio;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Haptics;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using DDrive.Runtime.Prefab;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Ui;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEditor;

namespace DDrive.Tests.Editor
{
    // [02] §11 / [09] §1 — 作成 → カタログ → Addressables が一つの導線になっていることの検証。
    public class AddressablesRegistrationValidatorTests
    {
        private const string TestRoot = TestTempFolder.Root + "/TempGameDataAddr";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); } // Addressables 設定の dirty を後続テストに持ち越さない
            }
        }

        private static List<ValidationResult> Run(AssetDataBase asset)
        {
            var validator = new AddressablesRegistrationValidator { IncludeTestFolders = true };
            return new List<ValidationResult>(validator.Validate(asset, new ValidationContext(new List<AssetDataBase> { asset })));
        }

        private static int Errors(List<ValidationResult> results)
        {
            var n = 0;
            foreach (var r in results)
            {
                if (r.Severity == ValidationSeverity.Error)
                {
                    n++;
                }
            }

            return n;
        }

        [Test]
        public void Create_RegistersAddressablesEntry_AndValidatorPasses()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "検証用", "Test", "AddrCheck", gameDataRoot: TestRoot);
            Assert.IsNotNull(asset);

            var entry = AddressablesSync.FindEntry(asset);
            Assert.IsNotNull(entry, "作成時に Addressables へ登録される");
            Assert.AreEqual("SE_Test_AddrCheck", entry.address, "address はカタログの Address(ファイル名)と一致する");

            Assert.AreEqual(0, Errors(Run(asset)));
        }

        [Test]
        public void MissingEntry_IsError_AndFixActionRegisters()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "検証用", "Test", "AddrMissing", gameDataRoot: TestRoot);
            Assert.IsTrue(AddressablesSync.RemoveEntry(asset));

            var results = Run(asset);
            Assert.AreEqual(1, Errors(results), "Addressables 未登録は Error");
            Assert.IsNotNull(results[0].FixAction);

            results[0].FixAction();
            Assert.IsNotNull(AddressablesSync.FindEntry(asset));
            Assert.AreEqual(0, Errors(Run(asset)));
        }

        [Test]
        public void AddressMismatch_IsError_AndFixActionRepairs()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "検証用", "Test", "AddrWrong", gameDataRoot: TestRoot);
            var entry = AddressablesSync.FindEntry(asset);
            entry.SetAddress("wrong_address", false);

            var results = Run(asset);
            Assert.AreEqual(1, Errors(results));
            StringAssert.Contains("不一致", results[0].Message);

            results[0].FixAction();
            Assert.AreEqual("SE_Test_AddrWrong", AddressablesSync.FindEntry(asset).address);
        }

        [Test]
        public void CatalogRegistered_WithLabel()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            AssetCreationService.Create(typeof(SeData), AssetType.Se, "検証用", "Test", "AddrCatalog", gameDataRoot: TestRoot);
            var catalog = AssetDatabase.LoadAssetAtPath<DDrive.Foundation.Registry.AssetCatalog>($"{TestRoot}/Catalogs/AudioCatalog.asset");
            Assert.IsNotNull(catalog);

            var entry = AddressablesSync.FindEntry(catalog);
            Assert.IsNotNull(entry, "カタログも Addressables に登録される");
            Assert.IsTrue(entry.labels.Contains(AddressablesSync.CatalogLabel), "起動時にラベルで集められる");
        }

        // U-20([39_usability_fixes_2026-09-17.md]) — Anim2D.Play(ID 版)は AnimManager.Play → ResolveOrPlaceholder
        // でしか解決しないため、Flags.Load が Preload でない既存 Anim2DData(このバグ修正より前に作られた物)は
        // Placeholder(Events 空)になり SE/VFX が鳴らない/出ない。Validator がそれを検出・修正できることを確認する。
        [Test]
        public void Anim2DAsset_CreatedWithPreload_AndFlagsLoadRegression_IsDetectedAndFixed()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var asset = (Anim2DData)AssetCreationService.Create(typeof(Anim2DData), AssetType.Anim2D, "検証用", "Test", "AddrAnim2D", gameDataRoot: TestRoot);
            Assert.IsNotNull(asset);
            Assert.AreEqual(LoadMode.Preload, asset.Flags.Load, "U-20 修正後は作成時点で既定 Preload になっているはず");
            Assert.AreEqual(0, Errors(Run(asset)));

            // このバグ修正より前に作られた既存アセット(Flags.Load=LazyLoad のまま)を模す。
            var flags = asset.Flags;
            flags.Load = LoadMode.LazyLoad;
            asset.Flags = flags;
            EditorUtility.SetDirty(asset);

            var results = Run(asset);
            Assert.AreEqual(1, Errors(results), "Flags.Load が Preload でない Anim2DData は Error");
            StringAssert.Contains("Preload", results[0].Message);

            results[0].FixAction();
            Assert.AreEqual(LoadMode.Preload, asset.Flags.Load, "FixAction で Preload に書き戻る");
            Assert.AreEqual(0, Errors(Run(asset)));
        }

        // 2026-09-17: NeedsPreload の対象リストが AssetCreationService.Create() と別々に持たれていたため、
        // Presentation/Shake/Haptics(2026-09-14 に Create() 側だけへ追加済みだった)が Validator に反映されて
        // おらず、既存アセットの Flags.Load 退行を検出できない抜けがあった([07_canvas_prefab.md] 追記参照)。
        // AssetCreationService.NeedsPreloadDefault への一本化後も同じ検出ができることを型ごとに確認する。
        [TestCase(typeof(PresentationData), AssetType.Presentation, "AddrPresentation")]
        [TestCase(typeof(CameraShakeData), AssetType.Shake, "AddrShake")]
        [TestCase(typeof(HapticsData), AssetType.Haptics, "AddrHaptics")]
        public void PresentationShakeHaptics_FlagsLoadRegression_IsDetectedAndFixed(
            System.Type dataType, AssetType assetType, string identifier)
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var asset = AssetCreationService.Create(dataType, assetType, "検証用", "Test", identifier, gameDataRoot: TestRoot);
            Assert.IsNotNull(asset);
            Assert.AreEqual(LoadMode.Preload, asset.Flags.Load, "作成時点で既定 Preload になっているはず");
            Assert.AreEqual(0, Errors(Run(asset)));

            // このバグ修正より前に作られた既存アセット(Flags.Load=LazyLoad のまま)を模す。
            var flags = asset.Flags;
            flags.Load = LoadMode.LazyLoad;
            asset.Flags = flags;
            EditorUtility.SetDirty(asset);

            var results = Run(asset);
            Assert.AreEqual(1, Errors(results), $"Flags.Load が Preload でない {assetType} は Error");
            StringAssert.Contains("Preload", results[0].Message);

            results[0].FixAction();
            Assert.AreEqual(LoadMode.Preload, asset.Flags.Load, "FixAction で Preload に書き戻る");
            Assert.AreEqual(0, Errors(Run(asset)));
        }

        // [M-1a、2026-09-25] MS2026 の実機テストで Prefab/Audio/Vfx/Material の 11 件が「未登録 → Placeholder」
        // になる不具合が見つかった(TeamNotes 2026-09-25)。Se/Bgm/Vfx/Material/Texture/Prefab/UiTween/Model/
        // Anchor/AnchorGroup の Manager 公開 API を grep して調査した結果、いずれも同期解決のみで非同期の
        // 代替経路が無かったため、`NeedsPreloadDefault` に追加した。既存アセットの Flags.Load 退行を
        // 検出・修正できることを型ごとに確認する(Presentation/Shake/Haptics と同じテスト方式)。
        [TestCase(typeof(SeData), AssetType.Se, "AddrM1aSe")]
        [TestCase(typeof(BgmData), AssetType.Bgm, "AddrM1aBgm")]
        [TestCase(typeof(VfxData), AssetType.Vfx, "AddrM1aVfx")]
        [TestCase(typeof(MaterialData), AssetType.Material, "AddrM1aMaterial")]
        [TestCase(typeof(TextureData), AssetType.Texture, "AddrM1aTexture")]
        [TestCase(typeof(PrefabData), AssetType.Prefab, "AddrM1aPrefab")]
        [TestCase(typeof(UiTweenData), AssetType.UiTween, "AddrM1aUiTween")]
        [TestCase(typeof(ModelData), AssetType.Model, "AddrM1aModel")]
        [TestCase(typeof(AnchorData), AssetType.Anchor, "AddrM1aAnchor")]
        [TestCase(typeof(AnchorGroupData), AssetType.AnchorGroup, "AddrM1aAnchorGroup")]
        public void M1a_SyncOnlyTypes_FlagsLoadRegression_IsDetectedAndFixed(
            System.Type dataType, AssetType assetType, string identifier)
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var asset = AssetCreationService.Create(dataType, assetType, "検証用", "Test", identifier, gameDataRoot: TestRoot);
            Assert.IsNotNull(asset);
            Assert.AreEqual(LoadMode.Preload, asset.Flags.Load, "M-1a 修正後は作成時点で既定 Preload になっているはず");
            Assert.AreEqual(0, Errors(Run(asset)));

            // このバグ修正より前に作られた既存アセット(Flags.Load=LazyLoad のまま)を模す。
            var flags = asset.Flags;
            flags.Load = LoadMode.LazyLoad;
            asset.Flags = flags;
            EditorUtility.SetDirty(asset);

            var results = Run(asset);
            Assert.AreEqual(1, Errors(results), $"Flags.Load が Preload でない {assetType} は Error");
            StringAssert.Contains("Preload", results[0].Message);
            Assert.AreEqual("DD-ADDR-PRELOAD-REQUIRED", results[0].Code);

            results[0].FixAction();
            Assert.AreEqual(LoadMode.Preload, asset.Flags.Load, "FixAction で Preload に書き戻る");
            Assert.AreEqual(0, Errors(Run(asset)));
        }

        // NeedsPreloadDefault が「全種別を Error にした」ことを保証する回帰テスト
        // (2026-09-25 時点のコード調査ではどの種別も非同期の代替経路を持たなかったため)。
        // 将来 AssetType に値を追加したときにこのテストが失敗したら、その種別の Manager を調査して
        // NeedsPreloadDefault か TryGetAsyncResolutionApi のどちらに属すか判断すること。
        [Test]
        public void NeedsPreloadDefault_IsTrueForAllKnownAssetTypes()
        {
            foreach (AssetType type in Enum.GetValues(typeof(AssetType)))
            {
                if (type == AssetType.None)
                {
                    continue;
                }

                Assert.IsTrue(AssetCreationService.NeedsPreloadDefault(type),
                    $"{type} は同期解決のみのはず(2026-09-25 時点の調査結果)。非同期の代替経路が見つかったなら " +
                    "TryGetAsyncResolutionApi へ登録し、このテストの例外として扱うこと。");
            }
        }

        // Warning 分岐(非同期の代替経路を持つ種別)のテスト専用データ。AssetIdDefinitionAttribute を
        // 意図的に付けない(ResolveType が AssetType.None を返す = NeedsPreloadDefault の対象外になる)。
        private sealed class FakeAsyncCapableData : AssetDataBase
        {
        }

        // [M-1a、2026-09-25] DD-ADDR-PRELOAD-RECOMMENDED(Warning)は、NeedsPreloadDefault が false かつ
        // TryGetAsyncResolutionApi に登録された種別だけで発火する。2026-09-25 時点の調査ではそのような
        // 実在の種別が無かった(全種別が同期解決のみ)ため、内部テーブル(AssetCreationService の
        // private static Dictionary)へリフレクションで一時的に登録し、Validator の Warning 分岐そのものを
        // 実際に Validate() を呼んで検証する(公開 API は増やさない最小限のテスト用フック)。
        [Test]
        public void PreloadRecommendedWarning_FiresWhenTypeHasRegisteredAsyncResolutionApi()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var field = typeof(AssetCreationService).GetField("AsyncResolutionApiByType", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, "AsyncResolutionApiByType フィールドが見つかりません(実装が変わっていないか確認)。");
            var table = (Dictionary<AssetType, string>)field.GetValue(null);

            // FakeAsyncCapableData には AssetIdDefinitionAttribute が無いため、Validator.ResolveType は
            // AssetType.None を返す(NeedsPreloadDefault(None) は常に false)。この型を対象に一時登録する。
            const AssetType fakeType = AssetType.None;
            const string fakeApiName = "Test.FakeAsyncApi";
            table[fakeType] = fakeApiName;

            try
            {
                var asset = AssetCreationService.Create(typeof(FakeAsyncCapableData), AssetType.None, "検証用", "Test", "AddrM1aWarnFake", gameDataRoot: TestRoot);
                Assert.IsNotNull(asset);
                Assert.AreEqual(LoadMode.LazyLoad, asset.Flags.Load, "None は NeedsPreloadDefault の対象外なので既定は LazyLoad のまま");

                var results = Run(asset);
                Assert.AreEqual(0, Errors(results), "非同期の代替経路がある種別は Error にしない");

                var warning = results.Find(r => r.Code == "DD-ADDR-PRELOAD-RECOMMENDED");
                Assert.IsNotNull(warning.Message, "DD-ADDR-PRELOAD-RECOMMENDED の Warning が出ているはず");
                Assert.AreEqual(ValidationSeverity.Warning, warning.Severity);
                StringAssert.Contains(fakeApiName, warning.Message);

                // Preload にすれば Warning も消える。
                var flags = asset.Flags;
                flags.Load = LoadMode.Preload;
                asset.Flags = flags;
                EditorUtility.SetDirty(asset);

                var afterFix = Run(asset);
                Assert.IsFalse(afterFix.Exists(r => r.Code == "DD-ADDR-PRELOAD-RECOMMENDED"));
            }
            finally
            {
                table.Remove(fakeType);
            }
        }
    }
}
