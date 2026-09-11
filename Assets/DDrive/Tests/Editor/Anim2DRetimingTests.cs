using DDrive.Editor.Anim2D;
using DDrive.Runtime.Anim2D;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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
                times[i] = length * i / frames;
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
