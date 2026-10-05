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
        // GB-R-07(2026-10-06): 検査の対象は ValueDefValidator の結果だけ(持ち込み先の Validator・Data 型・ログに左右されない)。
        // 新規作成直後の Data に ValueDef 由来の Error が出ないことを、メッセージの部分一致ではなく「ValueDefValidator が Error を返さない」で判定する。
        // 型の列挙は D-Drive の Data 型だけ(ConcreteDataTypes は DDrive.* のアセンブリ・テスト用を除く)。
        [Test]
        public void FreshlyCreatedData_OfEveryConcreteType_HasNoValueDefError()
        {
            var errors = new List<string>();
            var checkedTypes = 0;

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
                    var data = (AssetDataBase)instance;
                    checkedTypes++;
                    foreach (var r in new ValueDefValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })))
                    {
                        if (r.Severity == ValidationSeverity.Error)
                        {
                            errors.Add($"{type.Name}: {r.Message}");
                        }
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }

            Assert.Greater(checkedTypes, 0);
            Assert.IsEmpty(errors, "新規作成直後に ValueDef の Error が出ています: " + string.Join(" / ", errors));
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
