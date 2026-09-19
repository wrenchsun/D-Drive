using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using UnityEngine;

namespace DDrive.Runtime.Ui
{
    public readonly struct UiTweenMarker
    {
    }

    // [15_ui_interaction.md] B-3 — Tween が動かせる対象プロパティ。
    // 2026-09-11(4-11 完了): RotationX/RotationY/ColorHue を末尾に追加。既存 .asset が無い時点での追加のため
    // enum 順序を書き換えても安全だが、将来の互換性のため既存メンバーの並びは変えず末尾追加のみで揃える。
    public enum TweenProperty
    {
        AnchoredPosition,
        Scale,
        Rotation,
        Alpha,
        Color,
        FillAmount,
        SizeDelta,
        PathMove,
        RotationX,
        RotationY,

        // RainbowTint 用。Color(RGB 直接補間)と違い、From/To.FloatValue を色相[0,1]として扱い
        // Color.HSVToRGB(hue, 1, 1) で毎フレーム変換する(直線 RGB 補間だと彩度が落ちて虹に見えないため)。
        ColorHue,
    }

    // [15_ui_interaction.md] B-3 — 開始値の決め方。OffScreen は Property=AnchoredPosition のときのみ意味を持つ
    // (それ以外は Current と同じ挙動にフォールバックする。UiTweenManager 実装メモ参照)。
    public enum TweenFromMode
    {
        Current,
        Absolute,
        Relative,
        OffScreen,
    }

    public enum OffScreenDirection
    {
        Left,
        Right,
        Top,
        Bottom,
    }

    // Foundation の SplinePath は不変クラス(コンストラクタで弧長テーブルを構築する)でシリアライズ不可のため、
    // Data 側は制御点だけを持つこの構造体を保持し、実際の SplinePath は Play() 時(Tick の外)に 1 回だけ構築する。
    [Serializable]
    public struct SplinePathDef
    {
        public SplineType Type;
        public Vector3[] Points;

        public bool IsValid => Points != null && Points.Length >= 2;
    }

    // [15_ui_interaction.md] B-3 — 1 プロパティ分の動き。UiTweenData.Tracks は並列実行される。
    [Serializable]
    public struct TweenTrack
    {
        [Tooltip("動かす対象プロパティ")]
        public TweenProperty Property;

        [Tooltip("形(Ease/Bezier/Curve) + 尺 + Loop の統一表現([17_value_definition.md])。" +
                 "Tween ではこの Evaluate の From/To は使わず、0..1 の形だけを使う(下記 FromValue/ToValue が実際の値)")]
        public ValueDef Motion;

        [Tooltip("再生開始からこの秒数だけ遅らせて始める")]
        public float Delay;

        [Tooltip("開始値の決め方。Current=今の値 / Absolute=FromValue そのまま / Relative=今の値+FromValue / OffScreen=画面外(AnchoredPosition のみ有効)")]
        public TweenFromMode From;

        [Tooltip("From=OffScreen のときの方向")]
        public OffScreenDirection OffScreen;

        [Tooltip("From=Absolute/Relative のときの開始値")]
        public ParamValue FromValue;

        [Tooltip("終了値(From=OffScreen のときは無視され、開始前の現在値に戻る想定)")]
        public ParamValue ToValue;

        [Tooltip("Property=PathMove のときの経路(RectTransform ローカル座標の制御点)")]
        public SplinePathDef Path;
    }

    // [15_ui_interaction.md] B-3 — デザイナーが作る演出単位(1 つの UiTweenData = 複数 Track の並列再生)。
    [CreateAssetMenu(menuName = "D-Drive/Ui/Ui Tween Data", fileName = "UITWEEN_New")]
    [AssetIdDefinition(AssetType.UiTween, typeof(UiTweenMarker), "UITWEENID")]
    public class UiTweenData : AssetDataBase
    {
        [Tooltip("並列実行される Track の一覧")]
        public TweenTrack[] Tracks;

        [Tooltip("0 = 自動(全 Track の Delay+尺 の最大値)。手動指定すると Validator が最長 Track とのズレを警告する")]
        public float TotalDuration;

        // 0 なら Tracks から自動算出する(無限ループ Track は 1 周期分を目安値として返す)。
        public float ResolvedDuration
        {
            get
            {
                if (TotalDuration > 0f)
                {
                    return TotalDuration;
                }

                if (Tracks == null || Tracks.Length == 0)
                {
                    return 0f;
                }

                var max = 0f;
                foreach (var track in Tracks)
                {
                    var end = TrackEnd(track);
                    if (end > max)
                    {
                        max = end;
                    }
                }

                return max;
            }
        }

        internal static float TrackEnd(in TweenTrack track)
        {
            if (track.Motion.Loop == LoopMode.Once)
            {
                return track.Delay + track.Motion.Duration;
            }

            if (track.Motion.LoopCount > 0)
            {
                return track.Delay + track.Motion.Duration * track.Motion.LoopCount;
            }

            // 無限ループは目安として 1 周期分を返す(Validator/エディタ表示用。実際の終了判定は Stop 待ち)。
            return track.Delay + track.Motion.Duration;
        }
    }
}
