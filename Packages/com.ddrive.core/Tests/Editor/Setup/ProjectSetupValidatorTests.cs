using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.Validation;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor.Setup
{
    // [42_distribution.md] §3.6/§6 P-6(2026-09-20) — ProjectSetupValidator(IUniversalValidator)の
    // 回帰テスト。このリポジトリ(開発リポジトリ)は既にセットアップ済みのため、
    // 現状のまま実行すると Warning 0 件になることを固定する(= 検査ロジック自体が誤検出していないこと
    // の確認)。IUniversalValidator は ValidatorRegistry.RunAll が「1 件以上の AssetDataBase がある
    // ときにだけ」呼ぶ既存の制約があるため、メモリ上だけの(保存しない)ダミー Data を 1 件用意する。
    public class ProjectSetupValidatorTests
    {
        private SeData _dummy;

        [SetUp]
        public void SetUp()
        {
            _dummy = ScriptableObject.CreateInstance<SeData>(); // 保存しない(メモリ上のみ)。
        }

        [TearDown]
        public void TearDown()
        {
            if (_dummy != null)
            {
                Object.DestroyImmediate(_dummy);
            }
        }

        [Test]
        public void Validate_DevRepoFullyConfigured_ReturnsNoWarnings()
        {
            var ctx = new ValidationContext(new List<AssetDataBase> { _dummy });
            var validator = new ProjectSetupValidator();

            var results = validator.Validate(_dummy, ctx).ToList();

            Assert.IsEmpty(results, "開発リポジトリは既にセットアップ済みのため Warning が出ないこと: " +
                string.Join(", ", results.Select(r => $"{r.Code}:{r.Message}")));
        }

        [Test]
        public void Validate_SecondCallSameContext_DoesNotDuplicateReport()
        {
            var ctx = new ValidationContext(new List<AssetDataBase> { _dummy });
            var validator = new ProjectSetupValidator();

            var first = validator.Validate(_dummy, ctx).ToList();
            var second = validator.Validate(_dummy, ctx).ToList();

            Assert.IsEmpty(first);
            Assert.IsEmpty(second, "同じ ValidationContext で 2 回呼んでも重複報告しないこと");
        }

        [Test]
        public void Target_IsNone()
        {
            Assert.AreEqual(DDrive.Foundation.Identity.AssetType.None, new ProjectSetupValidator().Target);
        }
    }
}
