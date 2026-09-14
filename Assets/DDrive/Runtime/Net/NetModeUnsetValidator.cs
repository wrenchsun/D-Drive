using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Material;
using DDrive.Runtime.Prefab;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;

namespace DDrive.Runtime.Net
{
    // [14_networking.md] §10(6-6) — 「NetMode 未設定(既定値のまま大量放置)」Info(レポート)。
    // NetMode(Flags.Net、DDrive.Foundation.Net.NetMode)は全 AssetDataBase 共通のフィールドだが、
    // 意味を持つのは実際にネット経路に乗る種別だけ([14] §4 の表: Se/Bgm/Vfx/Prefab/Material/Presentation)。
    // Canvas/UI/Anim/Model/Anchor 等は「常に Local」が正しい既定であり対象外にする(誤検出防止)。
    //
    // 「大量放置」を検出する専用の集計 API は追加していない: ValidatorRegistry(Foundation/Validation)の
    // RunAll は 1 アセットにつき 1 回 Validate を呼ぶだけで、全アセット走査後にまとめて 1 件を出す
    // フックが無い(追加するには ValidatorRegistry 自体の設計変更が要る。要判断)。このため対象アセット
    // それぞれに Info を 1 件ずつ出す方針にした。Validation ウィンドウの一覧に並ぶ件数がそのまま
    // 「何個既定値のまま放置されているか」の集計になる(専用の合計数表示が欲しくなったら
    // ValidatorRegistry に集計フックを追加すること)。
    public sealed class NetModeUnsetValidator : IUniversalValidator
    {
        // IUniversalValidator のため無視される(ValidatorRegistry が全アセットに適用する)。
        public AssetType Target => AssetType.None;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (!IsNetworkAware(data) || data.Flags.Net != NetMode.Local)
            {
                yield break;
            }

            yield return ValidationResult.Info("NetMode(Flags.Net)が既定値(Local)のままです。ネットワーク経路に乗せる意図があるなら Cosmetic/Simulated を明示的に選んでください(意図的に Local のままで問題ありません。[14_networking.md] §3/§4)");
        }

        private static bool IsNetworkAware(AssetDataBase data) => data switch
        {
            SeData => true,
            BgmData => true,
            VfxData => true,
            PrefabData => true,
            MaterialData => true,
            PresentationData => true,
            _ => false,
        };
    }
}
