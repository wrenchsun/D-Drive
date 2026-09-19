using DDrive.Foundation.Data;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [08_presentation.md] 実装メモ(2026-09-19、トラック/アセット両方の Anchor 参照)—「設定されている」の
    // 判定に使う AnchorDef.IsDefault/Equals。LocalScale の 0/1 同一視・Path の null/空文字同一視を検証する。
    public class AnchorDefTests
    {
        [Test]
        public void WorldDefault_IsDefault_IsTrue()
        {
            Assert.IsTrue(AnchorDef.WorldDefault.IsDefault);
        }

        [Test]
        public void BareStructDefault_IsDefault_IsTrue()
        {
            // struct の既定値(全フィールド 0、LocalScale=Vector3.zero)は WorldDefault(LocalScale=one)と
            // 実質同じ意味(AnchorPose.BaseScale が LocalScale==0 を 1 として扱うため)。
            Assert.IsTrue(default(AnchorDef).IsDefault);
        }

        [Test]
        public void NullAndEmptyPath_AreEquivalent()
        {
            var withNull = new AnchorDef { Space = AnchorSpace.World, Path = null, LocalScale = Vector3.one };
            var withEmpty = new AnchorDef { Space = AnchorSpace.World, Path = string.Empty, LocalScale = Vector3.one };

            Assert.AreEqual(withNull, withEmpty);
            Assert.IsTrue(withNull.IsDefault);
        }

        [Test]
        public void NonDefaultSpace_IsNotDefault()
        {
            var def = new AnchorDef { Space = AnchorSpace.NamedObject, Path = "Hand", LocalScale = Vector3.one };
            Assert.IsFalse(def.IsDefault);
        }

        [Test]
        public void NonZeroOffset_IsNotDefault()
        {
            var def = AnchorDef.WorldDefault;
            def.LocalOffset = new Vector3(1f, 0f, 0f);
            Assert.IsFalse(def.IsDefault);
        }

        [Test]
        public void Equals_IgnoresScaleZeroVsOneDifference()
        {
            var zeroScale = new AnchorDef { Space = AnchorSpace.World, LocalScale = Vector3.zero };
            var oneScale = new AnchorDef { Space = AnchorSpace.World, LocalScale = Vector3.one };

            Assert.AreEqual(zeroScale, oneScale);
            Assert.AreEqual(zeroScale.GetHashCode(), oneScale.GetHashCode());
        }
    }
}
