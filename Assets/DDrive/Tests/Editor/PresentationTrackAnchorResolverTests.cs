using DDrive.Editor.Presentation;
using DDrive.Foundation.Data;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [08_presentation.md] SceneView Anchor 表示 — PresentationEditorWindow(ウィンドウ)を起動せずに
    // 「このトラックは今どこに出るか」の解決を検証する。実装調査で確認した事実(PresentationManager.FireVfx/
    // FireSe は常に track.Anchor を presolved spec として渡し、参照先 VfxData/SeData の AnchorId/埋め込み
    // Anchor は一切参照しない)をそのまま契約としてテストする。
    public class PresentationTrackAnchorResolverTests
    {
        private GameObject _self;
        private GameObject _boneChild;

        [SetUp]
        public void SetUp()
        {
            _self = new GameObject("Self");
            _self.transform.position = new Vector3(1f, 0f, 0f);
            _boneChild = new GameObject("RightHand");
            _boneChild.transform.SetParent(_self.transform);
            _boneChild.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_self);
        }

        [TestCase(TrackKind.Vfx, true)]
        [TestCase(TrackKind.Se, true)]
        [TestCase(TrackKind.AnchorGroup, true)]
        [TestCase(TrackKind.Anim, false)]
        [TestCase(TrackKind.Anim2D, false)]
        [TestCase(TrackKind.Bgm, false)]
        [TestCase(TrackKind.CameraShake, false)]
        [TestCase(TrackKind.Haptic, false)]
        [TestCase(TrackKind.HitStop, false)]
        [TestCase(TrackKind.Timeline, false)]
        [TestCase(TrackKind.Canvas, false)]
        [TestCase(TrackKind.UiTween, false)]
        [TestCase(TrackKind.Marker, false)]
        [TestCase(TrackKind.Signal, false)]
        public void HasPosition_MatchesKindsThatConsumeAnchor(TrackKind kind, bool expected)
        {
            Assert.AreEqual(expected, PresentationTrackAnchorResolver.HasPosition(kind));
        }

        [Test]
        public void Resolve_NonPositionalKind_ReturnsHasPositionFalse()
        {
            var track = new PresentationTrack { Kind = TrackKind.CameraShake };

            var result = PresentationTrackAnchorResolver.Resolve(in track, _self.transform, null);

            Assert.IsFalse(result.HasPosition);
        }

        [Test]
        public void Resolve_TargetSelf_ResolvesBoneUnderSelf()
        {
            var track = new PresentationTrack
            {
                Kind = TrackKind.Vfx,
                Target = TrackTargetMode.Self,
                Anchor = new AnchorDef { Space = AnchorSpace.NamedObject, Path = "RightHand" },
            };

            var result = PresentationTrackAnchorResolver.Resolve(in track, _self.transform, null);

            Assert.IsTrue(result.HasPosition);
            Assert.AreSame(_boneChild.transform, result.BaseTransform);
        }

        [Test]
        public void Resolve_TargetContextTarget_UsesTargetArgument_NotSelf()
        {
            var otherTargetGo = new GameObject("Target");
            var boneUnderTarget = new GameObject("Chest");
            boneUnderTarget.transform.SetParent(otherTargetGo.transform);

            try
            {
                var track = new PresentationTrack
                {
                    Kind = TrackKind.Se,
                    Target = TrackTargetMode.ContextTarget,
                    Anchor = new AnchorDef { Space = AnchorSpace.NamedObject, Path = "Chest" },
                };

                // 統合プレビューは常に ctx.Target=null で再生するため、target=null のときはワールド原点扱いになる
                // (見つからず null)。ここでは「ctx.Target が渡された場合」の解決も別途確認する。
                var withoutTarget = PresentationTrackAnchorResolver.Resolve(in track, _self.transform, null);
                Assert.IsNull(withoutTarget.BaseTransform);

                var withTarget = PresentationTrackAnchorResolver.Resolve(in track, _self.transform, otherTargetGo.transform);
                Assert.AreSame(boneUnderTarget.transform, withTarget.BaseTransform);
            }
            finally
            {
                Object.DestroyImmediate(otherTargetGo);
            }
        }

        [TestCase(TrackTargetMode.World)]
        [TestCase(TrackTargetMode.Anchor)]
        public void Resolve_WorldOrAnchorTarget_IgnoresSelfAndTarget(TrackTargetMode mode)
        {
            var track = new PresentationTrack
            {
                Kind = TrackKind.Vfx,
                Target = mode,
                Anchor = new AnchorDef { Space = AnchorSpace.NamedObject, Path = "RightHand" },
            };

            var result = PresentationTrackAnchorResolver.Resolve(in track, _self.transform, null);

            // World/Anchor は PlayContext を参照しない(PresentationManager.ResolveContextRoot が null を返す)ので、
            // Path 指定があっても解決対象が無くワールド原点扱いになる。
            Assert.IsTrue(result.HasPosition);
            Assert.IsNull(result.BaseTransform);
        }

        [Test]
        public void Resolve_PathNotFound_FallsBackToWorldOrigin()
        {
            var track = new PresentationTrack
            {
                Kind = TrackKind.Vfx,
                Target = TrackTargetMode.Self,
                Anchor = new AnchorDef { Space = AnchorSpace.NamedObject, Path = "NoSuchBone" },
            };

            var result = PresentationTrackAnchorResolver.Resolve(in track, _self.transform, null);

            Assert.IsNull(result.BaseTransform);
        }

        [Test]
        public void ResolveAnchorGroupPoints_Grid3x3_EnumeratesNinePointsAroundBone()
        {
            var group = ScriptableObject.CreateInstance<AnchorGroupData>();
            try
            {
                group.Layout = AnchorLayoutKind.Grid;
                group.GridCountX = 3;
                group.GridCountY = 1;
                group.GridCountZ = 3;
                group.GridSpacing = Vector3.one;
                group.GridCentered = true;
                group.Origin = new AnchorDef { Space = AnchorSpace.NamedObject, Path = "RightHand" };

                var buffer = new AnchorSpawnSpec[AnchorGroupData.MaxPoints];
                var count = PresentationTrackAnchorResolver.ResolveAnchorGroupPoints(
                    registry: null,
                    group,
                    _self.transform,
                    null,
                    TrackTargetMode.Self,
                    buffer,
                    out var baseTransform,
                    out var extraOffset);

                Assert.AreEqual(9, count);
                Assert.AreSame(_boneChild.transform, baseTransform);
                Assert.AreEqual(Vector3.zero, extraOffset);
                Assert.Less(Vector3.Distance(Vector3.zero, buffer[4].Def.LocalOffset), 1e-4f, "中央は原点");
            }
            finally
            {
                Object.DestroyImmediate(group);
            }
        }

        [Test]
        public void ResolveAnchorGroupPoints_NullGroup_ReturnsZero()
        {
            var buffer = new AnchorSpawnSpec[AnchorGroupData.MaxPoints];

            var count = PresentationTrackAnchorResolver.ResolveAnchorGroupPoints(
                registry: null, null, _self.transform, null, TrackTargetMode.Self, buffer, out var baseTransform, out _);

            Assert.AreEqual(0, count);
            Assert.IsNull(baseTransform);
        }
    }
}
