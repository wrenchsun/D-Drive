using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using UnityEngine;

namespace DDrive.Runtime.Ui
{
    public readonly struct ControlSkinMarker
    {
    }

    // [15_ui_interaction.md] A-3 / [18_ui_controls.md] A-1 — 状態 1 つ分の見た目。
    // EnterTween/EnterPreset は 2026-09-11(4-8+4-11 前半)で追加。UiInteractable.ApplyVisual が状態遷移時に
    // EnterTween(あれば優先) → EnterPreset(Preset!=None なら)の順で UiFx 経由で再生する。
    [Serializable]
    public struct StateVisual
    {
        [Tooltip("この状態に入ったときに再生する Tween(UiTweenData)。設定があれば EnterPreset より優先")]
        public AssetId<UiTweenMarker> EnterTween;
        [Tooltip("EnterTween が未設定のときに使う簡易プリセット指定。Preset=None なら何も再生しない")]
        public UiPresetRef EnterPreset;
        public Color Tint;
        public ValueDef Scale;
        public Sprite OverrideSprite;

        // 2026-09-14 追加(ユーザー要望: ボタン・スライダーの画像にスプライトアニメ / スクロールアニメ)。
        [Tooltip("この状態のあいだ順に切り替えるコマ画像(スプライトアニメ)。空なら無効。Override Sprite より優先")]
        public Sprite[] AnimFrames;
        [Tooltip("1 秒あたりのコマ数(0 以下は 12)")]
        public float AnimFps;
        [Tooltip("最後のコマまで行ったら最初に戻る(OFF なら最後のコマで止まる)")]
        public bool AnimLoop;
        [Tooltip("画像を流す速さ(UV / 秒。X=横, Y=縦。0 なら無効)。Skin の Scroll Material が必要。画像の Wrap Mode を Repeat にし、Sprite Atlas には入れないこと")]
        public Vector2 ScrollSpeed;

        // Inspector 上の既定値。Tint=白・Scale=定数1(Evaluate(1f)=1)で「未設定でも普通に表示される」を保証する。
        public static StateVisual Default => new() { Tint = Color.white, Scale = ValueDef.Constant01(1f), AnimFps = 12f, AnimLoop = true };
    }

    // [15_ui_interaction.md] A-3 / [18_ui_controls.md] A-1 — ButtonSkinData(15) /
    // SliderSkinData(18、未実装)が派生する Skin 基底。ControlState ごとの StateVisual を持ち、
    // UiInteractable.SetVisual/ApplySkinForCurrentState が Get(State) で引く。
    // 抽象型のため [AssetIdDefinition] は付けない(codegen は具象型のみを走査する。派生の
    // ButtonSkinData 側に付与する)。
    public abstract class ControlSkinData : AssetDataBase
    {
        [Header("状態別ビジュアル")]
        public StateVisual Normal = StateVisual.Default;
        public StateVisual Hover = StateVisual.Default;
        public StateVisual Pressed = StateVisual.Default;
        public StateVisual Selected = StateVisual.Default;
        public StateVisual Disabled = StateVisual.Default;
        public StateVisual Locked = StateVisual.Default;

        // 2026-09-14 追加(ユーザー要望: 当たり判定の調整)。状態ごとではなく Skin 全体の設定で、
        // UiInteractable が TargetGraphic へ適用する(Graphic.raycastPadding / Image.alphaHitTestMinimumThreshold)。
        [Header("当たり判定")]
        [Tooltip("押せる範囲を四辺ごとに広げる(+)/狭める(-)。単位は UI のピクセル(X=左, Y=下, Z=右, W=上)。Target Graphic に掛かる")]
        public Vector4 HitAreaExpand;

        [Tooltip("画像の不透明度がこの値未満のピクセルは押せない(0=無効、例 0.5)。画像(Sprite の Texture)の Read/Write を ON にし、Sprite Atlas には入れないこと")]
        [Range(0f, 1f)]
        public float AlphaHitThreshold;

        [Header("スクロール")]
        [Tooltip("スクロールアニメ用のマテリアル(DDrive/UI/Scroll シェーダー)。Scroll Speed を使う状態があるのに空だとスクロールしない。Skin Editor が既定のものを自動で入れる")]
        public UnityEngine.Material ScrollMaterial; // 名前空間 DDrive.Runtime.Material と衝突するため完全修飾

        // 実際に適用する広げ幅(派生 Skin が固有の設定を足す。SliderSkinData.ExtraHitPadding)。
        public virtual Vector4 EffectiveHitAreaExpand => HitAreaExpand;

        public ref readonly StateVisual Get(ControlState state)
        {
            switch (state)
            {
                case ControlState.Hover: return ref Hover;
                case ControlState.Pressed: return ref Pressed;
                case ControlState.Selected: return ref Selected;
                case ControlState.Disabled: return ref Disabled;
                case ControlState.Locked: return ref Locked;
                default: return ref Normal;
            }
        }
    }
}
