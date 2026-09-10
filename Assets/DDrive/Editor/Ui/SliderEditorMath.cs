using DDrive.Runtime.Ui;

namespace DDrive.Editor.Ui
{
    // [18_ui_controls.md] B-6(4-17) — SliderEditor の応答曲線グラフ / ノッチ可視化バーが使うサンプリングの
    // 純関数部分。EditorWindow(IMGUI 描画)から切り出してテストできるようにする。
    public static class SliderEditorMath
    {
        // x = つまみ位置(0..1、samples 等分)、戻り値[i] = UiSlider.EvaluateResponse(x) = 正規化値。
        public static float[] SampleResponse(UiSlider slider, int samples)
        {
            var result = new float[samples];
            if (slider == null || samples <= 0)
            {
                return result;
            }

            for (var i = 0; i < samples; i++)
            {
                var p = samples == 1 ? 0f : i / (float)(samples - 1);
                result[i] = slider.EvaluateResponse(p);
            }

            return result;
        }

        // notches 等分のノッチ位置(0..1、notches+1 個。両端 0 と 1 を含む)。
        public static float[] NotchPositions(int notches)
        {
            if (notches <= 0)
            {
                return new float[0];
            }

            var result = new float[notches + 1];
            for (var i = 0; i <= notches; i++)
            {
                result[i] = i / (float)notches;
            }

            return result;
        }
    }
}
