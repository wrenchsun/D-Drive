using System.Linq;
using DDrive.Editor.CanvasTool;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Tests.Editor
{
    // [07_canvas_prefab.md] 2026-10-12(C-1) — DD-CANVAS-SCALER-MISMATCH。
    public class CanvasScalerConsistencyValidatorTests
    {
        private readonly System.Collections.Generic.List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
            {
                Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        private CanvasData Make(string name, UiLayer layer, Vector2? reference)
        {
            var prefab = new GameObject(name + "Prefab", typeof(RectTransform));
            _created.Add(prefab);
            if (reference.HasValue)
            {
                var s = prefab.AddComponent<CanvasScaler>();
                s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                s.referenceResolution = reference.Value;
            }

            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.name = name;
            data.Layer = layer;
            data.Prefab = prefab;
            _created.Add(data);
            return data;
        }

        private static bool HasMismatch(CanvasData target, params AssetDataBase[] all)
        {
            var ctx = new ValidationContext(all);
            return new CanvasScalerConsistencyValidator().Validate(target, ctx).Any(r => r.Code == "DD-CANVAS-SCALER-MISMATCH");
        }

        [Test]
        public void SameLayer_DifferentScaler_Warns()
        {
            var a = Make("!a", UiLayer.Loading, new Vector2(1920, 1080));
            var b = Make("!b", UiLayer.Loading, new Vector2(2560, 1440));
            Assert.IsTrue(HasMismatch(b, a, b));
            Assert.IsFalse(HasMismatch(a, a, b)); // 基準側には出さない
        }

        [Test]
        public void SameLayer_SameScaler_NoWarning()
        {
            var a = Make("!a", UiLayer.Loading, new Vector2(1920, 1080));
            var b = Make("!b", UiLayer.Loading, new Vector2(1920, 1080));
            Assert.IsFalse(HasMismatch(b, a, b));
        }

        [Test]
        public void DifferentLayer_OrNoScaler_NoWarning()
        {
            var a = Make("!a", UiLayer.Loading, new Vector2(1920, 1080));
            var other = Make("!b", UiLayer.Overlay, new Vector2(2560, 1440));
            var none = Make("!c", UiLayer.Loading, null);
            Assert.IsFalse(HasMismatch(other, a, other));
            Assert.IsFalse(HasMismatch(none, a, none));
        }
    }
}
