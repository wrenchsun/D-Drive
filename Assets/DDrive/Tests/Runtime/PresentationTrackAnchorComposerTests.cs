using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;
using AnchorId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anchoring.AnchorMarker>;

namespace DDrive.Tests.Runtime
{
    // [08_presentation.md] 実装メモ(2026-09-19、トラック/アセット両方の Anchor 参照) — 3 ケースの判定と
    // 合成(PresentationTrackAnchorComposer)を検証する。AnchorChainTestRegistry(AnchorChainTests.cs)を再利用する。
    public class PresentationTrackAnchorComposerTests
    {
        private VfxData _vfx;
        private SeData _se;

        [SetUp]
        public void SetUp()
        {
            _vfx = ScriptableObject.CreateInstance<VfxData>();
            _se = ScriptableObject.CreateInstance<SeData>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_vfx);
            Object.DestroyImmediate(_se);
        }

        // ── ケース判定 ──

        [Test]
        public void DetermineCase_NeitherSet_ReturnsNeither()
        {
            var track = new PresentationTrack { Kind = TrackKind.Vfx, Anchor = AnchorDef.WorldDefault };
            var kase = PresentationTrackAnchorComposer.DetermineCase(in track, default, AnchorDef.WorldDefault);
            Assert.AreEqual(PresentationTrackAnchorComposer.Case.Neither, kase);
        }

        [Test]
        public void DetermineCase_TrackOnly()
        {
            var track = new PresentationTrack { Kind = TrackKind.Vfx, Anchor = new AnchorDef { Space = AnchorSpace.NamedObject, Path = "Hand", LocalScale = Vector3.one } };
            var kase = PresentationTrackAnchorComposer.DetermineCase(in track, default, AnchorDef.WorldDefault);
            Assert.AreEqual(PresentationTrackAnchorComposer.Case.TrackOnly, kase);
        }

        [Test]
        public void DetermineCase_AssetOnly_ViaEmbedded()
        {
            var track = new PresentationTrack { Kind = TrackKind.Vfx, Anchor = AnchorDef.WorldDefault };
            var embedded = new AnchorDef { Space = AnchorSpace.NamedObject, Path = "Foot", LocalScale = Vector3.one };
            var kase = PresentationTrackAnchorComposer.DetermineCase(in track, default, embedded);
            Assert.AreEqual(PresentationTrackAnchorComposer.Case.AssetOnly, kase);
        }

        [Test]
        public void DetermineCase_AssetOnly_ViaAnchorId()
        {
            var track = new PresentationTrack { Kind = TrackKind.Vfx, Anchor = AnchorDef.WorldDefault };
            var kase = PresentationTrackAnchorComposer.DetermineCase(in track, new AnchorId(1, AssetType.Anchor), AnchorDef.WorldDefault);
            Assert.AreEqual(PresentationTrackAnchorComposer.Case.AssetOnly, kase);
        }

        [Test]
        public void DetermineCase_Both()
        {
            var track = new PresentationTrack { Kind = TrackKind.Vfx, Anchor = new AnchorDef { Space = AnchorSpace.NamedObject, Path = "Hand", LocalScale = Vector3.one } };
            var kase = PresentationTrackAnchorComposer.DetermineCase(in track, new AnchorId(1, AssetType.Anchor), AnchorDef.WorldDefault);
            Assert.AreEqual(PresentationTrackAnchorComposer.Case.Both, kase);
        }

        // ── 合成(ケース1/2) ──

        [Test]
        public void Compose_Neither_ReturnsWorldDefault()
        {
            var track = new PresentationTrack { Kind = TrackKind.Vfx, Anchor = AnchorDef.WorldDefault };
            var spec = PresentationTrackAnchorComposer.Compose(in track, default, AnchorDef.WorldDefault, null, sampleRandom: false);
            Assert.AreEqual(AnchorSpace.World, spec.Def.Space);
            Assert.AreEqual(Vector3.zero, spec.Def.LocalOffset);
        }

