using System;
using System.Collections.Generic;
using DDrive.Editor.Validation;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Cutscene;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [26_timeline.md] §4.6.5 検出1 / [11_tasks.md] 6-10d — CameraExecutionOrderValidator。
    //
    // ProjectSettings の Script Execution Order は実プロジェクトの設定であり、テストが書き換えて残すと
    // 実害が大きい(CLAUDE.md §0-9)。そのため CameraExecutionOrderValidator.ScriptOrderProvider(static
    // なテスト用フック)を差し替えることで、実際の MonoImporter/ProjectSettings に一切触れずに
    // (a)(b) の判定ロジックだけを検証する。各テストは TearDown で必ず元のプロバイダに戻す。
    public class CameraExecutionOrderValidatorTests
    {
        private Func<IEnumerable<CameraExecutionOrderValidator.ScriptOrderInfo>> _originalProvider;
        private readonly List<CutsceneData> _tempAssets = new();

        [SetUp]
        public void SetUp()
        {
            _originalProvider = CameraExecutionOrderValidator.ScriptOrderProvider;
        }

        [TearDown]
        public void TearDown()
        {
            CameraExecutionOrderValidator.ScriptOrderProvider = _originalProvider;

            foreach (var asset in _tempAssets)
            {
                if (asset != null)
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            }

            _tempAssets.Clear();
        }

        private CutsceneData NewCutsceneData()
        {
            var data = ScriptableObject.CreateInstance<CutsceneData>();
            _tempAssets.Add(data);
            return data;
        }

        private static List<ValidationResult> Run(IReadOnlyList<AssetDataBase> assets)
        {
            var validator = new CameraExecutionOrderValidator();
            var ctx = new ValidationContext(new List<AssetDataBase>(assets));
            return new List<ValidationResult>(validator.Validate(assets.Count > 0 ? assets[0] : null, ctx));
        }

        private static int CountWithSeverity(List<ValidationResult> results, ValidationSeverity severity)
        {
            var n = 0;
            foreach (var r in results)
            {
                if (r.Severity == severity)
                {
                    n++;
                }
            }

            return n;
        }

        [Test]
        public void NoCutsceneData_SkipsEntirely()
        {
            CameraExecutionOrderValidator.ScriptOrderProvider = () => new[]
            {
                new CameraExecutionOrderValidator.ScriptOrderInfo("Assets/SomeGame/PlayerCameraController.cs", "PlayerCameraController", 1000),
            };

            // CutsceneData が 1 件も無い(空の資産一覧)。
            var results = Run(Array.Empty<AssetDataBase>());

            Assert.IsEmpty(results, "CutsceneData が 0 件のときは検査を省略する");
        }

        [Test]
        public void NonDDriveScriptAtThreshold_IsWarning_ApplierAtCorrectOrder_NoError()
        {
            CameraExecutionOrderValidator.ScriptOrderProvider = () => new[]
            {
                new CameraExecutionOrderValidator.ScriptOrderInfo(
                    "Assets/DDrive/Runtime/Cutscene/DDriveCutsceneCameraApplier.cs",
                    nameof(DDriveCutsceneCameraApplier),
                    DDriveCutsceneCameraApplier.ExecutionOrder),
                new CameraExecutionOrderValidator.ScriptOrderInfo(
                    "Assets/SomeGame/PlayerCameraController.cs",
                    "PlayerCameraController",
                    1000),
            };

            var results = Run(new AssetDataBase[] { NewCutsceneData() });

            Assert.AreEqual(0, CountWithSeverity(results, ValidationSeverity.Error), "Applier は既定の実行順のまま");
            Assert.AreEqual(1, CountWithSeverity(results, ValidationSeverity.Warning));

            var found = false;
            foreach (var r in results)
            {
                if (r.Severity == ValidationSeverity.Warning && r.Message.Contains("PlayerCameraController"))
                {
                    found = true;
                }
            }

            Assert.IsTrue(found, "非 D-Drive スクリプトの実行順 1000 以上が Warning になっていない");
        }

        [Test]
        public void ApplierAtWrongOrder_IsError()
        {
            CameraExecutionOrderValidator.ScriptOrderProvider = () => new[]
            {
                new CameraExecutionOrderValidator.ScriptOrderInfo(
                    "Assets/DDrive/Runtime/Cutscene/DDriveCutsceneCameraApplier.cs",
                    nameof(DDriveCutsceneCameraApplier),
                    999),
                new CameraExecutionOrderValidator.ScriptOrderInfo(
                    "Assets/SomeGame/Other.cs",
                    "Other",
                    0),
            };

            var results = Run(new AssetDataBase[] { NewCutsceneData() });

            Assert.AreEqual(1, CountWithSeverity(results, ValidationSeverity.Error));

            var found = false;
            foreach (var r in results)
            {
                if (r.Severity == ValidationSeverity.Error && r.Message.Contains("DDriveCutsceneCameraApplier"))
                {
                    found = true;
                }
            }

            Assert.IsTrue(found, "Applier の実行順が 1000 でないときに Error になっていない");
        }

        [Test]
        public void DDriveScriptAtThreshold_IsNotWarned()
        {
            CameraExecutionOrderValidator.ScriptOrderProvider = () => new[]
            {
                new CameraExecutionOrderValidator.ScriptOrderInfo(
                    "Assets/DDrive/Runtime/Cutscene/DDriveCutsceneCameraApplier.cs",
                    nameof(DDriveCutsceneCameraApplier),
                    DDriveCutsceneCameraApplier.ExecutionOrder),
                new CameraExecutionOrderValidator.ScriptOrderInfo(
                    "Assets/DDrive/Runtime/Foo/SomeOtherDDriveScript.cs",
                    "SomeOtherDDriveScript",
                    1500),
            };

            var results = Run(new AssetDataBase[] { NewCutsceneData() });

            Assert.AreEqual(0, CountWithSeverity(results, ValidationSeverity.Error));
            foreach (var r in results)
            {
                Assert.IsFalse(r.Severity == ValidationSeverity.Warning && r.Message.Contains("SomeOtherDDriveScript"),
                    "D-Drive 自身のスクリプトは (a) の対象外のはず");
            }
        }
    }
}
