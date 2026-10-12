using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Editor.CanvasTool
{
    // [07_canvas_prefab.md] 2026-10-12(C-1) — 同じ Layer の CanvasData 群で、Prefab の CanvasScaler 設定が食い違うと
    // Warning(DD-CANVAS-SCALER-MISMATCH)。UiManager はレイヤーごとに 1 つの CanvasScaler を共有し、先に開いた Canvas の
    // 設定を使うため、食い違う Canvas は意図した拡大縮小にならない。同じレイヤーで比べる基準は、名前順で最初の
    // 「CanvasScaler を持つ Prefab」の CanvasData(それ自身には出さない)。新規検査なので Warning のみ。
    public sealed class CanvasScalerConsistencyValidator : IValidator
    {
        public AssetType Target => AssetType.Canvas;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not CanvasData canvas || canvas.Prefab == null)
            {
                yield break;
            }

            var mine = FindScaler(canvas.Prefab);
            if (mine == null)
            {
                yield break;
            }

            CanvasData reference = null;
            CanvasScaler referenceScaler = null;
            foreach (var other in CanvasEmbeddedValidator.BuildLookup(ctx).All)
            {
                if (other == null || other.Layer != canvas.Layer || other.Prefab == null)
                {
                    continue;
                }

                var scaler = FindScaler(other.Prefab);
                if (scaler == null)
                {
                    continue;
                }

                if (reference == null || string.CompareOrdinal(other.name, reference.name) < 0)
                {
                    reference = other;
                    referenceScaler = scaler;
                }
            }

            if (reference == null || reference == canvas || Same(mine, referenceScaler))
            {
                yield break;
            }

            yield return ValidationResult.Warning(
                $"Layer={canvas.Layer} の CanvasScaler 設定が、同じレイヤーの Canvas '{reference.name}' の Prefab と食い違っています" +
                "(レイヤーは 1 つの CanvasScaler を共有し、先に開いた Canvas の設定が使われます。同じレイヤーでは Prefab の CanvasScaler を揃えてください)",
                code: "DD-CANVAS-SCALER-MISMATCH");
        }

        private static CanvasScaler FindScaler(GameObject prefab)
        {
            var scaler = prefab.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                return scaler;
            }

            var t = prefab.transform;
            for (var i = 0; i < t.childCount; i++)
            {
                scaler = t.GetChild(i).GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    return scaler;
                }
            }

            return null;
        }

        internal static bool Same(CanvasScaler a, CanvasScaler b)
        {
            if (a.uiScaleMode != b.uiScaleMode || !Mathf.Approximately(a.referencePixelsPerUnit, b.referencePixelsPerUnit))
            {
                return false;
            }

            switch (a.uiScaleMode)
            {
                case CanvasScaler.ScaleMode.ScaleWithScreenSize:
                    return a.referenceResolution == b.referenceResolution
                           && a.screenMatchMode == b.screenMatchMode
                           && Mathf.Approximately(a.matchWidthOrHeight, b.matchWidthOrHeight);
                case CanvasScaler.ScaleMode.ConstantPhysicalSize:
                    return a.physicalUnit == b.physicalUnit
                           && Mathf.Approximately(a.fallbackScreenDPI, b.fallbackScreenDPI)
                           && Mathf.Approximately(a.defaultSpriteDPI, b.defaultSpriteDPI);
                default:
                    return Mathf.Approximately(a.scaleFactor, b.scaleFactor);
            }
        }
    }
}
