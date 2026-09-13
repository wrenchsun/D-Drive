using UnityEditor;
using UnityEditorInternal;

namespace DDrive.Editor.Preview
{
    // (レビュー対応 2026-09-14) 確認用シーンのプレビューが動いている間の描き直しを 30fps 上限に間引く([09] §8.1 と同じ)。
    // Edit Mode の Game ビューは自動では再描画されず、Game ビューだけを描き直す公開 API も無いため
    // InternalEditorUtility.RepaintAllViews を使う(これが無いと Game ビューに最後の姿しか映らない)。
    // 以前は EditorApplication.update ごと(100fps 以上)に全ビューを描き直していた。
    public sealed class ViewRepaintThrottle
    {
        public const double DefaultInterval = 1.0 / 30.0;

        private readonly double _interval;
        private double _next;

        public ViewRepaintThrottle(double interval = DefaultInterval)
        {
            _interval = interval > 0.0 ? interval : DefaultInterval;
        }

        // force=true は間引かずに描き直す(動きが止まった最後の 1 回など、最終の姿を必ず映したいとき)。
        public void Request(bool force = false)
        {
            var now = EditorApplication.timeSinceStartup;
            if (!force && now < _next)
            {
                return;
            }

            _next = now + _interval;
            InternalEditorUtility.RepaintAllViews();
        }
    }
}
