using System.Collections.Generic;
using System.Linq;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Haptics;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [31_phase5_decisions.md] A2(2026-09-15、P6) — SliderSkinData の NotchHapticId/LimitHapticId が
    // 非 0 のとき、対応する HapticsData がプロジェクト内に無ければ Warning になることの検査。
    // ScriptableObject.CreateInstance のみ使用し、実 GameData・カタログ・Addressables には触らない。
    public class SliderSkinDataHapticValidatorTests
    {
        private readonly List<Object> _cleanup = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _cleanup)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _cleanup.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _cleanup.Add(o);
            return o;
        }

        private SliderSkinData CreateSkin(ulong notchHapticId = 0, ulong limitHapticId = 0)
        {
            var skin = Track(ScriptableObject.CreateInstance<SliderSkinData>());
            skin.NotchHapticId = notchHapticId;
            skin.LimitHapticId = limitHapticId;
            return skin;
        }

        private HapticsData CreateHaptic(ulong id)
        {
            var haptic = Track(ScriptableObject.CreateInstance<HapticsData>());
            haptic.Id = id;
            return haptic;
        }

        private static List<ValidationResult> Run(SliderSkinData skin, params AssetDataBase[] others)
        {
            var all = new List<AssetDataBase> { skin };
            all.AddRange(others);
            return new SliderSkinDataValidator().Validate(skin, new ValidationContext(all)).ToList();
        }

        private static bool Has(List<ValidationResult> results, ValidationSeverity severity, string fragment)
            => results.Any(r => r.Severity == severity && r.Message.Contains(fragment));

        [Test]
        public void NotchHapticId_Zero_IsIgnored()
        {
            var skin = CreateSkin(notchHapticId: 0);
            var results = Run(skin);
            Assert.IsFalse(Has(results, ValidationSeverity.Warning, "NotchHapticId"));
        }

        [Test]
        public void LimitHapticId_Zero_IsIgnored()
        {
            var skin = CreateSkin(limitHapticId: 0);
            var results = Run(skin);
            Assert.IsFalse(Has(results, ValidationSeverity.Warning, "LimitHapticId"));
        }

        [Test]
        public void NotchHapticId_NonZero_NoMatchingAsset_ReportsWarning()
        {
            var skin = CreateSkin(notchHapticId: 12345);
            var results = Run(skin);
            Assert.IsTrue(Has(results, ValidationSeverity.Warning, "NotchHapticId"));
        }

        [Test]
        public void LimitHapticId_NonZero_NoMatchingAsset_ReportsWarning()
        {
            var skin = CreateSkin(limitHapticId: 67890);
            var results = Run(skin);
            Assert.IsTrue(Has(results, ValidationSeverity.Warning, "LimitHapticId"));
        }

        [Test]
        public void NotchHapticId_MatchingAssetExists_NoWarning()
        {
            var skin = CreateSkin(notchHapticId: 111);
            var haptic = CreateHaptic(111);
            var results = Run(skin, haptic);
            Assert.IsFalse(Has(results, ValidationSeverity.Warning, "NotchHapticId"));
        }

        [Test]
        public void LimitHapticId_MatchingAssetExists_NoWarning()
        {
            var skin = CreateSkin(limitHapticId: 222);
            var haptic = CreateHaptic(222);
            var results = Run(skin, haptic);
            Assert.IsFalse(Has(results, ValidationSeverity.Warning, "LimitHapticId"));
        }

        [Test]
        public void BothHapticIds_NonZero_NoMatchingAssets_ReportsTwoWarnings()
        {
            var skin = CreateSkin(notchHapticId: 333, limitHapticId: 444);
            var results = Run(skin);
            Assert.IsTrue(Has(results, ValidationSeverity.Warning, "NotchHapticId"));
            Assert.IsTrue(Has(results, ValidationSeverity.Warning, "LimitHapticId"));
        }
    }
}