        [Test]
        public void Compose_TrackOnly_UsesTrackAnchor()
        {
            var track = new PresentationTrack { Kind = TrackKind.Vfx, Anchor = new AnchorDef { Space = AnchorSpace.World, LocalOffset = new Vector3(1, 2, 3), LocalScale = Vector3.one } };
            var spec = PresentationTrackAnchorComposer.Compose(in track, default, AnchorDef.WorldDefault, null, sampleRandom: false);
            Assert.AreEqual(new Vector3(1, 2, 3), spec.Def.LocalOffset);
        }

        [Test]
        public void Compose_AssetOnly_UsesEmbeddedAnchor()
        {
            var track = new PresentationTrack { Kind = TrackKind.Vfx, Anchor = AnchorDef.WorldDefault };
            var embedded = new AnchorDef { Space = AnchorSpace.World, LocalOffset = new Vector3(4, 5, 6), LocalScale = Vector3.one };
            var spec = PresentationTrackAnchorComposer.Compose(in track, default, embedded, null, sampleRandom: false);
            Assert.AreEqual(new Vector3(4, 5, 6), spec.Def.LocalOffset);
        }

        [Test]
        public void Compose_AssetOnly_ViaAnchorIdChain_MatchesAnchorChainResolve()
        {
            var root = AnchorChainTestRegistry.Anchor(1, offset: new Vector3(1f, 0f, 0f));
            var registry = AnchorChainTestRegistry.Build(root);
            var track = new PresentationTrack { Kind = TrackKind.Vfx, Anchor = AnchorDef.WorldDefault };

            var spec = PresentationTrackAnchorComposer.Compose(in track, AnchorChainTestRegistry.Id(1), AnchorDef.WorldDefault, registry, sampleRandom: false);
            var expected = AnchorChain.Resolve(registry, AnchorChainTestRegistry.Id(1), sampleRandom: false);

            Assert.AreEqual(expected.Def.LocalOffset, spec.Def.LocalOffset);
        }

        // ── 合成(ケース3: 両方設定 = 親子合成) ──

        [Test]
        public void Compose_Both_ComposesTrackAsParentOfEmbeddedAsset()
        {
            // トラック: World 原点から +X 1m, Y+90°回転。
            var track = new PresentationTrack
            {
                Kind = TrackKind.Vfx,
                Anchor = new AnchorDef { Space = AnchorSpace.World, LocalOffset = new Vector3(1f, 0f, 0f), LocalEuler = new Vector3(0f, 90f, 0f), LocalScale = Vector3.one },
            };
            // アセット側(埋め込み): 親の前方(+Z)に 1m。
            var embedded = new AnchorDef { Space = AnchorSpace.World, LocalOffset = new Vector3(0f, 0f, 1f), LocalScale = Vector3.one };

            var spec = PresentationTrackAnchorComposer.Compose(in track, default, embedded, null, sampleRandom: false);

            // 親の向き(Y+90°)で子の前方(+Z)は +X になる: (1,0,0) + (1,0,0) = (2,0,0)。
            Assert.Less(Vector3.Distance(new Vector3(2f, 0f, 0f), spec.Def.LocalOffset), 1e-4f);
            // Space/Path/FollowRotation/DetachOnStop はルート(トラック)のもの(子は無視される)。
            Assert.AreEqual(AnchorSpace.World, spec.Def.Space);
        }

        [Test]
        public void Compose_Both_WithAssetAnchorIdChain_OrdersTrackThenChainRootThenLeaf()
        {
            // アセット側の連鎖: leaf(2) の親が root(1)。
            var root = AnchorChainTestRegistry.Anchor(1, offset: new Vector3(0f, 0f, 1f));
            var leaf = AnchorChainTestRegistry.Anchor(2, offset: new Vector3(0f, 0f, 1f), parent: 1);
            var registry = AnchorChainTestRegistry.Build(root, leaf);

            var track = new PresentationTrack
            {
                Kind = TrackKind.Vfx,
                Anchor = new AnchorDef { Space = AnchorSpace.World, LocalOffset = new Vector3(1f, 0f, 0f), LocalEuler = new Vector3(0f, 90f, 0f), LocalScale = Vector3.one },
            };

            var spec = PresentationTrackAnchorComposer.Compose(in track, AnchorChainTestRegistry.Id(2), AnchorDef.WorldDefault, registry, sampleRandom: false);

            // トラック(+X 1m, Y+90°) → 連鎖ルート(+Z 1m、親の向きで+X) → 連鎖末端(+Z 1m、同じ向きで+X)
            // = (1,0,0) + (1,0,0) + (1,0,0) = (3,0,0)
            Assert.Less(Vector3.Distance(new Vector3(3f, 0f, 0f), spec.Def.LocalOffset), 1e-4f);
        }

