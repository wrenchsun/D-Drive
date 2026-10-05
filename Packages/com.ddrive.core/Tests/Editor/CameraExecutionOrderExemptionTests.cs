using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DDrive.Editor.Settings;
using DDrive.Editor.Validation;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Cutscene;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Editor
{
    // [26_timeline.md] §4.6.5 契約 G-1 / [42_distribution.md] §5.14 E-23(2026-10-06、P-15 確認 Q-4) —
    // 実行順の検査から「カメラを読むだけ」の型を外す仕組み(ICameraExecutionOrderExemptionProvider + プロジェクト設定)。
    // 解決のロジック(CameraExecutionOrderExemptions.Resolve)と、CameraExecutionOrderValidator への組み込み(プロジェクト設定経由)を固定する。
    // 外部アセンブリが実装する提供口の自動発見は ExternalContract.Tests.Editor(ExternalContractCameraExemptionTests)で固定する。
    public class CameraExecutionOrderExemptionTests
    {
        private sealed class FakeProvider : ICameraExecutionOrderExemptionProvider
        {
            public IEnumerable<CameraExecutionOrderExemption> Items = Array.Empty<CameraExecutionOrderExemption>();
            public bool Throws;

            public IEnumerable<CameraExecutionOrderExemption> GetExemptions()
            {
                if (Throws)
                {
                    throw new InvalidOperationException("boom (test)");
                }

                return Items;
            }
        }

        private sealed class NestedReader
        {
        }

        private static readonly Func<string, bool> AllExist = _ => true;
        private static readonly Func<string, bool> NoneExist = _ => false;

        private static CameraExecutionOrderExemptionEntry Entry(string typeName, string reason)
            => new() { TypeName = typeName, Reason = reason };

        // ── Resolve(純粋ロジック) ──

        [Test]
        public void Resolve_NoSources_IsEmpty_AndProducesNoResults()
        {
            var resolution = CameraExecutionOrderExemptions.Resolve(null, null, AllExist);

            Assert.IsEmpty(resolution.Valid);
            Assert.IsEmpty(resolution.Problems);
            Assert.IsEmpty(resolution.ToResults().ToList(), "除外が無ければ従来と同じ(何も出さない)");
            Assert.IsFalse(resolution.IsExempt("Any.Type", "Type"));
        }

        [Test]
        public void Resolve_Provider_AcceptsTypeAndTypeName_RequiresReason()
        {
            var provider = new FakeProvider
            {
                Items = new[]
                {
                    new CameraExecutionOrderExemption(typeof(CameraExecutionOrderExemptionTests), "カメラを読むだけ"),
                    new CameraExecutionOrderExemption("My.Ns.Reader", "位置を読むだけ"),
                    new CameraExecutionOrderExemption("My.Ns.NoReason", "  "),
                    new CameraExecutionOrderExemption((string)null, "型なし"),
                },
            };

            var resolution = CameraExecutionOrderExemptions.Resolve(new[] { provider }, null, AllExist);

            Assert.AreEqual(2, resolution.Valid.Count);
            Assert.AreEqual(typeof(CameraExecutionOrderExemptionTests).FullName, resolution.Valid[0].FullName);
            Assert.AreEqual("My.Ns.Reader", resolution.Valid[1].FullName);
            Assert.AreEqual(2, resolution.Problems.Count, "理由なし・型なしは無効(除外せず Problems)");
            StringAssert.Contains("理由", resolution.Problems.First(p => p.Contains("NoReason")));
            Assert.IsTrue(resolution.IsExempt("My.Ns.Reader", "Reader"));
            Assert.IsFalse(resolution.IsExempt("My.Ns.NoReason", "NoReason"), "無効な宣言は除外しない");
        }

        [Test]
        public void Resolve_Settings_ReasonRequired_AndUnknownTypeIsInvalid()
        {
            var entries = new[]
            {
                Entry("My.Ns.Reader", "カメラを読むだけ"),
                Entry("My.Ns.NoReason", ""),
                Entry("My.Ns.Missing", "存在しない型"),
                Entry("", "型が空"),
                null,
            };

            var resolution = CameraExecutionOrderExemptions.Resolve(null, entries, name => name == "My.Ns.Reader" || name == "My.Ns.NoReason");

            Assert.AreEqual(1, resolution.Valid.Count);
            Assert.AreEqual("My.Ns.Reader", resolution.Valid[0].FullName);
            Assert.AreEqual(4, resolution.Problems.Count);
            Assert.IsTrue(resolution.Problems.Any(p => p.Contains("My.Ns.NoReason") && p.Contains("理由")));
            Assert.IsTrue(resolution.Problems.Any(p => p.Contains("My.Ns.Missing") && p.Contains("見つかりません")));
            Assert.IsTrue(resolution.ToResults().Where(r => r.Severity == ValidationSeverity.Warning).All(r => r.Code == CameraExecutionOrderExemptions.CodeInvalid));
        }

        [Test]
        public void Resolve_Duplicate_FirstWins_ProviderBeforeSettings()
        {
            var provider = new FakeProvider { Items = new[] { new CameraExecutionOrderExemption("My.Ns.Reader", "提供口の理由") } };

            var resolution = CameraExecutionOrderExemptions.Resolve(new[] { provider }, new[] { Entry("My.Ns.Reader", "設定の理由") }, AllExist);

            Assert.AreEqual(1, resolution.Valid.Count);
            Assert.AreEqual("提供口の理由", resolution.Valid[0].Reason);
        }

        [Test]
        public void Resolve_ThrowingProvider_IsIsolated_OthersStillWork()
        {
            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            var bad = new FakeProvider { Throws = true };
            var good = new FakeProvider { Items = new[] { new CameraExecutionOrderExemption("My.Ns.Reader", "ok") } };

            var resolution = CameraExecutionOrderExemptions.Resolve(new ICameraExecutionOrderExemptionProvider[] { bad, good }, null, AllExist);

            Assert.AreEqual(1, resolution.Valid.Count);
            Assert.AreEqual(1, resolution.Problems.Count, "提供口の例外は Console だけでなく Warning としても結果に出る(GA-R-10)");
            StringAssert.Contains("boom", resolution.Problems[0]);
            Assert.IsTrue(resolution.ToResults().Any(r => r.Severity == DDrive.Foundation.Validation.ValidationSeverity.Warning));
        }

        [Test]
        public void IsExempt_NormalizesNestedTypeSeparator_AndFallsBackToShortName()
        {
            var resolution = CameraExecutionOrderExemptions.Resolve(
                null, new[] { Entry("My.Ns.Outer+Inner", "r"), Entry("My.Ns.Reader", "r") }, AllExist);

            Assert.IsTrue(resolution.IsExempt("My.Ns.Outer+Inner", "Inner"), "Type.FullName の入れ子の区切り(+)を同一視する");
            Assert.IsTrue(resolution.IsExempt("My.Ns.Outer.Inner", "Inner"));
            Assert.IsFalse(resolution.IsExempt("Other.Ns.Reader", "Reader"), "完全修飾名が違えば別の型");
            Assert.IsTrue(resolution.IsExempt(null, "Reader"), "完全修飾名が無い(テストの注入)ときは短い名前で照合");
            Assert.IsFalse(resolution.IsExempt(null, "Other"));
        }

        [Test]
        public void ToResults_OneInfoWithTypeNamesAndReasons_PlusWarningPerInvalid()
        {
            var resolution = CameraExecutionOrderExemptions.Resolve(
                null, new[] { Entry("My.Ns.A", "理由A"), Entry("My.Ns.B", "理由B"), Entry("My.Ns.C", "") }, AllExist);

            var results = resolution.ToResults().ToList();

            Assert.AreEqual(1, results.Count(r => r.Severity == ValidationSeverity.Info), "Info は 1 件(出しすぎない)");
            var info = results.Single(r => r.Severity == ValidationSeverity.Info);
            Assert.AreEqual(CameraExecutionOrderExemptions.CodeExempted, info.Code);
            StringAssert.Contains("実行順の検査から除外: 2 型", info.Message);
            StringAssert.Contains("My.Ns.A", info.Message);
            StringAssert.Contains("理由A", info.Message);
            StringAssert.Contains("理由B", info.Message);
            Assert.AreEqual(1, results.Count(r => r.Severity == ValidationSeverity.Warning));
        }

        // ── CameraExecutionOrderValidator への組み込み(プロジェクト設定経由) ──

        private Func<IEnumerable<CameraExecutionOrderValidator.ScriptOrderInfo>> _originalProvider;
        private FieldInfo _field;
        private object _originalList;
        private CutsceneData _cutscene;

        [SetUp]
        public void SetUp()
        {
            _originalProvider = CameraExecutionOrderValidator.ScriptOrderProvider;
            _field = typeof(DDriveProjectSettings).GetField("_cameraExecutionOrderExemptions", BindingFlags.NonPublic | BindingFlags.Instance);
            _originalList = _field.GetValue(DDriveProjectSettings.instance);
            _cutscene = ScriptableObject.CreateInstance<CutsceneData>();
        }

        [TearDown]
        public void TearDown()
        {
            CameraExecutionOrderValidator.ScriptOrderProvider = _originalProvider;
            _field.SetValue(DDriveProjectSettings.instance, _originalList); // Save は呼ばない(ProjectSettings を汚さない)
            if (_cutscene != null)
            {
                UnityEngine.Object.DestroyImmediate(_cutscene);
            }
        }

        private List<ValidationResult> RunValidator()
        {
            var validator = new CameraExecutionOrderValidator();
            var ctx = new ValidationContext(new List<AssetDataBase> { _cutscene });
            return new List<ValidationResult>(validator.Validate(_cutscene, ctx));
        }

        private static void SetScripts(params CameraExecutionOrderValidator.ScriptOrderInfo[] scripts)
            => CameraExecutionOrderValidator.ScriptOrderProvider = () => scripts;

        private static CameraExecutionOrderValidator.ScriptOrderInfo Script(string typeName, string fullName, int order)
            => new($"Assets/Game/{typeName}.cs", typeName, order, fullName);

        private static int Warnings(List<ValidationResult> results, string containing)
            => results.Count(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains(containing));

        [Test]
        public void Validator_NoExemptions_BehavesAsBefore()
        {
            _field.SetValue(DDriveProjectSettings.instance, new List<CameraExecutionOrderExemptionEntry>());
            SetScripts(Script("Reader", "My.Ns.Reader", 10000), Script("Writer", "My.Ns.Writer", 5000));

            var results = RunValidator();

            Assert.AreEqual(1, Warnings(results, "Reader"));
            Assert.AreEqual(1, Warnings(results, "Writer"));
            Assert.AreEqual(0, results.Count(r => r.Code == CameraExecutionOrderExemptions.CodeExempted || r.Code == CameraExecutionOrderExemptions.CodeInvalid));
        }

        [Test]
        public void Validator_SettingsExemption_SuppressesOnlyThatType_AndAddsOneInfo()
        {
            var realType = typeof(CameraExecutionOrderExemptionTests).FullName;
            _field.SetValue(DDriveProjectSettings.instance, new List<CameraExecutionOrderExemptionEntry> { Entry(realType, "カメラを読むだけ") });
            SetScripts(
                Script("CameraExecutionOrderExemptionTests", realType, 10000),
                Script("Writer", "My.Ns.Writer", 5000));

            var results = RunValidator();

            Assert.AreEqual(0, Warnings(results, "CameraExecutionOrderExemptionTests"), "除外された型は Warning を出さない");
            Assert.AreEqual(1, Warnings(results, "Writer"), "除外されていない他の型は従来どおり検査する");
            var infos = results.Where(r => r.Code == CameraExecutionOrderExemptions.CodeExempted).ToList();
            Assert.AreEqual(1, infos.Count);
            StringAssert.Contains(realType, infos[0].Message);
            StringAssert.Contains("カメラを読むだけ", infos[0].Message);
        }

        [Test]
        public void Validator_InvalidSettingsExemption_WarnsAndDoesNotExempt()
        {
            _field.SetValue(DDriveProjectSettings.instance, new List<CameraExecutionOrderExemptionEntry>
            {
                Entry("My.Ns.Reader", ""), // 理由なし
                Entry("No.Such.Type.Anywhere", "実在しない"), // 存在しない型名
            });
            SetScripts(Script("Reader", "My.Ns.Reader", 10000));

            var results = RunValidator();

            Assert.AreEqual(
                1,
                results.Count(r => r.Severity == ValidationSeverity.Warning && string.IsNullOrEmpty(r.Code) && r.Message.Contains("Reader")),
                "無効な除外では抑止されない(従来の Warning が残る)");
            Assert.AreEqual(2, results.Count(r => r.Code == CameraExecutionOrderExemptions.CodeInvalid && r.Severity == ValidationSeverity.Warning));
            Assert.AreEqual(0, results.Count(r => r.Code == CameraExecutionOrderExemptions.CodeExempted));
        }

        [Test]
        public void Validator_ApplierErrorIsNeverExempted()
        {
            var applier = typeof(DDriveCutsceneCameraApplier).FullName;
            _field.SetValue(DDriveProjectSettings.instance, new List<CameraExecutionOrderExemptionEntry> { Entry(applier, "誤って除外") });
            SetScripts(new CameraExecutionOrderValidator.ScriptOrderInfo(
                "Assets/DDrive/Applier.cs", nameof(DDriveCutsceneCameraApplier), 5, applier));

            var results = RunValidator();

            Assert.AreEqual(1, results.Count(r => r.Severity == ValidationSeverity.Error), "Applier の実行順の改変(b)は除外できない");
        }
    }
}
