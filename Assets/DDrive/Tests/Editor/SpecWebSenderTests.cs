using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Spec;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Tuning;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [32_spec_web.md] §5.2/§8 W-12 — D-Drive → Web 送信 payload の組み立て(ネットワークには出ない)。
    // 実際の送信(SpecWebFetcher.FetchPost)はネットワーク I/O のためここではテストしない
    // (SpecWebFetcherTests がフェイク HTTP で疎通経路を確認する)。ここでは payload の内容だけを検証する。
    public class SpecWebSenderTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempSpecWebSenderGameData";
        private const string TestIdentifier = "SpecWebSenderTestAlpha";

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

        [Test]
        public void BuildChoicesPayload_ContainsAssetTypesAndCreatedAssetCategory()
        {
            AssetCreationService.Create(typeof(SeData), AssetType.Se, "テスト", "SpecWebSenderCategory", TestIdentifier, gameDataRoot: TestRoot);

            var payload = SpecWebSender.BuildChoicesPayload();

            var assetTypes = ((Newtonsoft.Json.Linq.JArray)payload["assetTypes"]).Select(t => (string)t).ToArray();
            CollectionAssert.Contains(assetTypes, "Se");

            var categories = ((Newtonsoft.Json.Linq.JArray)payload["categories"]).Select(t => (string)t).ToArray();
            CollectionAssert.Contains(categories, "SpecWebSenderCategory");
        }

        [Test]
        public void BuildAssetStatePayload_ContainsCreatedAsset_WithCreatedTrue()
        {
            AssetCreationService.Create(typeof(SeData), AssetType.Se, "テスト", "Player", TestIdentifier, gameDataRoot: TestRoot);

            var payload = SpecWebSender.BuildAssetStatePayload();

            var items = (Newtonsoft.Json.Linq.JArray)payload["items"];
            var item = items.SingleOrDefault(i => (string)i["id"] == "Se::" + TestIdentifier);

            Assert.IsNotNull(item, "作成したアセットが items に含まれているはず");
            Assert.AreEqual(true, (bool)item["created"]);
            Assert.IsNotNull(item["lastSyncedAt"]);
        }

        // 追補(2026-09-14): isPlaceholder / hasIcon を実値にした([32] §9 の要判断 14 への対応)。
        // isPlaceholder は既存の SeDataValidator の「Clip が未設定(または Missing)です」(Error)を
        // そのまま再利用する(CI.RunValidation() 経由)。Clips 未設定の Data はデフォルトでこの
        // Error が出るため、作っただけの Data は isPlaceholder=true になる。
        [Test]
        public void BuildAssetStatePayload_ClipsUnset_IsPlaceholderTrue_AndHasIconFalse()
        {
            AssetCreationService.Create(typeof(SeData), AssetType.Se, "テスト", "Player", TestIdentifier, gameDataRoot: TestRoot);

            var payload = SpecWebSender.BuildAssetStatePayload();
            var items = (Newtonsoft.Json.Linq.JArray)payload["items"];
            var item = items.Single(i => (string)i["id"] == "Se::" + TestIdentifier);

            Assert.AreEqual(true, (bool)item["isPlaceholder"], "Clips 未設定(Validator の Error 対象)なので Placeholder 扱いのはず");
            Assert.AreEqual(false, (bool)item["hasIcon"], "Icon 未設定なので hasIcon=false のはず");
            Assert.IsNull((string)item["iconAssetId"], "iconAssetId は Drive アップロード未実装のため常に null(要判断として docs に記載)");
        }

        [Test]
        public void BuildAssetStatePayload_ClipsSetAndIconAssigned_IsPlaceholderFalse_AndHasIconTrue()
        {
            // Validator/CI.RunValidation()・BuildExistingIndex() は同一ドメイン内では同じインスタンスを
            // 読むため、ディスクへの保存(SetDirty/SaveAssets)は不要(このテストの狙いは payload の
            // 組み立てロジックの確認であり、Icon の永続化そのものは対象外)。
            var asset = (SeData)AssetCreationService.Create(typeof(SeData), AssetType.Se, "テスト", "Player", TestIdentifier, gameDataRoot: TestRoot);
            asset.Clips = new[] { AudioClip.Create("SpecWebSenderTestClip", 100, 1, 44100, false) };
            var icon = new Texture2D(4, 4);
            asset.Icon = icon;

            try
            {
                var payload = SpecWebSender.BuildAssetStatePayload();
                var items = (Newtonsoft.Json.Linq.JArray)payload["items"];
                var item = items.Single(i => (string)i["id"] == "Se::" + TestIdentifier);

                Assert.AreEqual(false, (bool)item["isPlaceholder"], "必須参照(Clips)が入っていれば Placeholder 扱いにならないはず");
                Assert.AreEqual(true, (bool)item["hasIcon"], "Icon が割り当て済みなので hasIcon=true のはず");
            }
            finally
            {
                asset.Icon = null;
                Object.DestroyImmediate(icon);
            }
        }

        [Test]
        public void BuildTuningUsagePayload_KeyNotReferencedInCode_IsUnused()
        {
            var table = ScriptableObject.CreateInstance<TuningTable>();
            table.Entries = new[]
            {
                new TuningEntry { Key = "ZzSpecWebSenderTest/UnusedKey", Type = TuningValueType.Float, ValueFloat = 1f },
            };

            var payload = SpecWebSender.BuildTuningUsagePayload(table);

            var unusedKeys = ((Newtonsoft.Json.Linq.JArray)payload["unusedKeys"]).Select(t => (string)t).ToArray();
            CollectionAssert.Contains(unusedKeys, "ZzSpecWebSenderTest/UnusedKey");
        }

        [Test]
        public void BuildTuningUsagePayload_KeyReferencedInCode_IsNotUnused()
        {
            var table = ScriptableObject.CreateInstance<TuningTable>();
            // TuningTests.cs 等で実際に "TUNING.CombatHitStopSec" という参照は無いが、
            // このテスト自身のソース中に同じ文字列を置くことで「コード参照あり」を再現する:
            // TUNING.ZzSpecWebSenderTestReferencedKey
            table.Entries = new[]
            {
                new TuningEntry { Key = "ZzSpecWebSenderTest/ReferencedKey", Type = TuningValueType.Float, ValueFloat = 1f },
            };

            var payload = SpecWebSender.BuildTuningUsagePayload(table);

            var unusedKeys = ((Newtonsoft.Json.Linq.JArray)payload["unusedKeys"]).Select(t => (string)t).ToArray();
            CollectionAssert.DoesNotContain(unusedKeys, "ZzSpecWebSenderTest/ReferencedKey");
        }
    }
}
