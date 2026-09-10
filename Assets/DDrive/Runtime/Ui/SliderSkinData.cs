using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using UnityEngine;
using SeId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Audio.SeMarker>;

namespace DDrive.Runtime.Ui
{
    // [18_ui_controls.md] B-2 — UiSlider 用 Skin。状態別ビジュアルは基底(ControlSkinData)、
    // スライダー固有のパーツ(Track/Fill/Handle/DelayFill)と操作 SE をここに追加する。
    // ConstantsClassName は ButtonSkinData の "SKINID" と衝突しないよう "SLIDERSKINID" にする
    // (AssetIdGenerator は ConstantsClassName ごとに別の static class を生成するため。[15] 実装メモ参照)。
    [CreateAssetMenu(menuName = "D-Drive/Ui/Slider Skin", fileName = "SKIN_New")]
    [AssetIdDefinition(AssetType.ControlSkin, typeof(ControlSkinMarker), "SLIDERSKINID")]
    public class SliderSkinData : ControlSkinData
    {
        [Header("パーツ")]
        public StateVisual Track;
        public StateVisual Fill;
        public StateVisual Handle;
        public StateVisual DelayFill;
        [Tooltip("ノッチ位置に並べる装飾スプライト(任意)")]
        public Sprite NotchSprite;
        [Tooltip("パッド操作時はハンドルを隠し、フォーカス枠のみ表示する")]
        public bool HideHandleOnGamepad;
        [Tooltip("タッチ用ヒット領域拡張(ハンドル外を押しても掴める)")]
        public Vector2 ExtraHitPadding;

        [Header("SE")]
        public SeId GrabSe;
        public SeId ReleaseSe;
        public SeId NotchSe;
        public SeId LimitSe;
        public SeId DeniedSe;
        [Tooltip("高速ドラッグ時に NotchSe が詰まらないようにする最小間隔(秒)")]
        public float NotchSeMinIntervalSec = 0.04f;

        [Header("触覚")]
        [Tooltip("[16] Part B で HapticId に置換")]
        public ulong NotchHapticId;
        [Tooltip("[16] Part B で HapticId に置換")]
        public ulong LimitHapticId;
    }
}
