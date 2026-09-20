using System;
using System.Collections.Generic;
using DDrive.Editor.Update;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Update
{
    // [42_distribution.md] §4.2/§6 P-14(2026-09-20) — 現在 vs 最新の判定(`UpdateCheckLogic.Evaluate`)を
    // 固定する。実 git・実 manifest には一切触れない。
    public class UpdateCheckLogicTests
    {
        private static readonly List<Version> OneZeroZeroOnly = new() { new Version(1, 0, 0) };

        [Test]
        public void Evaluate_LatestEqualsCurrentTag_ReturnsUpToDate()
        {
            var result = UpdateCheckLogic.Evaluate("v1.0.0", "1.0.0", OneZeroZeroOnly);

            Assert.AreEqual(UpdateCheckLogic.BumpKind.UpToDate, result.Bump);
            Assert.AreEqual(new Version(1, 0, 0), result.CurrentVersion);
            Assert.IsFalse(result.CurrentIsFromPackageJson);
        }

        [Test]
        public void Evaluate_LatestOlderThanCurrentTag_ReturnsUpToDate()
        {
            var tags = new List<Version> { new(0, 9, 0) };

            var result = UpdateCheckLogic.Evaluate("v1.0.0", "1.0.0", tags);

            Assert.AreEqual(UpdateCheckLogic.BumpKind.UpToDate, result.Bump);
        }

        [Test]
        public void Evaluate_MinorBump_DetectsMinor()
        {
            var tags = new List<Version> { new(1, 1, 0), new(1, 0, 0) };

            var result = UpdateCheckLogic.Evaluate("v1.0.0", "1.0.0", tags);

            Assert.AreEqual(UpdateCheckLogic.BumpKind.Minor, result.Bump);
            Assert.AreEqual(new Version(1, 1, 0), result.LatestVersion);
        }

        [Test]
        public void Evaluate_PatchBump_DetectsPatch()
        {
            var tags = new List<Version> { new(1, 0, 1) };

            var result = UpdateCheckLogic.Evaluate("v1.0.0", "1.0.0", tags);

            Assert.AreEqual(UpdateCheckLogic.BumpKind.Patch, result.Bump);
        }

        [Test]
        public void Evaluate_MajorBump_DetectsMajor()
        {
            var tags = new List<Version> { new(2, 0, 0), new(1, 5, 0) };

            var result = UpdateCheckLogic.Evaluate("v1.0.0", "1.0.0", tags);

            Assert.AreEqual(UpdateCheckLogic.BumpKind.Major, result.Bump);
            Assert.AreEqual(new Version(2, 0, 0), result.LatestVersion);
        }

        [Test]
        public void Evaluate_CurrentRefIsCommitHash_FallsBackToPackageJsonVersion()
        {
            var tags = new List<Version> { new(1, 1, 0) };

            var result = UpdateCheckLogic.Evaluate("6c65a8912345678901234567890123456789abcd", "1.0.0", tags);

            Assert.IsTrue(result.CurrentIsFromPackageJson);
            Assert.AreEqual(new Version(1, 0, 0), result.CurrentVersion);
            Assert.AreEqual(UpdateCheckLogic.BumpKind.Minor, result.Bump);
        }

        [Test]
        public void Evaluate_NoTagsAvailable_ReturnsUnknown()
        {
            var result = UpdateCheckLogic.Evaluate("v1.0.0", "1.0.0", new List<Version>());

            Assert.AreEqual(UpdateCheckLogic.BumpKind.Unknown, result.Bump);
            Assert.IsNull(result.LatestVersion);
        }

        [Test]
        public void Evaluate_CurrentUnparsable_AndNoPackageJsonFallback_ReturnsUnknown()
        {
            var result = UpdateCheckLogic.Evaluate("not-a-version-or-hash", "also-not-a-version", OneZeroZeroOnly);

            Assert.AreEqual(UpdateCheckLogic.BumpKind.Unknown, result.Bump);
            Assert.IsNull(result.CurrentVersion);
        }

        [Test]
        public void Evaluate_NullRef_FallsBackToPackageJsonVersion()
        {
            var result = UpdateCheckLogic.Evaluate(null, "1.0.0", OneZeroZeroOnly);

            Assert.IsTrue(result.CurrentIsFromPackageJson);
            Assert.AreEqual(new Version(1, 0, 0), result.CurrentVersion);
            Assert.AreEqual(UpdateCheckLogic.BumpKind.UpToDate, result.Bump);
        }
    }
}
