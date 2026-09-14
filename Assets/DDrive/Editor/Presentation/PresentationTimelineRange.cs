using System;
using DDrive.Foundation.Data;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Presentation;
using UnityEngine;

namespace DDrive.Editor.Presentation
{
    // [08_presentation.md] §4(5-4 追補、2026-09-14) — タイムラインの「表示範囲」(ルーラー/ズーム/シーク/
    // 「全体表示」が基準にする尺)。ランタイムの完了判定(PresentationTiming.EffectiveDuration、
    // Runtime/Presentation)は変更しない — TotalDuration が 0(未設定)のときに EffectiveDuration が
    // (AtTime トラックが無ければ 0 のまま、あっても余白無しの)値を返すため、そのままタイムラインの
    // 表示範囲に使うと極端に狭く(あるいは 0 に)潰れて「今どこにいるか分からない」原因になっていた
    // (ユーザー報告)。表示専用にこのクラスを分け、Runtime 側の契約(0 なら最初の Tick で即完了する等)には
    // 一切触れない。
    public static class PresentationTimelineRange
    {
        // TotalDuration 未設定でトラックの最大時刻に足す余白(秒)。
        // 「トラックの最後に合わせる」ボタンで、アセットの長さが分からないトラックの見込み尾ひれにも使う。
        public const float AutoMargin = 0.5f;

        // トラックが 1 つも無い(または AtTime トラックが無い)ときのフォールバック尺(秒)。
        public const float FallbackNoTracksDuration = 1f;

        // タイムラインの描画・シーク範囲・「全体表示」が使う尺。
        public static float DisplayDuration(PresentationData data)
        {
            if (data == null)
            {
                return FallbackNoTracksDuration;
            }

            if (data.TotalDuration > 0f)
            {
                return data.TotalDuration;
            }

            return TryGetMaxAtTimeTrackTime(data.Tracks, out var max) ? max + AutoMargin : FallbackNoTracksDuration;
        }

        // 「共通設定」の尺 0 警告の「トラックの最後に合わせる」ボタンが設定する値。
        // 各トラックの終了時刻(Time + 分かる場合はアセットの長さ、分からなければ AutoMargin)の最大値。
        // アセットの解決(AssetDatabase 検索)を伴うため、ウィンドウから呼ぶのはこちらの無引数版。
        public static float SuggestedTotalDuration(PresentationData data)
            => SuggestedTotalDuration(data, ResolveAssetForLength);

        // テスト用(および上記の実装): アセット解決を差し替え可能にした純粋版。
        // resolveAsset は (Kind, AssetId) → 解決済み AssetDataBase(見つからなければ null)。
        public static float SuggestedTotalDuration(PresentationData data, Func<TrackKind, ulong, AssetDataBase> resolveAsset)
        {
            if (data?.Tracks == null || !TryGetMaxAtTimeTrackTime(data.Tracks, out _))
            {
                return FallbackNoTracksDuration;
            }

            var maxEnd = 0f;
            var tracks = data.Tracks;
            for (var i = 0; i < tracks.Length; i++)
            {
                var track = tracks[i];
                if (track.Trigger != TrackTrigger.AtTime)
                {
                    continue;
                }

                var asset = track.Asset.IsAssigned ? resolveAsset?.Invoke(track.Kind, track.Asset.Id) : null;
                var tail = EstimateAssetTailSeconds(track.Kind, asset, out var known);
                var end = track.Time + (known ? tail : AutoMargin);
                if (end > maxEnd)
                {
                    maxEnd = end;
                }
            }

            return maxEnd;
        }

        // AtTime トラックの最大 Time([08] PresentationTiming.EffectiveDuration と同じ走査)。
        // found=false は「AtTime トラックが 1 つも無い」(max は常に 0)。
        public static bool TryGetMaxAtTimeTrackTime(PresentationTrack[] tracks, out float max)
        {
            max = 0f;
            if (tracks == null)
            {
                return false;
            }

            var found = false;
            for (var i = 0; i < tracks.Length; i++)
            {
                if (tracks[i].Trigger != TrackTrigger.AtTime)
                {
                    continue;
                }

                found = true;
                if (tracks[i].Time > max)
                {
                    max = tracks[i].Time;
                }
            }

            return found;
        }

        // ベストエフォート: 判明する種別(Anim/Anim2D=LengthSec、Se=Clips の最長 - StartOffsetSec)だけ
        // 加味する。それ以外(Vfx/Bgm/CameraShake/Haptic/…)はループ/曲線ベースで固定長を持たないため
        // 「分からない」扱いにする(要判断。将来 VfxData 等に明示的な長さの概念が増えたら拡張する)。
        public static float EstimateAssetTailSeconds(TrackKind kind, AssetDataBase asset, out bool known)
        {
            known = false;
            switch (kind)
            {
                case TrackKind.Anim:
                case TrackKind.Anim2D:
                    if (asset is AnimData anim)
                    {
                        known = true;
                        return anim.LengthSec;
                    }

                    break;
                case TrackKind.Se:
                    if (asset is SeData se && se.Clips != null)
                    {
                        var maxClip = 0f;
                        foreach (var clip in se.Clips)
                        {
                            if (clip != null && clip.length > maxClip)
                            {
                                maxClip = clip.length;
                            }
                        }

                        if (maxClip > 0f)
                        {
                            known = true;
                            return Mathf.Max(0f, maxClip - se.StartOffsetSec);
                        }
                    }

                    break;
            }

            return 0f;
        }

        private static AssetDataBase ResolveAssetForLength(TrackKind kind, ulong id)
            => PresentationTrackKindMapping.FindAssetById(PresentationTrackKindMapping.AssetTypeFor(kind), id);
    }
}
