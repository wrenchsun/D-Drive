using DDrive.Runtime.Anim2D;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Anim2D
{
    // [05_model_animation.md] C-5 — Anim2DData の方向 Clip(4 / 8 方向)へ同じリタイミングをまとめて適用する(2026-09-11)。
    // 主 Clip と同じ枚数の Sprite キーを持つ Clip だけを対象にし、それ以外はスキップして件数を返す。Undo 対応。
    // 2026-09-11 レビュー対応: 潰れたリタイミングカーブでは 1 本も書き換えない(警告 + no-op)。保存(SaveAssets)は呼び出し側で 1 回。
    public static class Anim2DRetiming
    {
        // 戻り値: 適用した Clip 数。主 Clip(data.Clip)自身は対象外(呼び出し側が先に適用している前提)。
        public static int ApplyToDirectionClips(Anim2DData data, PlacementMode mode, float totalSeconds, int expectedFrames, out int skipped)
        {
            skipped = 0;
            if (data == null || data.DirectionClips == null)
            {
                return 0;
            }

            // 時刻配列は枚数だけで決まる(対象はすべて expectedFrames 枚)。潰れたカーブなら 1 本も書き換えない(警告 + no-op)。
            if (expectedFrames <= 0 || !AnimationClipEditorUtility.TryBuildTimes(expectedFrames, mode, data.Retiming, out var times))
            {
                return 0;
            }

            var applied = 0;
            foreach (var clip in data.DirectionClips)
            {
                if (clip == null || clip == data.Clip)
                {
                    continue;
                }

                if (!AnimationClipEditorUtility.LoadSprites(clip, out var sprites, out _) || sprites == null || sprites.Length != expectedFrames)
                {
                    skipped++;
                    continue;
                }

                Undo.RecordObject(clip, "Anim2D Retiming (Direction)");
                if (AnimationClipEditorUtility.RebuildClip(clip, sprites, times, totalSeconds))
                {
                    applied++;
                }
                else
                {
                    skipped++;
                }
            }

            return applied;
        }
    }
}
