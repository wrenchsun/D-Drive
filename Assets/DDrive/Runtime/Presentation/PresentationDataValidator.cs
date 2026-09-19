using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Material;
using DDrive.Runtime.Prefab;
using DDrive.Runtime.Vfx;

namespace DDrive.Runtime.Presentation
{
    // [08_presentation.md] §6。
    public sealed class PresentationDataValidator : IValidator
    {
        // [14_networking.md] §10(6-6) — 「Cosmetic なのに Reliable 大容量パラメータ(Texture 等)をイベント
        // 送信」の検査しきい値。docs に具体的なバイト数の定めが無いため、ここで定数として置く
        // (docs/14 §10 実装メモに転記済み)。Object(Texture/Mesh 等の UnityEngine.Object 参照。実サイズが
        // 数百 KB〜数 MB になりうる)は件数を問わず常に対象、Curve/Gradient はキー数がこの値を超えたら対象
        // (数キー程度なら数十バイトで実害が薄いと判断した)。
        public const int LargeParamKeyCountWarnThreshold = 8;

        public AssetType Target => AssetType.Presentation;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not PresentationData presentation)
            {
                yield break;
            }

            var tracks = presentation.Tracks;
            if (tracks == null)
            {
                yield break;
            }

            for (var i = 0; i < tracks.Length; i++)
            {
                var track = tracks[i];

                if (RequiresAsset(track.Kind) && !track.Asset.IsAssigned)
                {
                    yield return ValidationResult.Error($"トラック {i}({track.Kind}) の Asset が未設定です");
                }

                // [22_anchor_group.md] §5(Presentation 統合) — Kind=AnchorGroup なのに Asset の種別が
                // AnchorGroup ではない(別種別のアセットを取り違えて割り当てた)場合を検出する。他 Kind は
                // D&D(PresentationTrackKindMapping)が Kind と Asset.Type を同時に書き込むため通常ズレないが、
                // AnchorGroup は追加時のみこの検査で明示的に守る。
                if (track.Kind == TrackKind.AnchorGroup && track.Asset.IsAssigned && track.Asset.Type != AssetType.AnchorGroup)
                {
                    yield return ValidationResult.Error($"トラック {i}({track.Kind}) の Asset の種別が AnchorGroup ではありません({track.Asset.Type})");
                }

                if (track.Trigger == TrackTrigger.OnSignal && string.IsNullOrEmpty(track.SignalKey))
                {
                    yield return ValidationResult.Error($"トラック {i}({track.Kind}) は Trigger=OnSignal ですが SignalKey が空です");
                }

                if ((track.Kind == TrackKind.Marker || track.Kind == TrackKind.Signal) && string.IsNullOrEmpty(track.SignalKey))
                {
                    yield return ValidationResult.Error($"トラック {i}({track.Kind}) の名前(SignalKey)が空です");
                }

                if (track.Trigger == TrackTrigger.AtTime && presentation.TotalDuration > 0f && track.Time > presentation.TotalDuration)
                {
                    yield return ValidationResult.Warning($"トラック {i}({track.Kind}) の Time({track.Time:0.###}s) が TotalDuration({presentation.TotalDuration:0.###}s) を超過しています");
                }

                // 循環参照(自身を含む): 現行の TrackKind には「他 Presentation を再生する」種別が無いため、
                // Asset が Presentation 種別を指している(将来の拡張)場合のみ検出する。
                if (track.Asset.Type == AssetType.Presentation && track.Asset.Id == presentation.Id && presentation.Id != 0)
                {
                    yield return ValidationResult.Error($"トラック {i} が自身({presentation.DisplayName})を参照する循環になっています");
                }

                // [14_networking.md] §10(6-6) — 「Presentation 内に Simulated トラックと PredictLocal の
                // 競合」Error。PredictLocal=true は行為者クライアントが Broadcast を待たずローカルで即時
                // 再生する予測再生([14] §5)。参照先アセット(Vfx/Se/Prefab 等)自体が NetMode=Simulated
                // (サーバー権威の生成、[14] §3)の場合、クライアントが予測でローカル生成してしまうのは
                // 権威モデルと矛盾する。
                if (presentation.PredictLocal && track.Asset.IsAssigned && TryFindTrackAssetNetMode(ctx, track.Asset, out var trackNetMode) && trackNetMode == NetMode.Simulated)
                {
                    yield return ValidationResult.Error($"トラック {i}({track.Kind}) の参照先アセットは Flags.Net=Simulated ですが、この Presentation は PredictLocal=true です(クライアントの予測再生がサーバー権威の生成と競合します)");
                }

                // [14_networking.md] §10(6-6) — 「Cosmetic なのに Reliable 大容量パラメータ(Texture 等)を
                // イベント送信」Warning。Params は現状どの Manager もネットワーク越しに同期しない
                // ([14] §4 実装メモ「paramOverrides の同期も未実装」)。Cosmetic な Presentation で大容量
                // Params を上書きしていても、各クライアントはローカルのデフォルト値で再生するため見た目が
                // 食い違う(将来 Params 同期を実装する場合は、そのまま送ると帯域を圧迫する)。
                if (presentation.Flags.Net == NetMode.Cosmetic && track.Params != null)
                {
                    for (var p = 0; p < track.Params.Length; p++)
                    {
                        if (IsLargeParam(in track.Params[p]))
                        {
                            yield return ValidationResult.Warning($"トラック {i}({track.Kind}) の Params[{p}]({track.Params[p].Type}) は大容量パラメータです。Cosmetic 配送では Params は同期されないため、各クライアントの見た目が食い違う可能性があります");
                        }
                    }
                }
            }

