using System;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Tests.Runtime
{
    // [15_ui_interaction.md] B-3.5 — チケット 4-11 完了分(近似実装の解消 + 新規プロパティ)のテスト。
    // UiTweenTests.cs を肥大化させないための姉妹ファイル(同じ SetUp/CreateRect 流儀)。
    public class UiPresetTests
    {
        private AssetRegistry _registry;
        private UiTweenManager _manager;

        [SetUp]
        public void SetUp()
        {
            _registry = new AssetRegistry(new FakeAssetLoader());
            _manager = new UiTweenManager(_registry);
        }

        private static RectTransform CreateRect(string name = "Target", bool withGraphic = false)
        {
            var go = withGraphic
                ? new GameObject(name, typeof(RectTransform), typeof(Image))
                : new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(100f, 40f);
            rt.localScale = Vector3.one;

            var parentGo = new GameObject("Parent", typeof(RectTransform));
            var parentRt = (RectTransform)parentGo.transform;
            parentRt.sizeDelta = new Vector2(800f, 600f);
            rt.SetParent(parentRt, false);

            return rt;
        }

        // ── 全プリセット網羅 ──

        [Test]
        public void AllPresets_ProduceFiniteTracks_AndAreNotApproximated()
        {
            foreach (UiPreset preset in Enum.GetValues(typeof(UiPreset)))
            {
                if (preset == UiPreset.None)
                {
                    continue;
                }

                Assert.IsFalse(UiPresetFactory.IsApproximation(preset), $"{preset} は近似実装が残っている");

                var rt = CreateRect("Preset_" + preset, withGraphic: true);
                var buffer = new TweenTrack[UiTweenManager.MaxTracksPerTween];
                var p = new UiPresetRef { Preset = preset, Duration = 0.2f };
                var count = UiPresetFactory.Build(in p, rt, buffer);

                Assert.Greater(count, 0, $"{preset} は 1 本以上の Track を生成するはず");

                var handle = _manager.PlayTracks(buffer, count, rt);
                for (var i = 0; i < 40; i++)
                {
                    _manager.Tick(0.05f);
                }

                // ループ系は自然完了しないため、明示的に Stop する。
                if (_manager.IsPlaying(handle))
                {
                    _manager.Stop(handle);
                }

                Assert.IsTrue(float.IsFinite(rt.anchoredPosition.x) && float.IsFinite(rt.anchoredPosition.y), $"{preset}: anchoredPosition が非有限値");
                Assert.IsTrue(float.IsFinite(rt.localScale.x) && float.IsFinite(rt.localScale.y) && float.IsFinite(rt.localScale.z), $"{preset}: localScale が非有限値");
                Assert.IsTrue(float.IsFinite(rt.localEulerAngles.x) && float.IsFinite(rt.localEulerAngles.y) && float.IsFinite(rt.localEulerAngles.z), $"{preset}: localEulerAngles が非有限値");
            }
        }

        // ── RotationX/Y ──

        [Test]
        public void FlipInX_AppliesToLocalEulerAngleX_NotZ()
        {
            var rt = CreateRect();
            var buffer = new TweenTrack[UiTweenManager.MaxTracksPerTween];
            var p = new UiPresetRef { Preset = UiPreset.FlipInX, Duration = 0.2f };
            var count = UiPresetFactory.Build(in p, rt, buffer);

            _manager.PlayTracks(buffer, count, rt);
            _manager.Tick(0f); // t=0: From 値(90°)がそのまま反映される

            Assert.AreEqual(90f, rt.localEulerAngles.x, 1f);
            Assert.AreEqual(0f, rt.localEulerAngles.z, 0.01f);
        }

        [Test]
        public void FlipInY_AppliesToLocalEulerAngleY_NotZ()
        {
            var rt = CreateRect();
            var buffer = new TweenTrack[UiTweenManager.MaxTracksPerTween];
            var p = new UiPresetRef { Preset = UiPreset.FlipInY, Duration = 0.2f };
            var count = UiPresetFactory.Build(in p, rt, buffer);

            _manager.PlayTracks(buffer, count, rt);
            _manager.Tick(0f);

            Assert.AreEqual(90f, rt.localEulerAngles.y, 1f);
            Assert.AreEqual(0f, rt.localEulerAngles.z, 0.01f);
        }

        // ── Jelly ──

        [Test]
        public void Jelly_EndsAtOriginalScale()
        {
            var rt = CreateRect();
            rt.localScale = Vector3.one;

            var buffer = new TweenTrack[UiTweenManager.MaxTracksPerTween];
            var p = new UiPresetRef { Preset = UiPreset.Jelly, Duration = 0.2f };
            var count = UiPresetFactory.Build(in p, rt, buffer);

            var handle = _manager.PlayTracks(buffer, count, rt);
            for (var i = 0; i < 40; i++)
            {
                _manager.Tick(0.05f);
            }

            Assert.IsFalse(_manager.IsPlaying(handle), "Jelly は loopCount 指定の PingPong なので自然完了するはず");
            Assert.AreEqual(1f, rt.localScale.x, 0.02f);
            Assert.AreEqual(1f, rt.localScale.y, 0.02f);
            Assert.AreEqual(1f, rt.localScale.z, 0.02f);
        }

        // ── RainbowTint ──

        [Test]
        public void RainbowTint_ChangesGraphicHueOverTime_AndKeepsLoopingUntilStop()
        {
            var rt = CreateRect(withGraphic: true);
            var image = rt.GetComponent<Image>();
            image.color = Color.white;

            var buffer = new TweenTrack[UiTweenManager.MaxTracksPerTween];
            var p = new UiPresetRef { Preset = UiPreset.RainbowTint, Duration = 1f };
            var count = UiPresetFactory.Build(in p, rt, buffer);

            var handle = _manager.PlayTracks(buffer, count, rt);

            _manager.Tick(0.25f);
            Color.RGBToHSV(image.color, out var h1, out _, out _);

            _manager.Tick(0.5f);
            Color.RGBToHSV(image.color, out var h2, out _, out _);

            Assert.Greater(Mathf.Abs(h1 - h2), 0.1f, "0.25s と 0.75s とで色相が十分に変化しているはず");

            // Loop なので 1 周期を大きく超えても再生中のまま。
            for (var i = 0; i < 30; i++)
            {
                _manager.Tick(0.1f);
            }

            Assert.IsTrue(_manager.IsPlaying(handle), "RainbowTint は無限ループなので Stop するまで完了しない");
            _manager.Stop(handle);
            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        // ── AttentionJump ──

        [Test]
        public void AttentionJump_ReturnsToStartY_AndCompletes()
        {
            var rt = CreateRect();
            var startY = rt.anchoredPosition.y;

            var buffer = new TweenTrack[UiTweenManager.MaxTracksPerTween];
            var p = new UiPresetRef { Preset = UiPreset.AttentionJump, Duration = 0.4f, Distance = 20f };
            var count = UiPresetFactory.Build(in p, rt, buffer);
            Assert.AreEqual(2, count, "AttentionJump は上昇/下降の 2 Track のはず");

            var handle = _manager.PlayTracks(buffer, count, rt);
            for (var i = 0; i < 20; i++)
            {
                _manager.Tick(0.05f);
            }

            Assert.IsFalse(_manager.IsPlaying(handle));
            Assert.AreEqual(startY, rt.anchoredPosition.y, 0.05f);
        }
    }
}
