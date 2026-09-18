using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Cutscene.Tracks;
using UnityEngine.Timeline;

namespace DDrive.Runtime.Cutscene
{
    // [26_timeline.md] §4.1/§4.2/§4.7 — 6-10a の範囲で判定できる基本チェックのみ。fps 検査 6 種・
    // Humanoid/Avatar 不整合・Presentation⇄Cutscene 循環参照・Cosmetic+Simulated 参照等は 6-10d の
    // CutsceneDataValidator 拡張で追加する([11_tasks.md] 6-10d)。
    // 6-10b で「標準 Audio/Control/Signal トラック・カメラへの標準 Animation トラック使用は Warning」
    // ([26] §6)を追加した(静的な型検査だけで判定できるため 6-10d を待たずに実装)。
    public sealed class CutsceneDataValidator : IValidator
    {
        public AssetType Target => AssetType.Cutscene;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not CutsceneData cutscene)
            {
                yield break;
            }

            if (cutscene.Timeline == null)
            {
                yield return ValidationResult.Warning("Timeline(TimelineAsset)が未設定です。Cutscene.Play() は即完了します");
            }

            if (cutscene.FrameRate <= 0f)
            {
                yield return ValidationResult.Warning($"FrameRate({cutscene.FrameRate})が 0 以下です");
            }

            if (cutscene.SourceFrameRange.Start != 0 && cutscene.SourceFrameRange.End != 0 && cutscene.SourceFrameRange.End < cutscene.SourceFrameRange.Start)
            {
                yield return ValidationResult.Error($"SourceFrameRange.End({cutscene.SourceFrameRange.End}) が Start({cutscene.SourceFrameRange.Start}) より小さいです");
            }

            if (cutscene.Origin == CutsceneOrigin.AnchorPoint && string.IsNullOrEmpty(cutscene.OriginAnchorName))
            {
                yield return ValidationResult.Error("Origin=AnchorPoint ですが OriginAnchorName が空です");
            }

            if (cutscene.Skip == CutsceneSkip.ToMarker)
            {
                if (string.IsNullOrEmpty(cutscene.SkipToMarkerKey))
                {
                    yield return ValidationResult.Warning("Skip=ToMarker ですが SkipToMarkerKey が空です(マーカーが見つからない場合と同じく Immediate〔末尾〕にフォールバックします)");
                }
                else if (cutscene.Timeline != null && !HasSignalMarker(cutscene.Timeline, cutscene.SkipToMarkerKey))
                {
                    yield return ValidationResult.Warning($"Skip=ToMarker の SkipToMarkerKey '{cutscene.SkipToMarkerKey}' に一致する D-Drive Signal マーカーが Timeline に見つかりません(Immediate〔末尾〕にフォールバックします)");
                }
            }

            var bindings = cutscene.Bindings;
            if (bindings != null)
            {
                var seenNames = new HashSet<string>();
                for (var i = 0; i < bindings.Length; i++)
                {
                    var binding = bindings[i];

                    if (string.IsNullOrEmpty(binding.TrackName))
                    {
                        yield return ValidationResult.Error($"Bindings[{i}] の TrackName が空です");
                    }
                    else if (!seenNames.Add(binding.TrackName))
                    {
                        yield return ValidationResult.Warning($"Bindings[{i}] の TrackName '{binding.TrackName}' が重複しています");
                    }

                    if (binding.Target == CutsceneBindTarget.SpawnModel && !binding.Model.IsValid)
                    {
                        yield return ValidationResult.Error($"Bindings[{i}]({binding.TrackName}) は Target=SpawnModel ですが Model が未設定です");
                    }

                    if ((binding.Target == CutsceneBindTarget.SceneObjectByName || binding.Target == CutsceneBindTarget.AnchorPoint)
                        && string.IsNullOrEmpty(binding.SceneObjectName))
                    {
                        yield return ValidationResult.Error($"Bindings[{i}]({binding.TrackName}) は Target={binding.Target} ですが SceneObjectName が空です");
                    }
                }
            }

            // [14_networking.md] §5 / [26] §4.7 — Presentation と同じ方針(PredictLocal は Cosmetic 限定、
            // Simulated には Cutscene 固有の意味付けが無い)。
            if (cutscene.PredictLocal && cutscene.Flags.Net != NetMode.Cosmetic)
            {
                yield return ValidationResult.Info("PredictLocal=true ですが Flags.Net が Cosmetic ではないため無効です(常にローカル再生のみ行われます)");
            }

            if (cutscene.Flags.Net == NetMode.Simulated)
            {
                yield return ValidationResult.Info("Cutscene の Flags.Net=Simulated は未対応です(Local または Cosmetic を使ってください)");
            }

            if (cutscene.Timeline != null)
            {
                foreach (var result in ValidateStandardTrackUsage(cutscene))
                {
                    yield return result;
                }
            }
        }

        private static bool HasSignalMarker(TimelineAsset timeline, string key)
        {
            foreach (var track in timeline.GetOutputTracks())
            {
                if (track == null)
                {
                    continue;
                }

                foreach (var marker in track.GetMarkers())
                {
                    if (marker is CutsceneSignalNotification signal && signal.Key == key)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // [26_timeline.md] §6(6-10b) — 標準 Audio/Control/Signal トラックは D-Drive の禁止 API
        // (AudioSource の Play や Instantiate の直呼び)を内部で使うため、D-Drive トラックへの置き換えを促す。
        // カメラへの標準 Animation トラック使用も同様(D-Drive Camera クリップを使ってください)。
        // いずれも静的な型検査のみで判定できるため 6-10d を待たずに実装した。
        private static IEnumerable<ValidationResult> ValidateStandardTrackUsage(CutsceneData cutscene)
        {
            var cameraTrackNames = new HashSet<string>();
            if (cutscene.Bindings != null)
            {
                for (var i = 0; i < cutscene.Bindings.Length; i++)
                {
                    if (cutscene.Bindings[i].Target == CutsceneBindTarget.MainCamera && !string.IsNullOrEmpty(cutscene.Bindings[i].TrackName))
                    {
                        cameraTrackNames.Add(cutscene.Bindings[i].TrackName);
                    }
                }
            }

            foreach (var track in cutscene.Timeline.GetOutputTracks())
            {
                if (track == null)
                {
                    continue;
                }

                switch (track)
                {
                    case AudioTrack:
                        yield return ValidationResult.Warning($"標準 Audio トラック('{track.name}')は使わないでください。D-Drive の SE クリップ(CutsceneSeTrack)を使ってください([26_timeline.md] §4.3)");
                        break;

                    case ControlTrack:
                        yield return ValidationResult.Warning($"標準 Control トラック('{track.name}')は使わないでください。D-Drive の VFX/AnchorGroup クリップを使ってください([26_timeline.md] §4.3)");
                        break;

                    case SignalTrack:
                        yield return ValidationResult.Warning($"標準 Signal トラック('{track.name}')は使わないでください。D-Drive の Event/Signal マーカーを使ってください([26_timeline.md] §4.3)");
                        break;

                    case AnimationTrack when cameraTrackNames.Contains(track.name):
                        yield return ValidationResult.Warning($"カメラ役割('{track.name}')に標準 Animation トラックがバインドされています。D-Drive Camera クリップ(CutsceneCameraTrack)を使ってください([26_timeline.md] §4.3/§4.6)");
                        break;
                }
            }
        }
    }
}
