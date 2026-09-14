using UnityEngine;

namespace DDrive.Editor.Presentation
{
    // [08_presentation.md] §4(5-4 追補、2026-09-14) — タイムラインのズーム/パン/目盛り間隔選択の純粋関数群。
    // ユーザー報告(「シークバーでどこにいるか分からない。拡縮が欲しい」)への対応。IMGUI 描画(PresentationEditorWindow.
    // Tracks.cs の DrawTimeline)から呼ばれるが、Unity オブジェクトに依存しないため EditMode テストで検証できる。
    // AnimEditorWindow / TimelineRulerGui(Anim と共用)は無改修 — Presentation 専用にこのクラスを新設した。
    public static class PresentationTimelineZoom
    {
        // 最大ズーム(これより表示範囲を狭くしない)。目盛りの最小候補(1/60s フレーム)より少し広い。
        public const float MinVisibleRange = 0.1f;

        // ズームスライダーの上限倍率(全体表示 = 1x に対して何倍まで拡大できるか)。
        public const float MaxZoomFactor = 50f;

        // 目盛り間隔の候補(秒、細かい→粗い順)。ズーム倍率に応じてこの中から選ぶ([08] 実装メモ参照)。
        public static readonly float[] TickStepCandidatesFineToCoarse = { 1f / 60f, 0.1f, 0.5f, 1f };

        // 1 目盛りがこの px を割り込む候補は使わない(細かすぎて潰れるため)。
        private const float MinPxPerTick = 6f;

        // ラベルの目標間隔(px)。これを下回らないよう、目盛り何本ごとにラベルを出すかを決める。
        private const float TargetLabelPx = 46f;

        // ── 目盛り間隔の自動選択 ──

        // 表示範囲(秒)とバーの幅(px)から、候補のうち「1 目盛りが MinPxPerTick 以上」を満たす
        // 最も細かい間隔を選ぶ(無ければ最も粗い間隔で妥協し、ラベル間引きで見た目を保つ)。
        public static float ChooseTickStep(float visibleSeconds, float barWidthPx)
        {
            if (visibleSeconds <= 0f || barWidthPx <= 0f)
            {
                return TickStepCandidatesFineToCoarse[^1];
            }

            var pxPerSecond = barWidthPx / visibleSeconds;
            foreach (var step in TickStepCandidatesFineToCoarse)
            {
                if (step * pxPerSecond >= MinPxPerTick)
                {
                    return step;
                }
            }

            return TickStepCandidatesFineToCoarse[^1];
        }

        // 目盛り何本ごとにラベルを出すか(1 = 毎回)。pxPerTick が広いほど 1 に近づく。
        public static int LabelStride(float pxPerTick, float targetLabelPx = TargetLabelPx)
        {
            if (pxPerTick <= 0f)
            {
                return 1;
            }

            return Mathf.Max(1, Mathf.CeilToInt(targetLabelPx / pxPerTick));
        }

        // ── 時刻 ⇔ X 座標 ──

        public static float TimeToX(Rect bar, float rangeStart, float rangeEnd, float time)
        {
            var range = Mathf.Max(1e-4f, rangeEnd - rangeStart);
            return bar.x + bar.width * Mathf.Clamp01((time - rangeStart) / range);
        }

        public static float XToTime(Rect bar, float rangeStart, float rangeEnd, float x)
        {
            var range = rangeEnd - rangeStart;
            var width = Mathf.Max(1e-4f, bar.width);
            return rangeStart + Mathf.Clamp01((x - bar.x) / width) * range;
        }

        // ── 表示範囲の操作(すべて ClampRange で [0, displayDuration] かつ [MinVisibleRange, 全体] に収める) ──

        // 「全体表示」: 演出の尺全体(displayDuration)が収まる範囲。
        public static (float start, float end) Fit(float displayDuration)
        {
            var width = Mathf.Max(displayDuration, MinVisibleRange);
            return (0f, width);
        }

        // 範囲を [0, 全体] 内に収め、幅を [MinVisibleRange, 全体] にクランプする。
        public static (float start, float end) ClampRange(float start, float end, float displayDuration)
        {
            var totalWidth = Mathf.Max(displayDuration, MinVisibleRange);
            var width = Mathf.Clamp(end - start, MinVisibleRange, totalWidth);
            var maxStart = Mathf.Max(0f, totalWidth - width);
            var clampedStart = Mathf.Clamp(start, 0f, maxStart);
            return (clampedStart, clampedStart + width);
        }

        // pivotTime(カーソル位置の時刻)を画面上の同じ位置に保ったまま、幅を factor 倍する
        // (factor>1 でズームイン=幅が狭くなる、factor<1 でズームアウト)。Ctrl+ホイールから使う。
        public static (float start, float end) ZoomAroundPivot(float start, float end, float pivotTime, float factor, float displayDuration)
        {
            if (!(factor > 0f) || float.IsNaN(factor))
            {
                return ClampRange(start, end, displayDuration);
            }

            var width = Mathf.Max(1e-4f, end - start);
            var totalWidth = Mathf.Max(displayDuration, MinVisibleRange);
            var newWidth = Mathf.Clamp(width / factor, MinVisibleRange, totalWidth);
            var ratio = Mathf.Clamp01((pivotTime - start) / width);
            var newStart = pivotTime - ratio * newWidth;
            return ClampRange(newStart, newStart + newWidth, displayDuration);
        }

        // ズームスライダー/＋−ボタン用: 絶対倍率(1 = 全体表示)を指定して pivotTime を中心にズームする。
        public static (float start, float end) WithZoomFactor(float start, float end, float pivotTime, float factor, float displayDuration)
        {
            var totalWidth = Mathf.Max(displayDuration, MinVisibleRange);
            var newWidth = Mathf.Clamp(totalWidth / Mathf.Max(1f, factor), MinVisibleRange, totalWidth);
            var width = Mathf.Max(1e-4f, end - start);
            var ratio = Mathf.Clamp01((pivotTime - start) / width);
            var newStart = pivotTime - ratio * newWidth;
            return ClampRange(newStart, newStart + newWidth, displayDuration);
        }

        // 現在の表示範囲に相当する倍率(ズームスライダーの表示・同期用)。
        public static float ZoomFactor(float start, float end, float displayDuration)
        {
            var totalWidth = Mathf.Max(displayDuration, MinVisibleRange);
            var width = Mathf.Max(1e-4f, end - start);
            return totalWidth / width;
        }

        // 横スクロール(ホイール単独 / Shift+ホイール / 下部の横スクロールバーのドラッグ)。
        public static (float start, float end) Pan(float start, float end, float deltaSeconds, float displayDuration)
            => ClampRange(start + deltaSeconds, end + deltaSeconds, displayDuration);

        // 「再生ヘッドに追従」: 再生ヘッドが表示範囲の余白(marginRatio、幅に対する比率)を超えて
        // はみ出そうになったら、はみ出さない位置まで範囲をスクロールする。範囲内なら何もしない。
        public static (float start, float end) FollowPlayhead(float start, float end, float playheadTime, float displayDuration, float marginRatio = 0.1f)
        {
            var width = end - start;
            var margin = width * Mathf.Clamp01(marginRatio);
            if (playheadTime < start + margin)
            {
                var newStart = playheadTime - margin;
                return ClampRange(newStart, newStart + width, displayDuration);
            }

            if (playheadTime > end - margin)
            {
                var newStart = playheadTime - width + margin;
                return ClampRange(newStart, newStart + width, displayDuration);
            }

            return (start, end);
        }
    }
}
