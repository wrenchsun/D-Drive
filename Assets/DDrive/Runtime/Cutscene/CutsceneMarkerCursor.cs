using System;
using System.Collections.Generic;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace DDrive.Runtime.Cutscene
{
    // [26_timeline.md] §4.3/§4.4 実装メモ — 時刻順マーカーの「跨いだら進む、Seek は無音でスキップ」カーソル。
    // `CutsceneManager.CollectMarkers`/`AdvanceMarkers`(6-10b)と同じパターンを、Edit Mode の
    // `CutsceneEditModePreviewProvider` の監視役でも使えるように独立したユーティリティとして切り出した
    // (2026-09-19)。CutsceneManager 自身の内部実装は既存のフィールド構成のままにしている(よく検証された
    // Play Mode 経路を、見た目だけの共通化のために触るリスクを避けるため)。
    public sealed class CutsceneMarkerCursor<TMarker> where TMarker : Marker
    {
        private readonly List<(double Time, TMarker Marker)> _items = new();

        public int Cursor { get; private set; }

        public int Count => _items.Count;

        // Timeline 上の全トラックから TMarker 型のマーカーだけを時刻順に集め直す(Play() 相当)。
        public void Collect(TimelineAsset timeline)
        {
            _items.Clear();
            Cursor = 0;
            if (timeline == null)
            {
                return;
            }

            foreach (var track in timeline.GetOutputTracks())
            {
                if (track == null)
                {
                    continue;
                }

                foreach (var marker in track.GetMarkers())
                {
                    if (marker is TMarker typed)
                    {
                        _items.Add((marker.time, typed));
                    }
                }
            }

            _items.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        // 巻き戻し検出時等、カーソルだけを先頭に戻す(Collect し直さない軽量版)。
        public void ResetCursor() => Cursor = 0;

        // newElapsed <= 跨いだマーカーの分だけ action を呼ぶ。fire=false は「Skip/巻き戻しで既に通過した」
        // 扱いにして無音でカーソルだけ進める(CutsceneManager.AdvanceMarkers と同じ意味)。
        public void Advance(double newElapsed, bool fire, Action<TMarker> action)
        {
            while (Cursor < _items.Count && _items[Cursor].Time <= newElapsed)
            {
                var marker = _items[Cursor].Marker;
                Cursor++;

                if (fire)
                {
                    action?.Invoke(marker);
                }
            }
        }
    }
}