        // ── アセット側の判定ヘルパー ──

        [Test]
        public void IsAssetAnchorSet_VfxData_DefaultIsFalse()
        {
            Assert.IsFalse(PresentationTrackAnchorComposer.IsAssetAnchorSet(_vfx));
        }

        [Test]
        public void IsAssetAnchorSet_VfxData_EmbeddedAnchorSetIsTrue()
        {
            _vfx.Anchor = new AnchorDef { Space = AnchorSpace.NamedObject, Path = "Hand", LocalScale = Vector3.one };
            Assert.IsTrue(PresentationTrackAnchorComposer.IsAssetAnchorSet(_vfx));
        }

        [Test]
        public void TryGetAssetAnchor_SeData_ReturnsFieldsAndTrue()
        {
            _se.Anchor = new AnchorDef { Space = AnchorSpace.NamedObject, Path = "Chest", LocalScale = Vector3.one };
            var ok = PresentationTrackAnchorComposer.TryGetAssetAnchor(_se, out var anchorId, out var embedded);
            Assert.IsTrue(ok);
            Assert.AreEqual("Chest", embedded.Path);
            Assert.IsFalse(anchorId.IsValid);
        }

        [Test]
        public void TryGetAssetAnchor_UnsupportedType_ReturnsFalse()
        {
            var other = ScriptableObject.CreateInstance<PresentationData>();
            try
            {
                var ok = PresentationTrackAnchorComposer.TryGetAssetAnchor(other, out _, out var embedded);
                Assert.IsFalse(ok);
                Assert.IsTrue(embedded.IsDefault);
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        // ── Editor 表示用: 段の一覧 ──

        [Test]
        public void ResolveStages_Both_FirstStageIsTrackAnchor()
        {
            var track = new PresentationTrack { Kind = TrackKind.Vfx, Anchor = new AnchorDef { Space = AnchorSpace.World, LocalOffset = Vector3.one, LocalScale = Vector3.one } };
            var embedded = new AnchorDef { Space = AnchorSpace.World, LocalOffset = new Vector3(0f, 0f, 1f), LocalScale = Vector3.one };
            var buffer = new PresentationTrackAnchorComposer.Stage[PresentationTrackAnchorComposer.MaxStages];

            var count = PresentationTrackAnchorComposer.ResolveStages(in track, default, embedded, null, buffer);

            Assert.AreEqual(2, count);
            Assert.AreEqual("トラック Anchor", buffer[0].Label);
            Assert.AreEqual(track.Anchor.LocalOffset, buffer[0].ComposedDef.LocalOffset);
        }

        [Test]
        public void ResolveStages_AssetOnly_SingleStageIsEmbeddedLabel()
        {
            var track = new PresentationTrack { Kind = TrackKind.Vfx, Anchor = AnchorDef.WorldDefault };
            var embedded = new AnchorDef { Space = AnchorSpace.World, LocalOffset = new Vector3(1f, 0f, 0f), LocalScale = Vector3.one };
            var buffer = new PresentationTrackAnchorComposer.Stage[PresentationTrackAnchorComposer.MaxStages];

            var count = PresentationTrackAnchorComposer.ResolveStages(in track, default, embedded, null, buffer);

            Assert.AreEqual(1, count);
            Assert.AreEqual("埋め込み Anchor", buffer[0].Label);
        }
    }
}
