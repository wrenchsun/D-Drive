using System;
using System.Collections.Generic;
using System.IO;
using DDrive.Editor.Compat;
using DDrive.Tests.Editor.Compat;
using DDrive.Editor.Validation;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // 2026-10-06 — Mode=Constant の ValueDef は Time を評価に使わないため、ValueDefValidator は Time の欄を検査しない。
    // ツールで新規作成した Anim2D / ControlSkin / CameraShake などが「Duration が 0 以下」「SpeedScale が 0 以下」の
    // Error にならないことを、全 Data 型の新規作成直後と v1.0.0 フィクスチャで固定する。
    public class ConstantTimeValidationTests
    {
        private static bool IsTimeError(ValidationResult r)
            => r.Severity == ValidationSeverity.Error &&
               (r.Message.Contains("TimeMode=Duration") || r.Message.Contains("SpeedScale") || r.Message.Contains("Duration=0"));

        [Test]
        public void FreshlyCreatedData_OfEveryConcreteType_HasNoValueDefTimeError()
        {
            var otherErrors = new List<string>();
            var timeErrors = new List<string>();

            foreach (var type in SerializedLayoutSnapshotBuilder.ConcreteDataTypes())
            {
                ScriptableObject instance;
                try
                {
                    instance = ScriptableObject.CreateInstance(type);
                }
                catch (Exception)
                {
                    continue;
                }

                try
                {
                    foreach (var r in DataValidationRunner.Run((AssetDataBase)instance))
                    {
                        if (r.Severity != ValidationSeverity.Error)
                        {
                            continue;
                        }

                        if (IsTimeError(r))
                        {
                            timeErrors.Add($"{type.Name}: {r.Message}");
                        }
                        else
                        {
                            otherErrors.Add($"{type.Name}: {r.Message}");
                        }
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }

            // 「未設定」(Clip / Prefab 未設定など)を理由にした Error は新規作成直後に出てよい。参考として出力する。
            TestContext.Out.WriteLine("新規作成直後の Data に出る他の Error(参考):\n" + string.Join("\n", otherErrors));
            Assert.IsEmpty(timeErrors, "ValueDef の Time 由来の Error が出ています:\n" + string.Join("\n", timeErrors));
        }

        [Test]
        public void V1_0_0_Fixtures_HaveNoValueDefTimeError()
        {
            var errors = new List<string>();
            var count = 0;
            foreach (var path in Directory.GetFiles(LegacyAssetFixtureTests.FixturesRoot, "*.asset"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(path.Replace('\\', '/'));
                Assert.IsNotNull(asset, path);
                count++;
                foreach (var r in new ValueDefValidator().Validate(asset, new ValidationContext(new List<AssetDataBase> { asset })))
                {
                    if (r.Severity == ValidationSeverity.Error)
                    {
                        errors.Add($"{asset.name}: {r.Message}");
                    }
                }
            }

            Assert.Greater(count, 0);
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }
    }
}
