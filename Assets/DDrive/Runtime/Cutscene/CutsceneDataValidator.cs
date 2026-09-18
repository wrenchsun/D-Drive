using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Cutscene.Tracks;
using DDrive.Runtime.Model;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Ui;
using DDrive.Runtime.Vfx;
using UnityEngine.Timeline;

namespace DDrive.Runtime.Cutscene
{
    // [26_timeline.md] §4.1/§4.2/§4.7 — 6-10a の範囲で判定できる基本チェックのみ。
    // 6-10b で「標準 Audio/Control/Signal トラック・カメラへの標準 Animation トラック使用は Warning」
    // ([26] §6)を追加した(静的な型検査だけで判定できるため 6-10d を待たずに実装)。
    // 6-10d(2026-09-18) — Humanoid/Avatar 不整合・Presentation⇄Cutscene 循環参照(Error)・
    // Cosmetic+Simulated 参照(Info)・OnSignal 付き Presentation クリップ(Warning)を追加した
    // ([11_tasks.md] 6-10d)。fps 検査 6 種(§5.3)は CutsceneImportProfile(Editor asmdef)への参照が
    // 要るため、この Runtime Validator ではなく Editor 側の `CutsceneFpsValidator`
    // (`Assets/DDrive/Editor/Cutscene/CutsceneFpsValidator.cs`)に実装した。
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

            // 6-10d([26_timeline.md] §5.4) — Humanoid クリップを持つ Animation トラックの Binding が
            // Humanoid Avatar の無い ModelData を指している。
            foreach (var result in ValidateHumanoidAvatarMismatch(cutscene, ctx))
            {
                yield return result;
            }

