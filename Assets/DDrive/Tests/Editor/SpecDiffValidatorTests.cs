using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Validation;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Tuning;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // 6-9([27_spec_sheet.md] §6 → [32_spec_web.md] §10 の読み替え表) — Specs/*.json のスナップショットと
    // 実データを比較する SpecDiffValidator の検証。ネットワークに出ない・実 Specs/・実 GameData・実
    // Addressables を汚さないことを徹底する(一時フォルダ + RepoRootOverride/TuningTableOverride で隔離)。
    public class SpecDiffValidatorTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempSpecDiffValidator";

        private string _tempRepoRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRepoRoot = Path.Combine(Path.GetTempPath(), "DDriveSpecDiffValidatorTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRepoRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempRepoRoot))
            {
                Directory.Delete(_tempRepoRoot, recursive: true);
            }

            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            }
        }

        private void WriteAssetsJson(string json)
        {
            var path = Path.Combine(_tempRepoRoot, "Specs", "assets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
        }

        private void WriteTuningJson(string json)
        {
            var path = Path.Combine(_tempRepoRoot, "Specs", "tuning.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
        }

        private SpecDiffValidator NewValidator(TuningTable table = null) => new()
        {
            RepoRootOverride = _tempRepoRoot,
            TuningTableOverride = table,
        };

        // 実ディスクに書かない、隔離用のダミー(グローバル検査だけを見たいテストで data 引数を満たすために使う)。
        private static SeData NewDummyAsset() => ScriptableObject.CreateInstance<SeData>();

        private static List<ValidationResult> Run(SpecDiffValidator validator, AssetDataBase data)
            => new(validator.Validate(data, new ValidationContext(new List<AssetDataBase> { data })));

        [Test]
        public void NoSnapshotFiles_ReturnsSingleInfo()
        {
            var results = Run(NewValidator(), NewDummyAsset());

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationSeverity.Info, results[0].Severity);
            StringAssert.Contains("スナップショットが見つかりません", results[0].Message);
        }

        [Test]
        public void AssetInSnapshot_WithNoMatchingData_IsInfo()
        {
            const string identifier = "SpecDiffValTestMissing";
            WriteAssetsJson($"{{\"items\":[{{\"id\":\"Se::{identifier}\",\"assetType\":\"Se\",\"identifier\":\"{identifier}\",\"displayName\":\"未作成テスト\",\"status\":\"発注済\"}}]}}");

            var results = Run(NewValidator(), NewDummyAsset());

            var hit = results.SingleOrDefault(r => r.Message.Contains(identifier));
            Assert.IsNotNull(hit.Message);
            Assert.AreEqual(ValidationSeverity.Info, hit.Severity);
            StringAssert.Contains("Data がありません", hit.Message);
        }

        [Test]
        public void ArchivedItem_IsNotTreatedAsMissing()
        {
            const string identifier = "SpecDiffValTestArchivedFlag";
            WriteAssetsJson($"{{\"items\":[{{\"id\":\"Se::{identifier}\",\"assetType\":\"Se\",\"identifier\":\"{identifier}\",\"archived\":true}}]}}");

            var results = Run(NewValidator(), NewDummyAsset());

            Assert.IsFalse(results.Any(r => r.Message.Contains(identifier)));
        }

        [Test]
        public void ImportedStatus_WithPlaceholderData_IsWarning()
        {
            const string identifier = "SpecDiffValTestImported";
            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "インポート済テスト", "Test", identifier, gameDataRoot: TestRoot);
            Assert.IsNotNull(asset);

            WriteAssetsJson($"{{\"items\":[{{\"id\":\"Se::{identifier}\",\"assetType\":\"Se\",\"identifier\":\"{identifier}\",\"status\":\"インポート済\"}}]}}");

            var results = Run(NewValidator(), asset);

            var hit = results.SingleOrDefault(r => r.Message.Contains(identifier));
            Assert.IsNotNull(hit.Message);
            Assert.AreEqual(ValidationSeverity.Warning, hit.Severity);
            StringAssert.Contains("Placeholder", hit.Message);
        }

        [Test]
        public void ImportedStatus_WithNonPlaceholderData_NoWarning()
        {
            const string identifier = "SpecDiffValTestImportedOk";
            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "インポート済OK", "Test", identifier, gameDataRoot: TestRoot);
            Assert.IsNotNull(asset);

            var seData = (SeData)asset;
            seData.Clips = new[] { AudioClip.Create("dummy", 1, 1, 44100, false) };
            EditorUtility.SetDirty(seData);
            using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }

            WriteAssetsJson($"{{\"items\":[{{\"id\":\"Se::{identifier}\",\"assetType\":\"Se\",\"identifier\":\"{identifier}\",\"status\":\"インポート済\"}}]}}");

            var results = Run(NewValidator(), asset);

            Assert.IsFalse(results.Any(r => r.Message.Contains(identifier) && r.Severity == ValidationSeverity.Warning));
        }

        [Test]
        public void AssetMissingFromSnapshot_WithSpecUrlSet_IsInfo()
        {
            const string identifier = "SpecDiffValTestRemoved";
            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "削除済みテスト", "Test", identifier, gameDataRoot: TestRoot);
            asset.SpecUrl = "https://example/spec";
            EditorUtility.SetDirty(asset);
            using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }

            WriteAssetsJson("{\"items\":[{\"id\":\"Se::SpecDiffValTestOther\",\"assetType\":\"Se\",\"identifier\":\"SpecDiffValTestOther\"}]}");

            var results = Run(NewValidator(), asset);

            var hit = results.SingleOrDefault(r => r.Message.Contains(identifier));
            Assert.IsNotNull(hit.Message);
            Assert.AreEqual(ValidationSeverity.Info, hit.Severity);
            StringAssert.Contains("見つかりません", hit.Message);
        }

        [Test]
        public void AssetMissingFromSnapshot_WithoutSpecUrl_NoInfo()
        {
            const string identifier = "SpecDiffValTestManual";
            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "手動作成テスト", "Test", identifier, gameDataRoot: TestRoot);

            WriteAssetsJson("{\"items\":[{\"id\":\"Se::SpecDiffValTestOther\",\"assetType\":\"Se\",\"identifier\":\"SpecDiffValTestOther\"}]}");

            var results = Run(NewValidator(), asset);

            Assert.IsFalse(results.Any(r => r.Message.Contains(identifier)));
        }

        [Test]
        public void CorruptAssetsJson_IsWarning()
        {
            WriteAssetsJson("{not valid json");

            var results = Run(NewValidator(), NewDummyAsset());

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("解釈できません")));
        }

        [Test]
        public void AssetsJsonMissingItemsKey_IsWarning()
        {
            WriteAssetsJson("{\"foo\":1}");

            var results = Run(NewValidator(), NewDummyAsset());

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("items")));
        }

        [Test]
        public void AssetItemMissingIdentifier_IsWarning_AndNotCountedAsMissing()
        {
            WriteAssetsJson("{\"items\":[{\"assetType\":\"Se\"}]}");

            var results = Run(NewValidator(), NewDummyAsset());

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Warning));
            Assert.IsFalse(results.Any(r => r.Severity == ValidationSeverity.Info));
        }

        [Test]
        public void CorruptTuningJson_IsWarning()
        {
            WriteTuningJson("{not valid json");

            var results = Run(NewValidator(), NewDummyAsset());

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("解釈できません")));
        }

        [Test]
        public void TuningJsonMissingScalarsAndTablesKeys_IsWarning()
        {
            WriteTuningJson("{\"foo\":1}");

            var results = Run(NewValidator(), NewDummyAsset());

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("scalars")));
        }

        [Test]
        public void TuningScalar_OutOfRange_IsError()
        {
            var table = ScriptableObject.CreateInstance<TuningTable>();
            table.Entries = new[]
            {
                new TuningEntry { Key = "Combat/HitStopSec", Type = TuningValueType.Float, ValueFloat = 5f },
            };

            WriteTuningJson("{\"scalars\":[{\"id\":\"Combat/HitStopSec\",\"kind\":\"scalar\",\"valueType\":\"float\",\"value\":5,\"min\":0,\"max\":1}],\"tables\":[]}");

            var results = Run(NewValidator(table), NewDummyAsset());

            var hit = results.SingleOrDefault(r => r.Message.Contains("Combat/HitStopSec"));
            Assert.IsNotNull(hit.Message);
            Assert.AreEqual(ValidationSeverity.Error, hit.Severity);
        }

        [Test]
        public void TuningScalar_WithinRange_NoError()
        {
            var table = ScriptableObject.CreateInstance<TuningTable>();
            table.Entries = new[]
            {
                new TuningEntry { Key = "Combat/HitStopSec", Type = TuningValueType.Float, ValueFloat = 0.5f },
            };

            WriteTuningJson("{\"scalars\":[{\"id\":\"Combat/HitStopSec\",\"kind\":\"scalar\",\"valueType\":\"float\",\"value\":0.5,\"min\":0,\"max\":1}],\"tables\":[]}");

            var results = Run(NewValidator(table), NewDummyAsset());

            Assert.IsFalse(results.Any(r => r.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void TuningScalar_MinEqualsMax_RangeCheckDisabled()
        {
            var table = ScriptableObject.CreateInstance<TuningTable>();
            table.Entries = new[]
            {
                new TuningEntry { Key = "Combat/HitStopSec", Type = TuningValueType.Float, ValueFloat = 999f },
            };

            WriteTuningJson("{\"scalars\":[{\"id\":\"Combat/HitStopSec\",\"kind\":\"scalar\",\"valueType\":\"float\",\"value\":999,\"min\":0,\"max\":0}],\"tables\":[]}");

            var results = Run(NewValidator(table), NewDummyAsset());

            Assert.IsFalse(results.Any(r => r.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void TuningTableCell_OutOfRange_IsError()
        {
            var table = ScriptableObject.CreateInstance<TuningTable>();
            table.Tables = new[]
            {
                new TuningTableEntry
                {
                    Key = "Enemy/Params",
                    Columns = new[] { new TuningTableColumn { Key = "Hp", Type = TuningValueType.Int, Min = 1, Max = 100 } },
                    Rows = new[]
                    {
                        new TuningTableRow
                        {
                            RowId = "Slime",
                            Cells = new[] { new TuningCellValue { ColumnKey = "Hp", I = 999 } },
                        },
                    },
                },
            };

            WriteTuningJson("{\"scalars\":[],\"tables\":[{\"id\":\"Enemy/Params\",\"kind\":\"table\",\"columns\":[{\"key\":\"Hp\",\"valueType\":\"int\",\"min\":1,\"max\":100}],\"rows\":[{\"rowId\":\"Slime\",\"cells\":{\"Hp\":999}}]}]}");

            var results = Run(NewValidator(table), NewDummyAsset());

            var hit = results.SingleOrDefault(r => r.Message.Contains("Enemy/Params") && r.Message.Contains("Slime"));
            Assert.IsNotNull(hit.Message);
            Assert.AreEqual(ValidationSeverity.Error, hit.Severity);
        }

        [Test]
        public void TuningTableCell_WithinRange_NoError()
        {
            var table = ScriptableObject.CreateInstance<TuningTable>();
            table.Tables = new[]
            {
                new TuningTableEntry
                {
                    Key = "Enemy/Params",
                    Columns = new[] { new TuningTableColumn { Key = "Hp", Type = TuningValueType.Int, Min = 1, Max = 100 } },
                    Rows = new[]
                    {
                        new TuningTableRow
                        {
                            RowId = "Slime",
                            Cells = new[] { new TuningCellValue { ColumnKey = "Hp", I = 10 } },
                        },
                    },
                },
            };

            WriteTuningJson("{\"scalars\":[],\"tables\":[{\"id\":\"Enemy/Params\",\"kind\":\"table\",\"columns\":[{\"key\":\"Hp\",\"valueType\":\"int\",\"min\":1,\"max\":100}],\"rows\":[{\"rowId\":\"Slime\",\"cells\":{\"Hp\":10}}]}]}");

            var results = Run(NewValidator(table), NewDummyAsset());

            Assert.IsFalse(results.Any(r => r.Severity == ValidationSeverity.Error));
        }
    }
}
