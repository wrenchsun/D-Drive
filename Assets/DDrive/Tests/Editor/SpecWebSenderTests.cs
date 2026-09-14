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
