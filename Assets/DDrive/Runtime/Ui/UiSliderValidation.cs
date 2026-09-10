using System.Collections.Generic;
using DDrive.Foundation.Validation;
using DDrive.Foundation.Values;
using UnityEngine;

namespace DDrive.Runtime.Ui
{
    // [18_ui_controls.md] B-7 — UiSlider コンポーネント単体の検査。CanvasDataValidator(4-16 配線検査)と
    // 将来の SliderEditor(4-17)の両方から再利用できるよう static にしてある。
    public static class UiSliderValidation
    {
        public static void Validate(UiSlider slider, string path, List<ValidationResult> results)
        {
            if (slider == null || results == null)
            {
                return;
            }

            if (slider.Min >= slider.Max)
            {
                results.Add(ValidationResult.Error($"{path}: Min({slider.Min}) が Max({slider.Max}) 以上です"));
            }

            if (!IsResponseMonotonic(slider.Response))
            {
                results.Add(ValidationResult.Error($"{path}: Response が単調増加ではありません(つまみ位置が一意に決まりません)"));
            }

            if (slider.Step > 0f)
            {
                var range = slider.Max - slider.Min;
                var ratio = range / slider.Step;
                if (!Mathf.Approximately(ratio, Mathf.Round(ratio)))
                {
                    results.Add(ValidationResult.Warning($"{path}: Step が (Max-Min) を割り切れません(端に到達できない刻みです)"));
                }
            }

            if (slider.Notches > 0 && slider.Step > 0f)
            {
                var expectedNotches = (slider.Max - slider.Min) / slider.Step;
                if (!Mathf.Approximately(expectedNotches, slider.Notches))
                {
                    results.Add(ValidationResult.Error($"{path}: Notches({slider.Notches}) と Step から求まる分割数({expectedNotches})が一致しません"));
                }
            }

            if (slider.WholeNumbers && slider.Step > 0f && !Mathf.Approximately(slider.Step, Mathf.Round(slider.Step)))
            {
                results.Add(ValidationResult.Error($"{path}: WholeNumbers=true なのに Step({slider.Step})が非整数です"));
            }

            if (slider.FollowMotion.Loop != LoopMode.Once)
            {
                results.Add(ValidationResult.Error($"{path}: FollowMotion が Loop 設定です(表示値が収束しません)"));
            }
        }

        // 32 サンプルで非単調(前より減少)がないかを見る。Mode=Constant(未設定既定)は線形として常に単調。
        private static bool IsResponseMonotonic(ValueDef response)
        {
            if (response.Mode == ValueMode.Constant)
            {
                return true;
            }

            const int samples = 32;
            var previous = float.NegativeInfinity;
            for (var i = 0; i <= samples; i++)
            {
                var t = i / (float)samples;
                var v = response.Evaluate(t);
                if (v < previous - 1e-4f)
                {
                    return false;
                }

                previous = v;
            }

            return true;
        }
    }
}
