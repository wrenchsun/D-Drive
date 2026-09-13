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
    }
}
