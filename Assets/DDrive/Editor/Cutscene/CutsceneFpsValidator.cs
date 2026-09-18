using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace DDrive.Editor.Cutscene
{
    // [26_timeline.md] §5.3(6-10d) — fps 検査 6 種。CutsceneImportProfile(Editor asmdef の設定 SO)を
    // 参照するため、Runtime の CutsceneDataValidator ではなくこの Editor 側 Validator に実装する
    // ([11_tasks.md] 6-10d、CutsceneDataValidator の 6-10d 実装メモ参照)。
    //
    // 「FBX の fps」そのものは Data に保存されないため、次の 2 つで近似する(いずれもランタイムの
    // 公開プロパティで読める値。ModelImporter 等の Editor 専用 API は使わない):
    //   - キャラ/小物の Animation トラックが参照する AnimationClip.frameRate(取り込み時に FBX の
    //     テイクの fps がそのまま入る、§5.2)
    //   - CutsceneData.FrameRate 自体(取り込みで FBX の fps がそのまま設定される、§5.3 の表)
    public sealed class CutsceneFpsValidator : IValidator
    {
        public AssetType Target => AssetType.Cutscene;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not CutsceneData cutscene)
            {
                yield break;
            }

            var clipFrameRates = CollectAnimationClipFrameRates(cutscene.Timeline);

            // 検査1: 同じ CutsceneData を構成する FBX 群(キャラ/小物の Animation トラック)の fps が不一致。
            if (clipFrameRates.Count > 1)
            {
                yield return ValidationResult.Warning($"キャラ/小物のアニメーション間で fps が一致していません({FormatRates(clipFrameRates)})。同じショットの全ファイルで開始〜終了フレームと fps を揃えてください([26_timeline.md] §5.3/§5.1.1)。");
            }

            // 検査2: FBX の fps(近似: Animation トラックの AnimationClip.frameRate)と
            // CutsceneData.FrameRate が不一致。clipFrameRates が 1 種類にまとまっているときだけ
            // FixAction(FBX に合わせる)を付ける(複数ある場合はどちらに合わせるべきか一意に決まらない)。
            if (clipFrameRates.Count == 1)
            {
                var fbxRate = GetSingle(clipFrameRates);
                if (!Mathf.Approximately(fbxRate, cutscene.FrameRate))
                {
                    var capturedCutscene = cutscene;
                    var capturedRate = fbxRate;
                    yield return ValidationResult.Warning(
                        $"FrameRate({cutscene.FrameRate:0.###})が、取り込んだアニメーションの fps({fbxRate:0.###})と一致していません([26_timeline.md] §5.3)。",
                        () => FixFrameRate(capturedCutscene, capturedRate));
                }
            }
            else if (clipFrameRates.Count > 1 && !clipFrameRates.Contains(cutscene.FrameRate))
            {
                yield return ValidationResult.Warning($"FrameRate({cutscene.FrameRate:0.###})が、取り込んだどのアニメーションの fps({FormatRates(clipFrameRates)})とも一致していません([26_timeline.md] §5.3)。");
            }

            // 検査3: FBX の fps(近似: CutsceneData.FrameRate)とプロジェクト既定 fps が不一致。
            var defaultFrameRate = CutsceneImportProfile.FindOrDefault().DefaultFrameRate;
            if (!Mathf.Approximately(cutscene.FrameRate, defaultFrameRate))
            {
                yield return ValidationResult.Info($"FrameRate({cutscene.FrameRate:0.###})がプロジェクト既定 fps(CutsceneImportProfile.DefaultFrameRate = {defaultFrameRate:0.###})と異なります。混在自体は許容されます([26_timeline.md] §5.3)。");
            }

            // 検査6: FrameRate が 30 / 60 以外。
            if (!Mathf.Approximately(cutscene.FrameRate, 30f) && !Mathf.Approximately(cutscene.FrameRate, 60f))
            {
                yield return ValidationResult.Info($"FrameRate({cutscene.FrameRate:0.###})が 30 / 60 以外です。動作はしますが、プロジェクト方針(30/60)から外れています([26_timeline.md] §5.3)。");
            }

            if (cutscene.Timeline == null)
            {
                yield break;
            }

            // 検査4/検査5: Camera クリップの StepFps。
            foreach (var track in cutscene.Timeline.GetOutputTracks())
            {
                if (track is not CutsceneCameraTrack)
                {
                    continue;
                }

                foreach (var clip in track.GetClips())
                {
                    if (clip.asset is not CutsceneCameraClip camClip || camClip.StepFps <= 0f)
                    {
                        continue;
                    }

                    if (camClip.StepFps > cutscene.FrameRate)
                    {
                        yield return ValidationResult.Warning($"トラック '{track.name}' の Camera クリップの StepFps({camClip.StepFps:0.###})が FrameRate({cutscene.FrameRate:0.###})を超えています。元より細かくはできません([26_timeline.md] §5.3)。");
                    }
                    else if (!IsIntegerMultiple(cutscene.FrameRate, camClip.StepFps))
                    {
                        yield return ValidationResult.Warning($"トラック '{track.name}' の Camera クリップの StepFps({camClip.StepFps:0.###})が FrameRate({cutscene.FrameRate:0.###})の整数倍ではありません。保持フレーム数が不均一になります([26_timeline.md] §5.3)。");
                    }
                }
            }
        }

        // AnimationTrack(キャラ/小物)が参照する AnimationClip.frameRate の重複無し集合。
        // カメラクリップ(CutsceneCameraClip)は AnimationClip を持たないため対象外。
        private static HashSet<float> CollectAnimationClipFrameRates(TimelineAsset timeline)
        {
            var rates = new HashSet<float>();
            if (timeline == null)
            {
                return rates;
            }

            foreach (var track in timeline.GetOutputTracks())
            {
                // CutsceneCameraTrack は TrackAsset 直下(AnimationTrack ではない)なので自然に除外される。
                if (track is not AnimationTrack)
                {
                    continue;
                }

                foreach (var clip in track.GetClips())
                {
                    if (clip.asset is AnimationPlayableAsset animAsset && animAsset.clip != null && animAsset.clip.frameRate > 0f)
                    {
                        rates.Add(animAsset.clip.frameRate);
                    }
                }
            }

            return rates;
        }

        private static float GetSingle(HashSet<float> set)
        {
            foreach (var v in set)
            {
                return v;
            }

            return 0f;
        }

        private static string FormatRates(HashSet<float> rates)
        {
            var parts = new List<string>();
            foreach (var r in rates)
            {
                parts.Add(r.ToString("0.###"));
            }

            parts.Sort();
            return string.Join(", ", parts);
        }

        private static bool IsIntegerMultiple(float frameRate, float stepFps)
        {
            if (stepFps <= 0f)
            {
                return true;
            }

            var ratio = frameRate / stepFps;
            return Mathf.Abs(ratio - Mathf.Round(ratio)) < 0.001f;
        }

        private static void FixFrameRate(CutsceneData cutscene, float frameRate)
        {
            Undo.RecordObject(cutscene, "Fix Cutscene FrameRate");
            cutscene.FrameRate = frameRate;
            EditorUtility.SetDirty(cutscene);
            AssetDatabase.SaveAssets();
        }
    }
}
