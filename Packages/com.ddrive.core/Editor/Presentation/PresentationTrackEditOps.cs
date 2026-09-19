using System;
using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Runtime.Presentation;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Presentation
{
    // [08_presentation.md] §4(5-4) — トラック追加/時間移動/複製/削除を Undo 1 回で行う純粋な操作群。
    // PresentationEditorWindow(UI)から呼ばれるが、UI 状態(ウィンドウ/SerializedObject)に依存しないため、
    // ウィンドウを起動せずに Undo 往復をテストできる([11_tasks.md] 5-4 のテスト要件)。
    public static class PresentationTrackEditOps
    {
        public static PresentationTrack CreateTrack(TrackKind kind, float time, AssetDataBase asset)
        {
            var track = new PresentationTrack
            {
                Trigger = TrackTrigger.AtTime,
                Time = Mathf.Max(0f, Mathf.Round(time * 100f) / 100f),
                Kind = kind,
                Target = TrackTargetMode.Self,
                Anchor = AnchorDef.WorldDefault,
                StopOnCancel = true,
            };

            if (asset != null && PresentationDataValidator.RequiresAsset(kind))
            {
                track.Asset = new AssetRef { Type = PresentationTrackKindMapping.AssetKindFor(kind), Id = asset.Id };
            }

            return track;
        }

        // 追加後のインデックスを返す。
        public static int AddTrack(PresentationData data, TrackKind kind, float time, AssetDataBase asset)
        {
            if (data == null)
            {
                return -1;
            }

            Undo.RecordObject(data, "Add Presentation Track");
            var list = new List<PresentationTrack>(data.Tracks ?? Array.Empty<PresentationTrack>()) { CreateTrack(kind, time, asset) };
            data.Tracks = list.ToArray();
            EditorUtility.SetDirty(data);
            return list.Count - 1;
        }

        public static void DuplicateTrack(PresentationData data, int index)
        {
            if (data?.Tracks == null || index < 0 || index >= data.Tracks.Length)
            {
                return;
            }

            Undo.RecordObject(data, "Duplicate Presentation Track");
            var list = new List<PresentationTrack>(data.Tracks);
            list.Insert(index + 1, list[index]);
            data.Tracks = list.ToArray();
            EditorUtility.SetDirty(data);
        }

        public static void RemoveTrack(PresentationData data, int index)
        {
            if (data?.Tracks == null || index < 0 || index >= data.Tracks.Length)
            {
                return;
            }

            Undo.RecordObject(data, "Remove Presentation Track");
            var list = new List<PresentationTrack>(data.Tracks);
            list.RemoveAt(index);
            data.Tracks = list.ToArray();
            EditorUtility.SetDirty(data);
        }

        // タイムライン上のドラッグ 1 フレーム分。呼び出し側(DrawTimeline)がドラッグ中は毎フレーム呼び、
        // MouseUp で Undo.CollapseUndoOperations して 1 回にまとめる(AnimEditorWindow.DrawTimeline と同じ手法)。
        public static void SetTrackTime(PresentationData data, int index, float time)
        {
            if (data?.Tracks == null || index < 0 || index >= data.Tracks.Length)
            {
                return;
            }

            Undo.RecordObject(data, "Move Presentation Track");
            var track = data.Tracks[index];
            track.Time = Mathf.Max(0f, Mathf.Round(time * 100f) / 100f);
            data.Tracks[index] = track;
            EditorUtility.SetDirty(data);
        }

        // [08_presentation.md] 5-4 追補(2026-09-14) — 「共通設定」の尺 0 警告にある
        // 「トラックの最後に合わせる」ボタン。TotalDuration を PresentationTimelineRange.SuggestedTotalDuration
        // (各トラックの終了時刻。分かる場合はアセットの長さを加味、分からなければ AutoMargin)に設定する。
        public static void FitTotalDurationToTracks(PresentationData data)
        {
            if (data == null)
            {
                return;
            }

            Undo.RecordObject(data, "Fit Presentation TotalDuration To Tracks");
            data.TotalDuration = PresentationTimelineRange.SuggestedTotalDuration(data);
            EditorUtility.SetDirty(data);
        }
    }
}
