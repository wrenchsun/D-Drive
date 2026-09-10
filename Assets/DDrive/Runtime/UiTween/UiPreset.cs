using System;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using DDrive.Runtime.Audio;
using UnityEngine;
using SeId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Audio.SeMarker>;

namespace DDrive.Runtime.Ui
{
    // [15_ui_interaction.md] B-3.5 — 定番演出のプリセット一覧(チケット 4-11 前半)。
    // None を先頭(0)に追加している点のみ docs の掲載順と異なる(EnterPreset 等の「未設定」を表現するため。
    // 2026-09-11 実装メモに追記済み)。それ以外は docs の並び順のまま。
    public enum UiPreset
    {
        None = 0,

        // ── 出現系 (Appear) ──
        FadeIn, SlideInLeft, SlideInRight, SlideInTop, SlideInBottom,
        ScaleIn, PopIn /*OutBack*/, BounceIn, ElasticIn, FlipInX, FlipInY,
        RotateIn, ZoomInFade, SlideFadeInLeft, SlideFadeInRight,
        SlideFadeInTop, SlideFadeInBottom, ExpandWidth, ExpandHeight, TypeFillIn,

        // ── 消滅系 (Disappear) ──
        FadeOut, SlideOutLeft, SlideOutRight, SlideOutTop, SlideOutBottom,
        ScaleOut, PopOut, BounceOut, ElasticOut, FlipOutX, FlipOutY,
        RotateOut, ZoomOutFade, SlideFadeOutLeft, SlideFadeOutRight,
        SlideFadeOutTop, SlideFadeOutBottom, CollapseWidth, CollapseHeight,

        // ── 常時系 (Idle / Loop) ──
        Pulse, Blink, Float /*上下ふわふわ*/, Sway /*左右*/, Breathe /*拡縮*/,
        RotateLoop, ShimmerAlpha, RainbowTint, WobbleLoop,

        // ── 強調系 (単発。通知・エラー・獲得演出) ──
        PunchScale, PunchRotation, Shake, ShakeHard, Flash, ColorFlash,
        HeartBeat, Jelly, Tada, RubberBand, AttentionJump,
    }

    // [15_ui_interaction.md] B-3.5 — ElementFx / StateVisual から参照する軽量指定。
    // 実体は UiPresetFactory が TweenTrack[] へ展開する(UiTweenData と同じ実行エンジンに乗る)。
    [Serializable]
    public struct UiPresetRef
    {
        public UiPreset Preset;

        [Tooltip("0 = プリセット既定")]
        public float Duration;

        [Tooltip("Slide 系の移動量、Punch/Shake 系の強さなど(意味はプリセットごとに異なる)。0 = 既定(Slide系は要素サイズから自動)")]
        public float Distance;

        [Tooltip("未指定(既定値と同一)ならプリセット既定の Ease を使う")]
        public EaseDef EaseOverride;

        [Tooltip("同時再生する SE(任意)")]
        public SeId Se;
    }
}
