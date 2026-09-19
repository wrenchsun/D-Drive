using DDrive.Editor.Anim2D;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Values;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [05_model_animation.md] Part C / チケット 3-11 — 既存ツール移植ロジックのユニットテスト。
    public class Anim2DToolTests
    {
        private const string TestRoot = "Packages/com.ddrive.core/Tests/Editor/Temp";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            }
        }

        private static void EnsureTestRoot()
        {
            if (!AssetDatabase.IsValidFolder(TestRoot))
            {
                AssetDatabase.CreateFolder("Packages/com.ddrive.core/Tests/Editor", "Temp");
            }
        }

        // ── NamingRuleResolver ──

        [Test]
        public void BuildClipName_WithAngle_AppendsAngleSuffix()
        {
            Assert.AreEqual("Anim_Player_Run_90", NamingRuleResolver.BuildClipName("Player", "Run", 90));
        }

        [Test]
        public void BuildClipName_WithoutAngle_OmitsSuffix()
        {
            Assert.AreEqual("Anim_Player_Idle", NamingRuleResolver.BuildClipName("Player", "Idle", -1));
        }

        [TestCase("Player_Run_0", true, 0)]
        [TestCase("Player_Run_315", true, 315)]
        [TestCase("Player_Run", false, -1)]
        [TestCase("Player_Run_999", false, -1)]
        public void TryExtractAngleFromName_MatchesKnownSuffixesOnly(string textureName, bool expectedSuccess, int expectedAngle)
        {
            var success = NamingRuleResolver.TryExtractAngleFromName(textureName, out var angle);
            Assert.AreEqual(expectedSuccess, success);
            if (expectedSuccess)
            {
                Assert.AreEqual(expectedAngle, angle);
            }
        }

        // ── DirectionAngle ──

        [Test]
        public void DirectionAngle_Map_HasEightEntries()
        {
            Assert.AreEqual(8, DirectionAngle.Map.Length);
        }

        [TestCase(0)]
        [TestCase(45)]
        [TestCase(90)]
        [TestCase(135)]
        [TestCase(180)]
        [TestCase(225)]
        [TestCase(270)]
        [TestCase(315)]
        public void DirectionAngle_AngleToPositionRoundTrip(int angle)
        {
            Assert.IsTrue(DirectionAngle.TryGetPosition(angle, out var position));
            Assert.Greater(position.magnitude, 0.9f); // 単位ベクトル(丸め誤差込み)
            var mode = DirectionAngle.FromAngle(angle);
            Assert.AreEqual(angle, DirectionAngle.ToAngle(mode));
        }

        [Test]
        public void DirectionAngle_UnknownAngle_ReturnsFalse()
        {
            Assert.IsFalse(DirectionAngle.TryGetPosition(999, out _));
            Assert.AreEqual(DirectionMode.None, DirectionAngle.FromAngle(999));
        }

        // ── AnimationClipEditorUtility ──

        [Test]
        public void BuildUniformTimes_IsMonotonicInZeroToOne()
        {
            var times = AnimationClipEditorUtility.BuildUniformTimes(5);
            Assert.AreEqual(5, times.Length);
            for (var i = 0; i < times.Length; i++)
            {
                Assert.GreaterOrEqual(times[i], 0f);
                Assert.Less(times[i], 1f);
                if (i > 0)
                {
                    Assert.Greater(times[i], times[i - 1]);
                }
            }
        }

        [Test]
        public void BuildRetimingTimes_EasedValues_StayInZeroToOne()
        {
            var retiming = new ValueDef
            {
                Mode = ValueMode.Parametric,
                Parametric = EaseDef.Named(Ease.OutBack), // オーバーシュートするイージングでもクランプされる
                From = 0f,
                To = 1f,
            };

            var times = AnimationClipEditorUtility.BuildRetimingTimes(8, retiming);
            Assert.AreEqual(8, times.Length);
            foreach (var t in times)
            {
                Assert.GreaterOrEqual(t, 0f);
                Assert.LessOrEqual(t, 1f);
            }
        }

        [Test]
        public void BuildTimes_UniformMode_MatchesBuildUniformTimes()
        {
            var expected = AnimationClipEditorUtility.BuildUniformTimes(4);
            var actual = AnimationClipEditorUtility.BuildTimes(4, PlacementMode.Uniform, ValueDef.Constant01(1f));
            CollectionAssert.AreEqual(expected, actual);
        }

        // ── AnimationClipBuilder ──

        [Test]
        public void Build_CreatesClipWithSpriteKeysAtGivenFrameRate()
        {
            EnsureTestRoot();
            var sprites = CreateTestSprites(4);

            var success = AnimationClipBuilder.Build(
                sprites, TestRoot, "TestClip", frameRate: 12,
                lengthMode: ClipLengthMode.Frames, length: 4f, loop: true, out var clip);

            Assert.IsTrue(success);
            Assert.IsNotNull(clip);
            Assert.AreEqual(12f, clip.frameRate);

            Assert.IsTrue(AnimationClipEditorUtility.LoadSprites(clip, out var loadedSprites, out var times));
            Assert.AreEqual(4, loadedSprites.Length);
            Assert.AreEqual(4, times.Length);

            var texture = sprites.Length > 0 ? sprites[0].texture : null;
            foreach (var s in sprites)
            {
                Object.DestroyImmediate(s);
            }

            if (texture != null)
            {
                Object.DestroyImmediate(texture);
            }
        }

        // ── AutomaticSpriteSlicer ──

        // Codex レビュー 2026-09-10 P1: DetectRects はユーザーがスライスを確定する前に
        // テクスチャの mipmap/圧縮設定を恒久的に書き換えてはいけない。
        // mipmap ON・Compressed でインポートしたテクスチャに対して DetectRects を呼び、
        // 呼び出し後もその設定が保たれていることを検証する。
        [Test]
        public void DetectRects_DoesNotMutatePersistedImporterSettings()
        {
            EnsureTestRoot();
            var path = $"{TestRoot}/DetectRectsTemp.png";

            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var clear = new Color32(0, 0, 0, 0);
            var opaque = new Color32(255, 255, 255, 255);
            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    var inCenter = x is >= 2 and < 6 && y is >= 2 and < 6;
                    texture.SetPixel(x, y, inCenter ? opaque : clear);
                }
            }

            texture.Apply();
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.alphaIsTransparency = true;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            var loaded = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.IsNotNull(loaded);

            var success = AutomaticSpriteSlicer.DetectRects(
                loaded, minSpriteSize: 1, extrudeSize: 0,
                out var rects, out _, out _, out _);

            Assert.IsTrue(success);
            Assert.IsNotNull(rects);
            Assert.Greater(rects.Length, 0, "透明背景の中央に不透明な矩形があるので 1 件以上検出されるはず");

            var importerAfter = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.IsTrue(importerAfter.mipmapEnabled, "DetectRects 後も mipmap 設定が復元されているはず");
            Assert.AreEqual(TextureImporterCompression.Compressed, importerAfter.textureCompression, "DetectRects 後も圧縮設定が復元されているはず");
        }

        // Codex レビュー 2026-09-10 P2: BuildWithTimes は Build と同じ frameRate/totalSeconds 検証を持つべき。
        [Test]
        public void BuildWithTimes_NonPositiveFrameRate_ReturnsFalse_AndDoesNotThrow()
        {
            EnsureTestRoot();
            var sprites = CreateTestSprites(2);

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*frameRate.*"));
            var success = true;
            AnimationClip clip = null;
            Assert.DoesNotThrow(() => success = AnimationClipBuilder.BuildWithTimes(
                sprites, AnimationClipEditorUtility.BuildUniformTimes(2), TestRoot, "InvalidFrameRateClip",
                frameRate: 0, totalSeconds: 1f, loop: false, out clip));

            Assert.IsFalse(success);
            Assert.IsNull(clip);

            var texture = sprites.Length > 0 ? sprites[0].texture : null;
            foreach (var s in sprites)
            {
                Object.DestroyImmediate(s);
            }

            if (texture != null)
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void BuildWithTimes_NonPositiveTotalSeconds_ReturnsFalse_AndDoesNotThrow()
        {
            EnsureTestRoot();
            var sprites = CreateTestSprites(2);

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*totalSeconds.*"));
            var success = true;
            AnimationClip clip = null;
            Assert.DoesNotThrow(() => success = AnimationClipBuilder.BuildWithTimes(
                sprites, AnimationClipEditorUtility.BuildUniformTimes(2), TestRoot, "InvalidTotalSecondsClip",
                frameRate: 12, totalSeconds: 0f, loop: false, out clip));

            Assert.IsFalse(success);
            Assert.IsNull(clip);

            var texture = sprites.Length > 0 ? sprites[0].texture : null;
            foreach (var s in sprites)
            {
                Object.DestroyImmediate(s);
            }

            if (texture != null)
            {
                Object.DestroyImmediate(texture);
            }
        }

        // ── BlendTreeRegistrar ──

        [Test]
        public void Register_CreatesFreeformDirectional2DBlendTree_WithXyParameters()
        {
            EnsureTestRoot();
            var controller = AnimatorController.CreateAnimatorControllerAtPath($"{TestRoot}/TestController.controller");
            var clip = new AnimationClip { name = "DirClip0" };
            AssetDatabase.AddObjectToAsset(clip, controller);

            var registered = BlendTreeRegistrar.Register(controller, 0, "Run", clip, 0, angle => BlendTreeConflictResolution.Overwrite);
            Assert.IsTrue(registered);

            var hasX = false;
            var hasY = false;
            foreach (var p in controller.parameters)
            {
                if (p.name == "x")
                {
                    hasX = true;
                }

                if (p.name == "y")
                {
                    hasY = true;
                }
            }

            Assert.IsTrue(hasX);
            Assert.IsTrue(hasY);

            AnimatorState state = null;
            foreach (var cs in controller.layers[0].stateMachine.states)
            {
                if (cs.state.name == "Run")
                {
                    state = cs.state;
                }
            }

            Assert.IsNotNull(state);
            var tree = state.motion as BlendTree;
            Assert.IsNotNull(tree);
            Assert.AreEqual(BlendTreeType.FreeformDirectional2D, tree.blendType);
            Assert.AreEqual(1, tree.children.Length);
            Assert.IsTrue(DirectionAngle.TryGetPosition(0, out var expectedPos));
            Assert.Less(Vector2.Distance(expectedPos, tree.children[0].position), 1e-3f);
        }

        // ── Anim2DImportProfile ──

        [Test]
        public void FindOrDefault_WithNoAssetInProject_ReturnsBuiltInDefault()
        {
            var profile = Anim2DImportProfile.FindOrDefault();
            Assert.IsNotNull(profile);
            Assert.Greater(profile.DefaultFrameRate, 0);
        }

        private static Sprite[] CreateTestSprites(int count)
        {
            var texture = new Texture2D(4, 4);
            var sprites = new Sprite[count];
            for (var i = 0; i < count; i++)
            {
                sprites[i] = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
                sprites[i].name = $"TestSprite_{i}";
            }

            return sprites;
        }
    }
}
