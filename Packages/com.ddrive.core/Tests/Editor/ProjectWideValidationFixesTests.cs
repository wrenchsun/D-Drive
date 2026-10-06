using System;
using System.Collections.Generic;
using DDrive.Editor.Validation;
using DDrive.Foundation.Validation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Editor
{
    // [64_review_m6_2026-10-06.md] GF-R-11 — Run All の「プロジェクト全体の指摘」(Data に紐付かない)の FixAction を実行する経路。
    public class ProjectWideValidationFixesTests
    {
        [Test]
        public void FindFixable_KeepsOnlyProjectWideErrorsAndWarningsWithAFix()
        {
            Action fix = () => { };
            var reports = new List<ValidationReport>
            {
                new ValidationReport(null, ValidationResult.Error("e", fix, "A")),
                new ValidationReport(null, ValidationResult.Warning("w", fix, "B")),
                new ValidationReport(null, ValidationResult.Info("i", fix, "C")),
                new ValidationReport(null, ValidationResult.Error("no fix", null, "D")),
            };

            var found = ProjectWideValidationFixes.FindFixable(reports);

            Assert.AreEqual(2, found.Count);
            Assert.AreEqual("A", found[0].Result.Code);
            Assert.AreEqual("B", found[1].Result.Code);
        }

        [Test]
        public void Apply_RunsEveryFix_AndContinuesAfterAnException()
        {
            var ran = 0;
            var reports = new List<ValidationReport>
            {
                new ValidationReport(null, ValidationResult.Error("boom", () => throw new InvalidOperationException("x"), "A")),
                new ValidationReport(null, ValidationResult.Error("ok", () => ran++, "B")),
            };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("修正に失敗しました[(]A[)]"));
            var done = ProjectWideValidationFixes.Apply(reports);

            Assert.AreEqual(1, done);
            Assert.AreEqual(1, ran);
        }
    }
}
