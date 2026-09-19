using System.Reflection;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using DDrive.Runtime.Ui;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;

namespace DDrive.Tests.Editor
{
    // [26_timeline.md] §4.4(Edit Mode プレビュー、2026-09-19) — SE/VFX/UI/AnchorGroup クリップの
    // `Application.isPlaying` → `CutsceneDirectorContext.FireEnabled` 置き換えを、Timeline ウィンドウを
    // 起動せずに検証する(PlayableBehaviour.ProcessFrame/OnBehaviourPause を直接呼ぶ)。
    // 「発火した」ことは private の `_fired` フラグをリフレクションで確認する(静的ファサードは
    // 未 Bind のため実際の音・エフェクトは出ないが、発火経路に入ったかどうかはこれで判定できる)。
    public class CutsceneDirectorContextGatingTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }
        }

        private static bool GetFired(object behaviour)
        {
            var field = behaviour.GetType().GetField("_fired", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{behaviour.GetType().Name} に _fired フィールドが無い");
            return (bool)field.GetValue(behaviour);
        }

        private CutsceneDirectorContext CreateContext(bool fireEnabled)
        {
            _go = new GameObject("CutsceneDirectorContextGatingTests");
            var ctx = _go.AddComponent<CutsceneDirectorContext>();
            ctx.FireEnabled = fireEnabled;
            return ctx;
        }

        // ── SE ──

        [Test]
        public void SeBehaviour_DoesNotFire_WhenFireDisabled()
        {
            var ctx = CreateContext(fireEnabled: false);
            var behaviour = new CutsceneSeBehaviour { SeId = new AssetId<SeMarker>(1UL, AssetType.Se), Context = ctx };

            Assert.DoesNotThrow(() => behaviour.ProcessFrame(Playable.Null, default, null));
            Assert.IsFalse(GetFired(behaviour));
        }

        [Test]
        public void SeBehaviour_Fires_WhenFireEnabled_ViaStaticFacadeFallback()
        {
            var ctx = CreateContext(fireEnabled: true);
            var behaviour = new CutsceneSeBehaviour { SeId = new AssetId<SeMarker>(1UL, AssetType.Se), Context = ctx };

            // ManagerRefs は未設定(null)なので静的ファサードへフォールバックする経路
            // (静的ファサードは未 Bind でも例外にならず no-op で継続する、CLAUDE.md TL;DR #4)。
            Assert.DoesNotThrow(() => behaviour.ProcessFrame(Playable.Null, default, null));
            Assert.IsTrue(GetFired(behaviour));

            Assert.DoesNotThrow(() => behaviour.OnBehaviourPause(Playable.Null, default));
            Assert.IsFalse(GetFired(behaviour));
        }

        [Test]
        public void SeBehaviour_NullContext_DoesNotFire()
        {
            var behaviour = new CutsceneSeBehaviour { SeId = new AssetId<SeMarker>(1UL, AssetType.Se), Context = null };
            Assert.DoesNotThrow(() => behaviour.ProcessFrame(Playable.Null, default, null));
            Assert.IsFalse(GetFired(behaviour));
        }

        // ── VFX ──

        [Test]
        public void VfxBehaviour_DoesNotFire_WhenFireDisabled()
        {
            var ctx = CreateContext(fireEnabled: false);
            var behaviour = new CutsceneVfxBehaviour { VfxId = new AssetId<VfxMarker>(1UL, AssetType.Vfx), Context = ctx };

            Assert.DoesNotThrow(() => behaviour.ProcessFrame(Playable.Null, default, null));
            Assert.IsFalse(GetFired(behaviour));
        }

        [Test]
        public void VfxBehaviour_Fires_WhenFireEnabled()
        {
            var ctx = CreateContext(fireEnabled: true);
            var behaviour = new CutsceneVfxBehaviour { VfxId = new AssetId<VfxMarker>(1UL, AssetType.Vfx), Context = ctx };

            Assert.DoesNotThrow(() => behaviour.ProcessFrame(Playable.Null, default, null));
            Assert.IsTrue(GetFired(behaviour));
        }

        // ── UI ──

        [Test]
        public void UiBehaviour_DoesNotFire_WhenFireDisabled()
        {
            var ctx = CreateContext(fireEnabled: false);
            var behaviour = new CutsceneUiBehaviour { CanvasId = new AssetId<CanvasMarker>(1UL, AssetType.Canvas), Context = ctx };

            Assert.DoesNotThrow(() => behaviour.ProcessFrame(Playable.Null, default, null));
            Assert.IsFalse(GetFired(behaviour));
        }

        [Test]
        public void UiBehaviour_Fires_WhenFireEnabled()
        {
            var ctx = CreateContext(fireEnabled: true);
            var behaviour = new CutsceneUiBehaviour { CanvasId = new AssetId<CanvasMarker>(1UL, AssetType.Canvas), Context = ctx };

            Assert.DoesNotThrow(() => behaviour.ProcessFrame(Playable.Null, default, null));
            Assert.IsTrue(GetFired(behaviour));
        }

        // ── AnchorGroup ──

        [Test]
        public void AnchorGroupBehaviour_DoesNotFire_WhenFireDisabled()
        {
            var ctx = CreateContext(fireEnabled: false);
            var behaviour = new CutsceneAnchorGroupBehaviour { GroupId = new AssetId<DDrive.Runtime.Anchoring.AnchorGroupMarker>(1UL, AssetType.AnchorGroup), Context = ctx };

            Assert.DoesNotThrow(() => behaviour.ProcessFrame(Playable.Null, default, null));
            Assert.IsFalse(GetFired(behaviour));
        }

        [Test]
        public void AnchorGroupBehaviour_Fires_WhenFireEnabled()
        {
            var ctx = CreateContext(fireEnabled: true);
            var behaviour = new CutsceneAnchorGroupBehaviour { GroupId = new AssetId<DDrive.Runtime.Anchoring.AnchorGroupMarker>(1UL, AssetType.AnchorGroup), Context = ctx };

            Assert.DoesNotThrow(() => behaviour.ProcessFrame(Playable.Null, default, null));
            Assert.IsTrue(GetFired(behaviour));
        }

        // ── Presentation(ManagerRefs を持たない。FireEnabled のゲートのみ検証) ──

        [Test]
        public void PresentationBehaviour_DoesNotFire_WhenFireDisabled()
        {
            var ctx = CreateContext(fireEnabled: false);
            var behaviour = new CutscenePresentationBehaviour
            {
                PresentationId = new AssetId<DDrive.Runtime.Presentation.PresentationMarker>(1UL, AssetType.Presentation),
                Context = ctx,
            };

            Assert.DoesNotThrow(() => behaviour.ProcessFrame(Playable.Null, default, null));
            Assert.IsFalse(GetFired(behaviour));
        }
    }
}
