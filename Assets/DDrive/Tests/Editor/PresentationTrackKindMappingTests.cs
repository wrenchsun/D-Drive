using DDrive.Editor.Presentation;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Anim2D;
using DDrive.Runtime.Audio;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Haptics;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Ui;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [08_presentation.md] §4(5-4) — PresentationEditor の D&D Kind 判定(具象 Data 型 → TrackKind)と
    // Asset 欄の objectType 決定(TrackKind → 具象 Data 型)がズレていないことを検証する。
    public class PresentationTrackKindMappingTests
    {
        [Test]
        public void TryKindFor_Anim2DData_ResolvesToAnim2D_NotAnim()
        {
            // Anim2DData : AnimData なので、先に Anim2D と判定できないと常に Anim に化けてしまう。
            var data = ScriptableObject.CreateInstance<Anim2DData>();
            Assert.IsTrue(PresentationTrackKindMapping.TryKindFor(data, out var kind));
            Assert.AreEqual(TrackKind.Anim2D, kind);
            Object.DestroyImmediate(data);
        }

        [Test]
        public void TryKindFor_AnimData_ResolvesToAnim()
        {
            var data = ScriptableObject.CreateInstance<AnimData>();
            Assert.IsTrue(PresentationTrackKindMapping.TryKindFor(data, out var kind));
            Assert.AreEqual(TrackKind.Anim, kind);
            Object.DestroyImmediate(data);
        }

        [TestCase(typeof(SeData), TrackKind.Se)]
        [TestCase(typeof(BgmData), TrackKind.Bgm)]
        [TestCase(typeof(VfxData), TrackKind.Vfx)]
        [TestCase(typeof(CameraShakeData), TrackKind.CameraShake)]
        [TestCase(typeof(HapticsData), TrackKind.Haptic)]
        [TestCase(typeof(CanvasData), TrackKind.Canvas)]
        [TestCase(typeof(UiTweenData), TrackKind.UiTween)]
        public void TryKindFor_EachConcreteType_ResolvesExpectedKind(System.Type dataType, TrackKind expected)
        {
            var data = (DDrive.Foundation.Data.AssetDataBase)ScriptableObject.CreateInstance(dataType);
            Assert.IsTrue(PresentationTrackKindMapping.TryKindFor(data, out var kind));
            Assert.AreEqual(expected, kind);
            Object.DestroyImmediate(data);
        }

        [Test]
        public void TryKindFor_UnrelatedAssetType_ReturnsFalse()
        {
            var data = ScriptableObject.CreateInstance<DDrive.Runtime.Model.ModelData>();
            Assert.IsFalse(PresentationTrackKindMapping.TryKindFor(data, out _));
            Object.DestroyImmediate(data);
        }

        [TestCase(TrackKind.Anim, typeof(AnimData))]
        [TestCase(TrackKind.Anim2D, typeof(Anim2DData))]
        [TestCase(TrackKind.Se, typeof(SeData))]
        [TestCase(TrackKind.Bgm, typeof(BgmData))]
        [TestCase(TrackKind.Vfx, typeof(VfxData))]
        [TestCase(TrackKind.CameraShake, typeof(CameraShakeData))]
        [TestCase(TrackKind.Haptic, typeof(HapticsData))]
        [TestCase(TrackKind.Canvas, typeof(CanvasData))]
        [TestCase(TrackKind.UiTween, typeof(UiTweenData))]
        public void AssetTypeFor_MatchesTryKindFor_RoundTrip(TrackKind kind, System.Type expectedType)
        {
            Assert.AreEqual(expectedType, PresentationTrackKindMapping.AssetTypeFor(kind));
        }

        [TestCase(TrackKind.HitStop)]
        [TestCase(TrackKind.Timeline)]
        [TestCase(TrackKind.Marker)]
        [TestCase(TrackKind.Signal)]
        public void AssetTypeFor_AssetlessKinds_ReturnsNull(TrackKind kind)
        {
            Assert.IsNull(PresentationTrackKindMapping.AssetTypeFor(kind));
        }

        [TestCase(TrackKind.Vfx, AssetType.Vfx)]
        [TestCase(TrackKind.CameraShake, AssetType.Shake)]
        [TestCase(TrackKind.Haptic, AssetType.Haptics)]
        [TestCase(TrackKind.Anim2D, AssetType.Anim2D)]
        public void AssetKindFor_MatchesAssetTypeEnum(TrackKind kind, AssetType expected)
        {
            Assert.AreEqual(expected, PresentationTrackKindMapping.AssetKindFor(kind));
        }
    }
}
