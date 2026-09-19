using DDrive.Foundation.Easing;
using DDrive.Foundation.Values;
using DDrive.Runtime.Ui;
using UnityEditor;

namespace DDrive.Editor.Ui
{
    // [18_ui_controls.md] B-6(4-17) — スライダーのプリセット 5 種(音量 / 感度 / HP バー / スタミナ / キャラメイク)。
    // 元は SliderSkinEditorWindow だけが持っていた最小実装(4-15/4-18)だったが、SliderEditor(4-17)からも
    // 同じ内容を使うため共有 static へ切り出した(実装メモ: docs/18_ui_controls.md B-6 2026-09-11)。
    // Apply は Undo.RecordObject で包む(target は Prefab/シーン上のコンポーネント、またはプレビュー用 DontSave 実体)。
    public static class SliderPresets
    {
        public enum SliderPreset
        {
            None,
            音量,
            感度,
            HPバー,
            スタミナ,
            キャラメイク,
        }

        public static void Apply(UiSlider slider, SliderPreset preset)
        {
            if (slider == null || preset == SliderPreset.None)
            {
                return;
            }

            Undo.RecordObject(slider, $"UiSlider: プリセット '{preset}' を適用");

            // 2026-09-14: 入力の許可は HP バー(表示専用)だけが OFF にする。他のプリセットに切り替えたら ON に戻す。
            slider.PointerInput = true;
            slider.NavigationInput = true;

            switch (preset)
            {
                case SliderPreset.音量:
                    slider.SetRange(0f, 1f);
                    slider.Step = 0f;
                    slider.Notches = 0;
                    slider.Response = new ValueDef { Mode = ValueMode.Parametric, Parametric = EaseDef.Named(Ease.InQuad), From = 0f, To = 1f };
                    slider.FollowMotion = ValueDef.Constant01(0f);
                    break;

                case SliderPreset.感度:
                    slider.SetRange(0.1f, 5f);
                    slider.Step = 0f;
                    slider.Notches = 0;
                    slider.Response = new ValueDef { Mode = ValueMode.Parametric, Parametric = EaseDef.Named(Ease.OutQuad), From = 0f, To = 1f };
                    break;

                case SliderPreset.HPバー:
                    slider.SetRange(0f, 100f);
                    slider.Step = 1f;
                    slider.Notches = 0;
                    slider.Response = default;
                    slider.FollowMotion = new ValueDef { Mode = ValueMode.Parametric, Parametric = EaseDef.Named(Ease.OutCubic), Time = TimeDef.Duration(0.25f), Loop = LoopMode.Once };
                    slider.PointerInput = false;
                    slider.NavigationInput = false;
                    break;

                case SliderPreset.スタミナ:
                    slider.SetRange(0f, 100f);
                    slider.Step = 1f;
                    slider.Notches = 4;
                    slider.SnapThreshold = 0.03f;
                    slider.Response = default;
                    break;

                case SliderPreset.キャラメイク:
                    slider.SetRange(0f, 10f);
                    slider.Step = 1f;
                    slider.WholeNumbers = true;
                    slider.Notches = 10;
                    slider.Response = default;
                    break;
            }

            EditorUtility.SetDirty(slider);
        }
    }
}
