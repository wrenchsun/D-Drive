using DDrive.Foundation.Values;
using DDrive.Runtime.Ui;

namespace DDrive.Editor.Ui
{
    // [15_ui_interaction.md] B-6 — UiTweenEditorWindow「カーブ一覧」の各行に出す 1 行サマリー(チケット 4-10)。
    // 見た目の形はここでは決めない(実際の曲線描画は UiTweenEditorWindow 側の IMGUIContainer が
    // ValueDef.Evaluate を直接サンプリングする)。ここは文字列だけを担当する。
    public static class TweenTrackSummary
    {
        public static string Describe(in TweenTrack track)
        {
            var mode = DescribeMode(track.Motion);
            var duration = track.Motion.Duration;
            var loop = DescribeLoop(track.Motion);
            return $"{track.Property} / {mode} / {duration:0.###}s / {loop} / delay {track.Delay:0.###}s";
        }

        private static string DescribeMode(in ValueDef motion) => motion.Mode switch
        {
            ValueMode.Constant => "Const",
            ValueMode.Parametric => motion.Parametric.Kind == ParametricKind.CustomBezier
                ? "Bezier"
                : $"Ease:{motion.Parametric.Ease}",
            ValueMode.Curve => "Curve",
            _ => motion.Mode.ToString(),
        };

        private static string DescribeLoop(in ValueDef motion) => motion.Loop switch
        {
            LoopMode.Once => "Once",
            LoopMode.Loop => motion.LoopCount > 0 ? $"Loop x{motion.LoopCount}" : "Loop",
            LoopMode.PingPong => motion.LoopCount > 0 ? $"PingPong x{motion.LoopCount}" : "PingPong",
            _ => motion.Loop.ToString(),
        };
    }
}
