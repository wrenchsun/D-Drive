using DDrive.Runtime.Cutscene;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Cutscene
{
    // [26_timeline.md] §5.1(6-10c)「逃げ道: 1 FBX に複数ショット」— `CutsceneData.SourceFrameRange` が
    // 既定(Start=End=0 = FBX 全体)でないとき、埋め込み AnimationClip をその範囲だけに切り出す。
    // 既定のまま(1 ショット = 1 FBX)の場合は何もしない(元の clip をそのまま返す。複製しない)。
    public static class CutsceneFrameRangeTrimmer
    {
        public static bool ShouldTrim(FrameRange range) => range.Start != 0 || range.End != 0;

        // End は含む(フレーム単位、[26] §4.1)。fps<=0(検出できない)場合は切り出せないため元のまま返す。
        public static AnimationClip TrimClip(AnimationClip source, FrameRange range, float fps)
        {
            if (source == null || !ShouldTrim(range) || fps <= 0f)
            {
                return source;
            }

            var startTime = range.Start / fps;
            var endTime = (range.End + 1) / fps;

            var trimmed = new AnimationClip { frameRate = source.frameRate, name = source.name + "_Trim" };
            var bindings = AnimationUtility.GetCurveBindings(source);
            for (var i = 0; i < bindings.Length; i++)
            {
                var curve = AnimationUtility.GetEditorCurve(source, bindings[i]);
                var trimmedCurve = TrimCurve(curve, startTime, endTime);
                if (trimmedCurve != null)
                {
                    AnimationUtility.SetEditorCurve(trimmed, bindings[i], trimmedCurve);
                }
            }

            return trimmed;
        }

        // startTime を新しい 0 秒とし、[startTime, endTime] 外のキーは捨てる。範囲内にキーが 1 つも無ければ、
        // 元カーブをその範囲の代表点(startTime の値)で評価した定数キーを 1 つ入れる(空カーブにしない)。
        public static AnimationCurve TrimCurve(AnimationCurve curve, float startTime, float endTime)
        {
            if (curve == null || curve.length == 0)
            {
                return curve;
            }

            var result = new AnimationCurve();
            var keys = curve.keys;
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                if (key.time < startTime || key.time > endTime)
                {
                    continue;
                }

                key.time -= startTime;
                result.AddKey(key);
            }

            if (result.length == 0)
            {
                var firstTime = keys[0].time;
                var lastTime = keys[keys.Length - 1].time;
                var clamped = Mathf.Clamp(startTime, firstTime, lastTime);
                result.AddKey(0f, curve.Evaluate(clamped));
            }

            return result;
        }
    }
}