            // 6-10d([26_timeline.md] §6) — Timeline が参照する各アセット(SE/VFX/AnchorGroup/UI/
            // Presentation/SpawnModel)を 1 回だけ走査し、Cosmetic+Simulated 参照(Info)・
            // OnSignal 付き Presentation クリップ(Warning)・Presentation⇄Cutscene 循環参照(Error)を検出する。
            foreach (var result in ValidateReferencedAssets(cutscene, ctx))
            {
                yield return result;
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

        // [26_timeline.md] §5.4(6-10d) — Humanoid クリップ(AnimationClip.humanMotion)を持つ Animation
        // トラックの Binding(Target=SpawnModel)が、Humanoid Avatar の無い ModelData を指している場合の
        // 検査。取り込み時(6-10c)は Avatar 未設定でも CreateFromThisModel で仮取り込みするため Error に
        // せず Warning とする(ModelData 自体の Avatar 未設定は既存の ModelDataValidator の Error 対象)。
        private static IEnumerable<ValidationResult> ValidateHumanoidAvatarMismatch(CutsceneData cutscene, ValidationContext ctx)
        {
            if (cutscene.Bindings == null || cutscene.Timeline == null)
            {
                yield break;
            }

            for (var i = 0; i < cutscene.Bindings.Length; i++)
            {
                var binding = cutscene.Bindings[i];
                if (binding.Target != CutsceneBindTarget.SpawnModel || !binding.Model.IsValid || string.IsNullOrEmpty(binding.TrackName))
                {
                    continue;
                }

                if (!TrackHasHumanoidClip(cutscene.Timeline, binding.TrackName))
                {
                    continue;
                }

                var model = FindAsset<ModelData>(ctx, AssetType.Model, binding.Model.Value);
                if (model == null)
                {
                    continue; // 未発見は参照切れの検査対象(他 Validator)。ここでは判定不能として黙る。
                }

                if (model.Avatar == null || !model.Avatar.isValid || !model.Avatar.isHuman)
                {
                    yield return ValidationResult.Warning($"Bindings[{i}]({binding.TrackName}) の Animation トラックは Humanoid クリップですが、ModelData '{model.DisplayName}' に Humanoid Avatar が設定されていません([26_timeline.md] §5.4)。");
                }
            }
        }

        private static bool TrackHasHumanoidClip(TimelineAsset timeline, string trackName)
        {
            foreach (var track in timeline.GetOutputTracks())
            {
                if (track == null || track is not AnimationTrack || track.name != trackName)
                {
                    continue;
                }

                foreach (var clip in track.GetClips())
                {
                    if (clip.asset is AnimationPlayableAsset animAsset && animAsset.clip != null && animAsset.clip.humanMotion)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // [26_timeline.md] §6/§4.7/§3.1(6-10d) — Timeline の各クリップが参照するアセットを 1 回の走査で
        // まとめて検査する。SE/VFX/AnchorGroup/UI クリップと Bindings(SpawnModel)は Cosmetic+Simulated
        // 参照(Info)のみ、Presentation クリップは Cosmetic+Simulated 参照(Info)・OnSignal トラックを
        // 持つ Presentation の使用(Warning)・Presentation 経由で自身に戻る循環参照(Error)の 3 つを検査する。
        private static IEnumerable<ValidationResult> ValidateReferencedAssets(CutsceneData cutscene, ValidationContext ctx)
        {
            if (cutscene.Bindings != null)
            {
                for (var i = 0; i < cutscene.Bindings.Length; i++)
                {
                    var binding = cutscene.Bindings[i];
                    if (binding.Target == CutsceneBindTarget.SpawnModel && binding.Model.IsValid)
                    {
                        foreach (var result in CheckCosmeticSimulatedReference(cutscene, ctx, AssetType.Model, binding.Model.Value, binding.TrackName))
                        {
                            yield return result;
                        }
                    }
                }
            }

            if (cutscene.Timeline == null)
            {
                yield break;
            }

            foreach (var track in cutscene.Timeline.GetOutputTracks())
            {
                if (track == null)
                {
                    continue;
                }

                foreach (var clip in track.GetClips())
                {
                    switch (clip.asset)
                    {
                        case CutsceneSeClip se when se.SeId.IsValid:
                            foreach (var result in CheckCosmeticSimulatedReference(cutscene, ctx, AssetType.Se, se.SeId.Value, track.name))
                            {
                                yield return result;
                            }
                            break;

                        case CutsceneVfxClip vfx when vfx.VfxId.IsValid:
                            foreach (var result in CheckCosmeticSimulatedReference(cutscene, ctx, AssetType.Vfx, vfx.VfxId.Value, track.name))
                            {
                                yield return result;
                            }
                            break;

                        case CutsceneAnchorGroupClip group when group.GroupId.IsValid:
                            foreach (var result in CheckCosmeticSimulatedReference(cutscene, ctx, AssetType.AnchorGroup, group.GroupId.Value, track.name))
                            {
                                yield return result;
                            }
                            break;

                        case CutsceneUiClip ui when ui.CanvasId.IsValid:
                            foreach (var result in CheckCosmeticSimulatedReference(cutscene, ctx, AssetType.Canvas, ui.CanvasId.Value, track.name))
                            {
                                yield return result;
                            }
                            break;

                        case CutscenePresentationClip pres when pres.PresentationId.IsValid:
                            foreach (var result in CheckCosmeticSimulatedReference(cutscene, ctx, AssetType.Presentation, pres.PresentationId.Value, track.name))
                            {
                                yield return result;
                            }

                            var presentation = FindAsset<PresentationData>(ctx, AssetType.Presentation, pres.PresentationId.Value);
                            if (presentation != null)
                            {
                                if (HasOnSignalTrack(presentation))
                                {
                                    yield return ValidationResult.Warning($"トラック '{track.name}' が参照する Presentation '{presentation.DisplayName}' は OnSignal トラックを持ちますが、Cutscene には Signal を送る手段がありません([26_timeline.md] §3.1/§4.5)。ゲーム結果を待つ演出には Presentation を親にしてください。");
                                }

                                if (cutscene.Id != 0 && HasCycleBackToCutscene(presentation, cutscene.Id, ctx, new HashSet<(int, ulong)>()))
                                {
                                    yield return ValidationResult.Error($"トラック '{track.name}' の Presentation '{presentation.DisplayName}' 経由で、この Cutscene 自身({cutscene.DisplayName})に戻る循環参照があります([26_timeline.md] §6)。");
                                }
                            }
                            break;
                    }
                }
            }
        }

        // [14_networking.md] §10(6-6) / [26_timeline.md] §4.7 — PresentationDataValidator の
        // PredictLocal×Simulated 検査と同じ考え方(Cosmetic な演出がクライアントごとに直接ローカル生成する
        // 参照先が、サーバー権威の生成〔Simulated〕と食い違う)。Cutscene には PredictLocal と同種の
        // 意味を持つフィールドが無いため、Presentation の Error より一段軽い Info とした([11_tasks.md] 6-10d)。
        private static IEnumerable<ValidationResult> CheckCosmeticSimulatedReference(CutsceneData cutscene, ValidationContext ctx, AssetType type, ulong id, string trackName)
        {
            if (cutscene.Flags.Net != NetMode.Cosmetic)
            {
                yield break;
            }

            if (!TryFindNetMode(ctx, type, id, out var netMode) || netMode != NetMode.Simulated)
            {
                yield break;
            }

            yield return ValidationResult.Info($"トラック '{trackName}' が参照する {type} アセット(0x{id:X})は Flags.Net=Simulated です。この Cutscene は Cosmetic(同期再生)のため、各クライアントがローカルで直接生成することになり、サーバー権威の生成と食い違う可能性があります([26_timeline.md] §4.7)。");
        }

        private static bool HasOnSignalTrack(PresentationData presentation)
        {
            var tracks = presentation.Tracks;
            if (tracks == null)
            {
                return false;
            }

            for (var i = 0; i < tracks.Length; i++)
            {
                if (tracks[i].Trigger == TrackTrigger.OnSignal)
                {
                    return true;
                }
            }

            return false;
        }

        // Presentation(トラック Kind=Timeline)→ Cutscene → Presentation … と辿り、originCutsceneId に
        // 戻ってくるか(visited は (種別マーカー, Id) の組。0=Presentation, 1=Cutscene)。
        private static bool HasCycleBackToCutscene(PresentationData presentation, ulong originCutsceneId, ValidationContext ctx, HashSet<(int, ulong)> visited)
        {
            if (!visited.Add((0, presentation.Id)))
            {
                return false;
            }

            var tracks = presentation.Tracks;
            if (tracks == null)
            {
                return false;
            }

            for (var i = 0; i < tracks.Length; i++)
            {
                var track = tracks[i];
                if (track.Kind != TrackKind.Timeline || !track.Asset.IsAssigned)
                {
                    continue;
                }

                if (track.Asset.Id == originCutsceneId)
                {
                    return true;
                }

                var nextCutscene = FindAsset<CutsceneData>(ctx, AssetType.Cutscene, track.Asset.Id);
                if (nextCutscene != null && CutsceneReachesCycle(nextCutscene, originCutsceneId, ctx, visited))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CutsceneReachesCycle(CutsceneData cutscene, ulong originCutsceneId, ValidationContext ctx, HashSet<(int, ulong)> visited)
        {
            if (!visited.Add((1, cutscene.Id)) || cutscene.Timeline == null)
            {
                return false;
            }

            foreach (var track in cutscene.Timeline.GetOutputTracks())
            {
                if (track == null)
                {
                    continue;
                }

                foreach (var clip in track.GetClips())
                {
                    if (clip.asset is CutscenePresentationClip p && p.PresentationId.IsValid)
                    {
                        var presentation = FindAsset<PresentationData>(ctx, AssetType.Presentation, p.PresentationId.Value);
                        if (presentation != null && HasCycleBackToCutscene(presentation, originCutsceneId, ctx, visited))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool TryFindNetMode(ValidationContext ctx, AssetType type, ulong id, out NetMode netMode)
        {
            netMode = NetMode.Local;
            var all = ctx.AllAssets;
            for (var i = 0; i < all.Count; i++)
            {
                var candidate = all[i];
                if (candidate != null && candidate.Id == id && AssetTypeOf(candidate) == type)
                {
                    netMode = candidate.Flags.Net;
                    return true;
                }
            }

            return false;
        }

        private static T FindAsset<T>(ValidationContext ctx, AssetType type, ulong id) where T : AssetDataBase
        {
            var all = ctx.AllAssets;
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] is T typed && typed.Id == id && AssetTypeOf(typed) == type)
                {
                    return typed;
                }
            }

            return null;
        }

        private static AssetType AssetTypeOf(AssetDataBase data) => data switch
        {
            SeData => AssetType.Se,
            VfxData => AssetType.Vfx,
            AnchorGroupData => AssetType.AnchorGroup,
            CanvasData => AssetType.Canvas,
            ModelData => AssetType.Model,
            PresentationData => AssetType.Presentation,
            CutsceneData => AssetType.Cutscene,
            _ => AssetType.None,
        };
    }
}
