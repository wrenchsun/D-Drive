using System;
using System.Collections.Generic;
using DDrive.Editor.Anim;
using DDrive.Editor.Anim2D;
using DDrive.Editor.Inspector;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Anim2D;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [05_model_animation.md] C-5/C-6 — チケット 3-13(Anim2DEditor: 共通プレビュー移植 + イベント D&D + Validator)。
    public class Anim2DEditorTests
    {
        private const string TestRoot = TestTempFolder.Root + "/Temp3-13";

        private readonly List<UnityEngine.Object> _scratch = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _scratch)
            {
                if (obj != null)
                {
                    UnityEngine.Object.DestroyImmediate(obj);
                }
            }

            _scratch.Clear();

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
                TestTempFolder.CreateFolder("Temp3-13");
            }
        }

        private T Track<T>(T obj) where T : UnityEngine.Object
        {
            _scratch.Add(obj);
            return obj;
        }

        private Sprite CreateSprite()
        {
            var tex = Track(new Texture2D(4, 4));
            return Track(Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f)));
        }

        private AnimationClip CreateClipWithSpriteKeys(string name, params Sprite[] sprites)
        {
            var clip = Track(new AnimationClip { name = name });
            var binding = EditorCurveBinding.PPtrCurve(string.Empty, typeof(SpriteRenderer), "m_Sprite");
            var keyframes = new ObjectReferenceKeyframe[sprites.Length];
            for (var i = 0; i < sprites.Length; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe { time = i * 0.1f, value = sprites[i] };
            }

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
            return clip;
        }

        // ── Anim2DPreviewObject ──

        [Test]
        public void Create_PlacesDontSaveSpriteAnimatorObject_WithFirstSprite()
        {
            var sprite = CreateSprite();
            var clip = CreateClipWithSpriteKeys("PreviewClip", sprite);
            var data = Track(ScriptableObject.CreateInstance<Anim2DData>());
            data.Clip = clip;

            var go = Track(Anim2DPreviewObject.Create(data));

            Assert.IsNotNull(go);
            Assert.AreEqual(Anim2DPreviewObject.Name, go.name);
            Assert.AreEqual(HideFlags.DontSave, go.hideFlags);
            Assert.IsNotNull(go.GetComponent<SpriteRenderer>());
            Assert.IsNotNull(go.GetComponent<Animator>());
            Assert.AreEqual(sprite, go.GetComponent<SpriteRenderer>().sprite);

            Anim2DPreviewObject.Destroy(go);
            Assert.IsTrue(go == null); // Unity の破棄済みオブジェクト比較(fake-null)
            _scratch.Remove(go);
        }

        [Test]
        public void Create_WithoutClip_DoesNotThrow_AndLeavesSpriteEmpty()
        {
            var data = Track(ScriptableObject.CreateInstance<Anim2DData>());

            GameObject go = null;
            Assert.DoesNotThrow(() => go = Anim2DPreviewObject.Create(data));
            Track(go);

            Assert.IsNull(go.GetComponent<SpriteRenderer>().sprite);
        }

        // ── Anim2DEditorValidator: (a) BlendTree の x, y パラメータ ──

        [Test]
        public void Validator_MissingBlendTreeParameters_ReportsError_AndFixActionAddsThem()
        {
            EnsureTestRoot();
            var controllerPath = TestRoot + "/Anim2DValidatorController.controller";
            // 永続アセット(TestRoot 配下)。DestroyImmediate は使わず TearDown の AssetDatabase.DeleteAsset(TestRoot) で片付ける。
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var state = controller.layers[0].stateMachine.AddState("Run");
            var blendTree = new BlendTree { name = "Run", blendType = BlendTreeType.FreeformDirectional2D };
            AssetDatabase.AddObjectToAsset(blendTree, controller);
            state.motion = blendTree;
            using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            AssetDatabase.Refresh();

            // コントローラには x, y パラメータを意図的に追加していない。
            Assert.IsEmpty(controller.parameters);

            var data = Track(ScriptableObject.CreateInstance<Anim2DData>());
            data.Directions = DirectionSet.Four;
            data.StateName = "Run";
            data.ParamXName = "x";
            data.ParamYName = "y";

            var ctx = new ValidationContext(new List<AssetDataBase> { data });
            var results = new List<ValidationResult>(new Anim2DEditorValidator().Validate(data, ctx));

            var paramError = results.Find(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("x") && r.Message.Contains("y"));
            Assert.IsNotNull(paramError.FixAction, "x, y パラメータ不足の Error に FixAction が無い");

            paramError.FixAction();

            Assert.IsTrue(HasFloatParameter(controller, "x"));
            Assert.IsTrue(HasFloatParameter(controller, "y"));
        }

        [Test]
        public void Validator_NoDirections_NeverChecksBlendTreeParameters()
        {
            var data = Track(ScriptableObject.CreateInstance<Anim2DData>());
            data.Directions = DirectionSet.None;
            data.StateName = "Idle";

            var ctx = new ValidationContext(new List<AssetDataBase> { data });
            var results = new List<ValidationResult>(new Anim2DEditorValidator().Validate(data, ctx));

            Assert.IsFalse(results.Exists(r => r.Message.Contains("パラメータ")));
        }

        private static bool HasFloatParameter(AnimatorController controller, string name)
        {
            foreach (var p in controller.parameters)
            {
                if (p.name == name && p.type == AnimatorControllerParameterType.Float)
                {
                    return true;
                }
            }

            return false;
        }

        // ── Anim2DEditorValidator: (b) スライス済みスプライトの参照切れ ──

        [Test]
        public void Validator_ClipWithNullSpriteKey_ReportsError()
        {
            var sprite = CreateSprite();
            var clip = CreateClipWithSpriteKeys("BrokenClip", sprite, null);

            var data = Track(ScriptableObject.CreateInstance<Anim2DData>());
            data.Directions = DirectionSet.None;
            data.Clip = clip;

            var ctx = new ValidationContext(new List<AssetDataBase> { data });
            var results = new List<ValidationResult>(new Anim2DEditorValidator().Validate(data, ctx));

            var spriteError = results.Find(r => r.Severity == ValidationSeverity.Error && r.Message.Contains(clip.name));
            Assert.IsNotNull(spriteError.Message, "参照切れスプライトの Error が出ていません");
            StringAssert.Contains("1", spriteError.Message);
        }

        [Test]
        public void Validator_ClipWithAllSpriteKeysValid_ReportsNothing()
        {
            var sprite = CreateSprite();
            var clip = CreateClipWithSpriteKeys("OkClip", sprite, sprite);

            var data = Track(ScriptableObject.CreateInstance<Anim2DData>());
            data.Directions = DirectionSet.None;
            data.Clip = clip;

            var ctx = new ValidationContext(new List<AssetDataBase> { data });
            var results = new List<ValidationResult>(new Anim2DEditorValidator().Validate(data, ctx));

            Assert.IsEmpty(results);
        }

        // ── DataEditorRegistry: Anim2DData → Anim2DEditorWindow + AnimEditorWindow(基底型継承) ──

        [Test]
        public void DataEditorRegistry_ResolvesAnim2DData_ToBothEditors()
        {
            var entries = DataEditorRegistry.GetEntries(typeof(Anim2DData));
            Assert.IsTrue(HasWindow(entries, typeof(Anim2DEditorWindow)), "Anim2DEditorWindow が登録されていません");
            Assert.IsTrue(HasWindow(entries, typeof(AnimEditorWindow)), "基底 AnimData の AnimEditorWindow が継承で引けていません");
        }

        private static bool HasWindow(IReadOnlyList<DataEditorRegistry.Entry> entries, Type windowType)
        {
            foreach (var entry in entries)
            {
                if (entry.WindowType == windowType)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
