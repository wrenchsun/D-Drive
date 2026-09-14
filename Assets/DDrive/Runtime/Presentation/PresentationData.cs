using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEngine;

namespace DDrive.Runtime.Presentation
{
    public readonly struct PresentationMarker
    {
    }

    // [08_presentation.md] §2 — 「剣攻撃」等の複数アセットの束を 1 データにまとめる。
    // Presentation 自身は再生せず、Tracks を各 Manager へ委譲するだけの薄いオーケストレータ([01] §8)。
    [CreateAssetMenu(menuName = "D-Drive/Presentation/Presentation Data", fileName = "PRES_NewPresentation")]
    [AssetIdDefinition(AssetType.Presentation, typeof(PresentationMarker), "PRESENTID")]
    public class PresentationData : AssetDataBase
    {
        [Header("Tracks")]
        [Tooltip("演出を構成する各トラック(Anim/SE/VFX/HitStop/...)。")]
        public PresentationTrack[] Tracks;

        [Tooltip("再生の総尺(秒)。0 の場合は Tracks(Trigger=AtTime のみ)の最大 Time から自動算出する。")]
        public float TotalDuration;

        [Tooltip("true のときだけ Cancel() で中断できる。false の Cancel() は警告 1 回のうえ no-op。")]
        public bool Interruptible = true;

        [Tooltip("Flags.Net=Cosmetic のときのみ有効。true = 行為者は Host 確定の Broadcast を待たず即ローカル再生する" +
                 "(予測再生。[14_networking.md] §5)。false(既定) = SE/VFX 等の既存 Cosmetic と同じく、自分の Broadcast を" +
                 "受信して初めて再生する。")]
        public bool PredictLocal;
    }
}