            if (!presentation.Interruptible && PresentationTiming.EffectiveDuration(presentation) > 10f)
            {
                yield return ValidationResult.Warning("Interruptible=false ですが尺が 10 秒を超えています(中断できない長尺演出)");
            }

            // [14_networking.md] §10(5-8/5-9) — PredictLocal は Flags.Net=Cosmetic のときのみ意味を持つ。
            if (presentation.PredictLocal && presentation.Flags.Net != NetMode.Cosmetic)
            {
                yield return ValidationResult.Info("PredictLocal=true ですが Flags.Net が Cosmetic ではないため無効です(常にローカル再生のみ行われます)");
            }

            // Presentation 自体に Simulated の意味付けは無い(§5 参照。ネットは Local/Cosmetic の 2 値運用)。
            if (presentation.Flags.Net == NetMode.Simulated)
            {
                yield return ValidationResult.Info("Presentation の Flags.Net=Simulated は未対応です(Cosmetic として Host 権威の生成は行われません。Local または Cosmetic を使ってください)");
            }
        }

        // internal ではなく public: PresentationEditorWindow(5-4)が「Asset 未設定」の Kind をトラック追加時の
        // 初期値判定や D&D の可否判定に再利用する(同じ判定をエディタ側に複製しない)。
        public static bool RequiresAsset(TrackKind kind) => kind switch
        {
            TrackKind.Marker => false,
            TrackKind.Signal => false,
            TrackKind.HitStop => false,
            _ => true,
        };

        // [14_networking.md] §10(6-6) — AnchorDataValidator.FindAnchor と同じ慣習(ctx.AllAssets の
        // 単純な線形走査、LINQ 不使用)。track が参照する先のアセットが見つかれば、その Flags.Net を返す。
        // 種別を問わず Id だけで一致させる(D-Drive の AssetId は種別ごとに独立した値域を持つ想定だが、
        // AssetRef.Type も一致させて誤爆を避ける)。
        private static bool TryFindTrackAssetNetMode(ValidationContext ctx, in AssetRef assetRef, out NetMode netMode)
        {
            netMode = NetMode.Local;
            var all = ctx.AllAssets;
            for (var i = 0; i < all.Count; i++)
            {
                var candidate = all[i];
                if (candidate != null && candidate.Id == assetRef.Id && AssetTypeOf(candidate) == assetRef.Type)
                {
                    netMode = candidate.Flags.Net;
                    return true;
                }
            }

            return false;
        }

        private static AssetType AssetTypeOf(AssetDataBase data) => data switch
        {
            SeData => AssetType.Se,
            BgmData => AssetType.Bgm,
            VfxData => AssetType.Vfx,
            PrefabData => AssetType.Prefab,
            MaterialData => AssetType.Material,
            PresentationData => AssetType.Presentation,
            _ => AssetType.None,
        };

        // [14_networking.md] §10(6-6) — Object(Texture/Mesh 等)は件数を問わず常に「大容量の疑いあり」、
        // Curve/Gradient はキー数が LargeParamKeyCountWarnThreshold を超えたときだけ対象にする。
        private static bool IsLargeParam(in ParamValue param) => param.Type switch
        {
            ParamValueType.Object => param.ObjectValue != null,
            ParamValueType.Curve => param.CurveValue != null && param.CurveValue.length > LargeParamKeyCountWarnThreshold,
            ParamValueType.Gradient => param.GradientValue != null &&
                param.GradientValue.colorKeys.Length + param.GradientValue.alphaKeys.Length > LargeParamKeyCountWarnThreshold,
            _ => false,
        };
    }
}
