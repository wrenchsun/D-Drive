using System.Text.RegularExpressions;
using DDrive.Editor.Anim2D;
using DDrive.Foundation.Values;
using DDrive.Runtime.Anim2D;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Editor
{
    // [05_model_animation.md] C-5 — 方向 Clip への一括リタイミング(2026-09-11)。
    public class Anim2DRetimingTests
    {
        private Texture2D _texture;
        private Sprite[] _sprites;

        [SetUp]
        public void SetUp()
        {
            _texture = new Texture2D(8, 8);
            _sprites = new Sprite[4];
            for (var i = 0; i < _sprites.Length; i++)
            {
                _sprites[i] = Sprite.Create(_texture, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f));
                _sprites[i].name = "s" + i;
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var s in _sprites)
            {
                Object.DestroyImmediate(s);
            }

            Object.DestroyImmediate(_texture);
        }

        private AnimationClip MakeClip(int frames, float length)
        {
            var clip = new AnimationClip { frameRate = 12f };
            var sprites = new Sprite[frames];
            var times = new float[frames];
            for (var i = 0; i < frames; i++)
            {
                sprites[i] = _sprites[i % _sprites.Length];
                times[i] = (float)i / frames; // RebuildClip は正規化時刻(0..1)を受け取る
            }

            Assert.IsTrue(AnimationClipEditorUtility.RebuildClip(clip, sprites, times, length));
            return clip;
        }

        [Test]
        public void ApplyToDirectionClips_RetimesMatchingClips_SkipsOthers()
        {
            var main = MakeClip(4, 1f);
            var dir0 = MakeClip(4, 1f);
            var dir90 = MakeClip(4, 1f);
            var odd = MakeClip(3, 1f); // 枚数が違う → スキップ
            var data = ScriptableObject.CreateInstance<Anim2DData>();
            data.Clip = main;
            data.DirectionClips = new[] { main, dir0, dir90, odd, null };
            try
            {
                var applied = Anim2DRetiming.ApplyToDirectionClips(data, PlacementMode.Uniform, 2f, 4, out var skipped);

                Assert.AreEqual(2, applied, "主 Clip と null を除き、枚数一致の 2 本に適用");
                Assert.AreEqual(1, skipped);
                Assert.IsTrue(AnimationClipEditorUtility.LoadSprites(dir0, out _, out var times0));
                Assert.AreEqual(4, times0.Length);
                Assert.AreEqual(0.5f, times0[1], 1e-3f, "2 秒 / 4 枚 = 0.5 秒間隔");
                Assert.IsTrue(AnimationClipEditorUtility.LoadSprites(odd, out _, out var timesOdd));
                Assert.AreEqual(1f / 3f, timesOdd[1], 1e-3f, "スキップした Clip は元のまま");
            }
            finally
            {
                Object.DestroyImmediate(data);
                Object.DestroyImmediate(main);
                Object.DestroyImmediate(dir0);
                Object.DestroyImmediate(dir90);
                Object.DestroyImmediate(odd);
            }
        }

        // 既定の Retiming(Constant01(1))では全フレームが末尾に潰れるので、1 本も書き換えず警告だけ出す(2026-09-11 レビュー対応)。
        [Test]
        public void ApplyToDirectionClips_ConstantRetiming_WarnsAndLeavesClipsUntouched()
        {
            var main = MakeClip(4, 1f);
            var dir0 = MakeClip(4, 1f);
            var data = ScriptableObject.CreateInstance<Anim2DData>();
            data.Clip = main;
            data.DirectionClips = new[] { main, dir0 };
            data.Retiming = ValueDef.Constant01(1f);
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("単調増加"));
                var applied = Anim2DRetiming.ApplyToDirectionClips(data, PlacementMode.Retiming, 2f, 4, out var skipped);

                Assert.AreEqual(0, applied);
                Assert.AreEqual(0, skipped);
                Assert.IsTrue(AnimationClipEditorUtility.LoadSprites(dir0, out _, out var times0));
                Assert.AreEqual(0.25f, times0[1], 1e-3f, "元の等間隔(1 秒 / 4 枚)のまま");
            }
            finally
            {
                Object.DestroyImmediate(data);
                Object.DestroyImmediate(main);
                Object.DestroyImmediate(dir0);
            }
        }

        // 長さが 1 秒でない Clip でも、正規化時刻 x totalSeconds で配置される。
        [Test]
        public void ApplyToDirectionClips_NonUnitLength_ScalesTimes()
        {
            var main = MakeClip(4, 0.5f);
            var dir0 = MakeClip(4, 0.5f);
            var data = ScriptableObject.CreateInstance<Anim2DData>();
            data.Clip = main;
            data.DirectionClips = new[] { main, dir0 };
            try
            {
                Assert.AreEqual(1, Anim2DRetiming.ApplyToDirectionClips(data, PlacementMode.Uniform, 0.5f, 4, out var skipped));
                Assert.AreEqual(0, skipped);
                Assert.IsTrue(AnimationClipEditorUtility.LoadSprites(dir0, out _, out var times0));
                Assert.AreEqual(0.125f, times0[1], 1e-3f, "0.5 秒 / 4 枚 = 0.125 秒間隔");
                Assert.AreEqual(0.375f, times0[3], 1e-3f);
            }
            finally
            {
                Object.DestroyImmediate(data);
                Object.DestroyImmediate(main);
                Object.DestroyImmediate(dir0);
            }
        }

        [Test]
        public void ApplyToDirectionClips_NoClips_ReturnsZero()
        {
            var data = ScriptableObject.CreateInstance<Anim2DData>();
            try
            {
                Assert.AreEqual(0, Anim2DRetiming.ApplyToDirectionClips(data, PlacementMode.Uniform, 1f, 4, out var skipped));
                Assert.AreEqual(0, skipped);
                Assert.AreEqual(0, Anim2DRetiming.ApplyToDirectionClips(null, PlacementMode.Uniform, 1f, 4, out _));
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }
    }
}
