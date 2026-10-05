using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DDrive.Editor.Validation;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Cutscene;
using ExternalPackage.Fake;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ExternalContract.Tests
{
    // [docs/42 §5.14 E-23] 外部拡張の契約: 実行順の検査(契約 G-1)の除外(ICameraExecutionOrderExemptionProvider)。
    // 外部アセンブリ(ExternalContract.Tests.Editor)の public な実装が TypeCache で発見され、宣言された型だけが
    // Warning から外れ、Info が 1 件出て、理由が無ければ無効 + Warning、例外は隔離され、何も宣言しなければ従来どおり、を固定する。
    public class ExternalContractCameraExemptionTests
    {
        private Func<IEnumerable<CameraExecutionOrderValidator.ScriptOrderInfo>> _originalProvider;
        private CutsceneData _cutscene;

        [SetUp]
        public void SetUp()
        {
            _originalProvider = CameraExecutionOrderValidator.ScriptOrderProvider;
            ExternalCameraExemptionProbe.Reset();
            _cutscene = ScriptableObject.CreateInstance<CutsceneData>();
            CameraExecutionOrderValidator.ScriptOrderProvider = () => new[]
            {
                Script(typeof(ExternalCameraReader), 10000),
                Script(typeof(ExternalCameraReaderOther), 10000),
            };
        }

        [TearDown]
        public void TearDown()
        {
            CameraExecutionOrderValidator.ScriptOrderProvider = _originalProvider;
            ExternalCameraExemptionProbe.Reset();
            if (_cutscene != null)
            {
                UnityEngine.Object.DestroyImmediate(_cutscene);
            }
        }

        private static CameraExecutionOrderValidator.ScriptOrderInfo Script(Type type, int order)
            => new($"Packages/com.external.fake/{type.Name}.cs", type.Name, order, type.FullName);

        private List<ValidationResult> Run()
        {
            var validator = new CameraExecutionOrderValidator();
            var ctx = new ValidationContext(new List<AssetDataBase> { _cutscene });
            return new List<ValidationResult>(validator.Validate(_cutscene, ctx));
        }

        private static int Warnings(List<ValidationResult> results, string typeName)
            => results.Count(r => r.Severity == ValidationSeverity.Warning && string.IsNullOrEmpty(r.Code) && r.Message.Contains("(" + typeName + ")"));

        [Test]
        public void E23_NothingDeclared_BothTypesWarn_AsBefore()
        {
            var results = Run();

            Assert.AreEqual(1, Warnings(results, nameof(ExternalCameraReader)));
            Assert.AreEqual(1, Warnings(results, nameof(ExternalCameraReaderOther)));
            Assert.IsFalse(results.Any(r => r.Code == CameraExecutionOrderExemptions.CodeExempted || r.Code == CameraExecutionOrderExemptions.CodeInvalid));
        }

        [Test]
        public void E23_ProviderInExternalAssembly_IsDiscovered_AndExemptsOnlyTheDeclaredType()
        {
            ExternalCameraExemptionProbe.Enabled = true;

            var results = Run();

            Assert.AreEqual(0, Warnings(results, nameof(ExternalCameraReader)), "宣言された型は Warning を出さない");
            Assert.AreEqual(1, Warnings(results, nameof(ExternalCameraReaderOther)), "宣言されていない型は従来どおり検査する");
            var info = results.Single(r => r.Code == CameraExecutionOrderExemptions.CodeExempted);
            Assert.AreEqual(ValidationSeverity.Info, info.Severity);
            StringAssert.Contains(typeof(ExternalCameraReader).FullName, info.Message);
            StringAssert.Contains("カメラを読むだけ", info.Message);
            StringAssert.Contains(typeof(ExternalCameraReadOnlyProvider).FullName, info.Message, "宣言元が分かる");
        }

        [Test]
        public void E23_EmptyReason_IsInvalid_WarnsAndDoesNotExempt()
        {
            ExternalCameraExemptionProbe.Enabled = true;
            ExternalCameraExemptionProbe.EmptyReason = true;

            var results = Run();

            Assert.AreEqual(1, Warnings(results, nameof(ExternalCameraReader)), "理由が無い宣言は無効(抑止しない)");
            Assert.AreEqual(1, results.Count(r => r.Code == CameraExecutionOrderExemptions.CodeInvalid && r.Severity == ValidationSeverity.Warning));
            Assert.AreEqual(0, results.Count(r => r.Code == CameraExecutionOrderExemptions.CodeExempted));
        }

        [Test]
        public void E23_ThrowingProvider_IsIsolated_OtherProvidersStillApply()
        {
            ExternalCameraExemptionProbe.Enabled = true;
            ExternalCameraExemptionProbe.Throws = true;
            LogAssert.Expect(LogType.Exception, new Regex("external camera exemption provider failure"));

            var results = Run();

            Assert.AreEqual(0, Warnings(results, nameof(ExternalCameraReader)));
        }

        [Test]
        public void E23_NestedProvider_IsDiscovered_OnlyWhenTheOuterTypeIsVisible()
        {
            Assert.IsFalse(typeof(ExternalHiddenCameraExemptionHost.ExternalHiddenNestedCameraExemptionProvider).IsVisible);
            Assert.IsTrue(typeof(ExternalVisibleCameraExemptionHost.ExternalVisibleNestedCameraExemptionProvider).IsVisible);

            Run();

            Assert.AreEqual(0, ExternalCameraExemptionProbe.HiddenNestedProviderCalls, "外側が internal の入れ子は発見されない");
            Assert.Greater(ExternalCameraExemptionProbe.VisibleNestedProviderCalls, 0, "外側も public の入れ子は発見される");
        }
    }
}
