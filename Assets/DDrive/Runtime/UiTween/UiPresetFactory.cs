using DDrive.Foundation.Data;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Values;
using UnityEngine;

namespace DDrive.Runtime.Ui
{
    // [15_ui_interaction.md] B-3.5 — UiPresetRef → TweenTrack[] への展開(チケット 4-11 前半)。
    // Build は Play 直前(UiTweenManager.PrepareTracks と同じく Tick の外)にのみ呼ばれる想定のため、
    // ここでの alloc(配列コピー無し。呼び出し側バッファへ直接書く)は許容するが、
    // ループ・スイッチのみで LINQ/クロージャは使わない。
    public static class UiPresetFactory
    {
        // 既定値テーブル(enum 値でインデックスするだけの配列。スイッチ地獄にしない)。
        private static readonly float[] DefaultDurations;
        private static readonly Ease[] DefaultEases;

        static UiPresetFactory()
        {
            var count = System.Enum.GetValues(typeof(UiPreset)).Length;
            DefaultDurations = new float[count];
            DefaultEases = new Ease[count];
            for (var i = 0; i < count; i++)
            {
                DefaultDurations[i] = 0.3f;
                DefaultEases[i] = Ease.OutCubic;
            }

            SetDefault(UiPreset.FadeIn, 0.25f, Ease.OutSine);
            SetDefault(UiPreset.FadeOut, 0.25f, Ease.InSine);
            SetDefault(UiPreset.SlideInLeft, 0.3f, Ease.OutCubic);
            SetDefault(UiPreset.SlideInRight, 0.3f, Ease.OutCubic);
            SetDefault(UiPreset.SlideInTop, 0.3f, Ease.OutCubic);
            SetDefault(UiPreset.SlideInBottom, 0.3f, Ease.OutCubic);
            SetDefault(UiPreset.SlideOutLeft, 0.25f, Ease.InCubic);
            SetDefault(UiPreset.SlideOutRight, 0.25f, Ease.InCubic);
            SetDefault(UiPreset.SlideOutTop, 0.25f, Ease.InCubic);
            SetDefault(UiPreset.SlideOutBottom, 0.25f, Ease.InCubic);
            SetDefault(UiPreset.SlideFadeInLeft, 0.3f, Ease.OutCubic);
            SetDefault(UiPreset.SlideFadeInRight, 0.3f, Ease.OutCubic);
            SetDefault(UiPreset.SlideFadeInTop, 0.3f, Ease.OutCubic);
            SetDefault(UiPreset.SlideFadeInBottom, 0.3f, Ease.OutCubic);
            SetDefault(UiPreset.SlideFadeOutLeft, 0.25f, Ease.InCubic);
            SetDefault(UiPreset.SlideFadeOutRight, 0.25f, Ease.InCubic);
            SetDefault(UiPreset.SlideFadeOutTop, 0.25f, Ease.InCubic);
            SetDefault(UiPreset.SlideFadeOutBottom, 0.25f, Ease.InCubic);
            SetDefault(UiPreset.ScaleIn, 0.25f, Ease.OutBack);
            SetDefault(UiPreset.ScaleOut, 0.2f, Ease.InBack);
            SetDefault(UiPreset.PopIn, 0.3f, Ease.OutBack);
            SetDefault(UiPreset.PopOut, 0.2f, Ease.InBack);
            SetDefault(UiPreset.BounceIn, 0.5f, Ease.OutBounce);
            SetDefault(UiPreset.BounceOut, 0.4f, Ease.InBounce);
            SetDefault(UiPreset.ElasticIn, 0.6f, Ease.OutElastic);
            SetDefault(UiPreset.ElasticOut, 0.4f, Ease.InElastic);
            SetDefault(UiPreset.ZoomInFade, 0.3f, Ease.OutCubic);
            SetDefault(UiPreset.ZoomOutFade, 0.25f, Ease.InCubic);
            SetDefault(UiPreset.ExpandWidth, 0.3f, Ease.OutCubic);
            SetDefault(UiPreset.ExpandHeight, 0.3f, Ease.OutCubic);
            SetDefault(UiPreset.CollapseWidth, 0.25f, Ease.InCubic);
            SetDefault(UiPreset.CollapseHeight, 0.25f, Ease.InCubic);
            SetDefault(UiPreset.Pulse, 1.2f, Ease.InOutSine);
            SetDefault(UiPreset.Blink, 0.6f, Ease.InOutSine);
            SetDefault(UiPreset.Float, 1.6f, Ease.InOutSine);
            SetDefault(UiPreset.Sway, 1.4f, Ease.InOutSine);
            SetDefault(UiPreset.Breathe, 1.8f, Ease.InOutSine);
            SetDefault(UiPreset.RotateLoop, 2f, Ease.Linear);
            SetDefault(UiPreset.ShimmerAlpha, 1.2f, Ease.InOutSine);
            SetDefault(UiPreset.PunchScale, 0.35f, Ease.OutElastic);
            SetDefault(UiPreset.PunchRotation, 0.35f, Ease.OutElastic);
            SetDefault(UiPreset.Shake, 0.4f, Ease.OutSine);
            SetDefault(UiPreset.ShakeHard, 0.5f, Ease.OutSine);
            SetDefault(UiPreset.Flash, 0.3f, Ease.OutSine);
            SetDefault(UiPreset.HeartBeat, 0.6f, Ease.InOutSine);
        }

        private static void SetDefault(UiPreset preset, float duration, Ease ease)
        {
            DefaultDurations[(int)preset] = duration;
            DefaultEases[(int)preset] = ease;
        }

        // 未実装プリセットが暫定的にどれへ委譲されているか(4-11 残作業のガイド用にギャラリーが使う)。
        public static bool IsApproximation(UiPreset preset) => Approximate(preset) != preset;

        private static UiPreset Approximate(UiPreset preset)
        {
            switch (preset)
            {
                case UiPreset.FlipInX:
                case UiPreset.FlipInY:
                    return UiPreset.ScaleIn; // TODO 4-11: 実際の 3D 反転(RotateY)へ差し替え
                case UiPreset.FlipOutX:
                case UiPreset.FlipOutY:
                    return UiPreset.ScaleOut; // TODO 4-11
                case UiPreset.RotateIn:
                    return UiPreset.ZoomInFade; // TODO 4-11: Rotation+Scale の複合へ
                case UiPreset.RotateOut:
                    return UiPreset.ZoomOutFade; // TODO 4-11
                case UiPreset.TypeFillIn:
                    return UiPreset.FadeIn; // TODO 4-11: 文字送り(TMP 統合)が必要
                case UiPreset.RainbowTint:
                    return UiPreset.ShimmerAlpha; // TODO 4-11: Gradient/HSV サイクルが必要
                case UiPreset.WobbleLoop:
                    return UiPreset.Sway; // TODO 4-11: Rotation+Position の複合へ
                case UiPreset.ColorFlash:
                    return UiPreset.Flash; // TODO 4-11: Color トラックへ差し替え(対象 Graphic 色前提)
                case UiPreset.Jelly:
                case UiPreset.Tada:
                case UiPreset.RubberBand:
                    return UiPreset.PunchScale; // TODO 4-11: 非均一スケールの複合波形が必要
                case UiPreset.AttentionJump:
                    return UiPreset.Pulse; // TODO 4-11: 縦方向パンチ+Scale の複合へ
                default:
                    return preset;
            }
        }

        // buffer へ Track を書き込み、書き込んだ本数を返す(0 = 何もしない)。alloc は buffer 提供元(呼び出し側)の責任。
        public static int Build(in UiPresetRef p, RectTransform target, TweenTrack[] buffer)
        {
            if (target == null || buffer == null || buffer.Length == 0 || p.Preset == UiPreset.None)
            {
                return 0;
            }

            var resolved = Approximate(p.Preset);
            var duration = p.Duration > 0f ? p.Duration : DefaultDurations[(int)resolved];
            // EaseOverride は EaseDef(Ease または CustomBezier)だが、プリセット既定テーブルは Ease 単体で
            // 持っているため、上書き時は名前付き Ease 側だけを使う(CustomBezier での上書きは 4-11 の
            // 残作業で TweenTrack.Motion.Parametric へ直接反映する形に拡張する)。
            var ease = IsDefaultEase(p.EaseOverride) ? DefaultEases[(int)resolved] : p.EaseOverride.Ease;
            var dist = p.Distance;
            var cur = target.anchoredPosition;
            var curScale = target.localScale.x <= 0f ? 1f : target.localScale.x;
            var curSize = target.sizeDelta;

            switch (resolved)
            {
                case UiPreset.FadeIn:
                    buffer[0] = AlphaTrack(duration, ease, 0f, 1f);
                    return 1;

                case UiPreset.FadeOut:
                    buffer[0] = AlphaTrack(duration, ease, 1f, 0f);
                    return 1;

                case UiPreset.SlideInLeft:
                case UiPreset.SlideInRight:
                case UiPreset.SlideInTop:
                case UiPreset.SlideInBottom:
                    buffer[0] = PosTrack(duration, ease, SlideFrom(target, DirOf(resolved), dist), cur);
                    return 1;

                case UiPreset.SlideOutLeft:
                case UiPreset.SlideOutRight:
                case UiPreset.SlideOutTop:
                case UiPreset.SlideOutBottom:
                    buffer[0] = PosTrack(duration, ease, cur, SlideFrom(target, DirOf(resolved), dist));
                    return 1;

                case UiPreset.SlideFadeInLeft:
                case UiPreset.SlideFadeInRight:
                case UiPreset.SlideFadeInTop:
                case UiPreset.SlideFadeInBottom:
                    buffer[0] = PosTrack(duration, ease, SlideFrom(target, DirOf(resolved), dist), cur);
                    if (buffer.Length > 1)
                    {
                        buffer[1] = AlphaTrack(duration, ease, 0f, 1f);
                        return 2;
                    }

                    return 1;

                case UiPreset.SlideFadeOutLeft:
                case UiPreset.SlideFadeOutRight:
                case UiPreset.SlideFadeOutTop:
                case UiPreset.SlideFadeOutBottom:
                    buffer[0] = PosTrack(duration, ease, cur, SlideFrom(target, DirOf(resolved), dist));
                    if (buffer.Length > 1)
                    {
                        buffer[1] = AlphaTrack(duration, ease, 1f, 0f);
                        return 2;
                    }

                    return 1;

                case UiPreset.ScaleIn:
                    buffer[0] = ScaleTrack(duration, ease, 0f, curScale);
                    return 1;

                case UiPreset.ScaleOut:
                    buffer[0] = ScaleTrack(duration, ease, curScale, 0f);
                    return 1;

                case UiPreset.PopIn:
                    buffer[0] = ScaleTrack(duration, Ease.OutBack, 0f, curScale);
                    return 1;

                case UiPreset.PopOut:
                    buffer[0] = ScaleTrack(duration, Ease.InBack, curScale, 0f);
                    return 1;

                case UiPreset.BounceIn:
                    buffer[0] = ScaleTrack(duration, Ease.OutBounce, 0f, curScale);
                    return 1;

                case UiPreset.BounceOut:
                    buffer[0] = ScaleTrack(duration, Ease.InBounce, curScale, 0f);
                    return 1;

                case UiPreset.ElasticIn:
                    buffer[0] = ScaleTrack(duration, Ease.OutElastic, 0f, curScale);
                    return 1;

                case UiPreset.ElasticOut:
                    buffer[0] = ScaleTrack(duration, Ease.InElastic, curScale, 0f);
                    return 1;

                case UiPreset.ZoomInFade:
                    buffer[0] = ScaleTrack(duration, ease, curScale * 1.4f, curScale);
                    if (buffer.Length > 1)
                    {
                        buffer[1] = AlphaTrack(duration, ease, 0f, 1f);
                        return 2;
                    }

                    return 1;

                case UiPreset.ZoomOutFade:
                    buffer[0] = ScaleTrack(duration, ease, curScale, curScale * 1.4f);
                    if (buffer.Length > 1)
                    {
                        buffer[1] = AlphaTrack(duration, ease, 1f, 0f);
                        return 2;
                    }

                    return 1;

                case UiPreset.ExpandWidth:
                    buffer[0] = SizeTrack(duration, ease, new Vector2(0f, curSize.y), curSize);
                    return 1;

                case UiPreset.ExpandHeight:
                    buffer[0] = SizeTrack(duration, ease, new Vector2(curSize.x, 0f), curSize);
                    return 1;

                case UiPreset.CollapseWidth:
                    buffer[0] = SizeTrack(duration, ease, curSize, new Vector2(0f, curSize.y));
                    return 1;

                case UiPreset.CollapseHeight:
                    buffer[0] = SizeTrack(duration, ease, curSize, new Vector2(curSize.x, 0f));
                    return 1;

                case UiPreset.Pulse:
                    buffer[0] = ScaleTrack(duration, Ease.InOutSine, curScale, curScale * (dist > 0f ? 1f + dist : 1.06f), LoopMode.PingPong);
                    return 1;

                case UiPreset.Blink:
                    buffer[0] = AlphaTrack(duration, Ease.InOutSine, 1f, 0.2f, LoopMode.PingPong);
                    return 1;

                case UiPreset.Float:
                    buffer[0] = PosTrack(duration, Ease.InOutSine, cur, cur + new Vector2(0f, dist > 0f ? dist : 10f), LoopMode.PingPong);
                    return 1;

                case UiPreset.Sway:
                    buffer[0] = RotTrack(duration, Ease.InOutSine, -(dist > 0f ? dist : 8f), dist > 0f ? dist : 8f, LoopMode.PingPong);
                    return 1;

                case UiPreset.Breathe:
                    buffer[0] = ScaleTrack(duration, Ease.InOutSine, curScale, curScale * (dist > 0f ? 1f + dist : 1.08f), LoopMode.PingPong);
                    return 1;

                case UiPreset.RotateLoop:
                    buffer[0] = RotTrack(duration, Ease.Linear, 0f, 360f, LoopMode.Loop);
                    return 1;

                case UiPreset.ShimmerAlpha:
                    buffer[0] = AlphaTrack(duration, Ease.InOutSine, 0.4f, 1f, LoopMode.PingPong);
                    return 1;

                case UiPreset.PunchScale:
                    buffer[0] = ScaleTrack(duration, Ease.OutElastic, curScale * (1f + (dist > 0f ? dist : 0.2f)), curScale);
                    return 1;

                case UiPreset.PunchRotation:
                    buffer[0] = RotTrack(duration, Ease.OutElastic, dist > 0f ? dist : 15f, 0f);
                    return 1;

                case UiPreset.Shake:
                    buffer[0] = PosTrack(duration, Ease.OutSine, cur + new Vector2(-(dist > 0f ? dist : 8f), 0f), cur + new Vector2(dist > 0f ? dist : 8f, 0f), LoopMode.PingPong, 6);
                    return 1;

                case UiPreset.ShakeHard:
                    buffer[0] = PosTrack(duration, Ease.OutSine, cur + new Vector2(-(dist > 0f ? dist : 16f), 0f), cur + new Vector2(dist > 0f ? dist : 16f, 0f), LoopMode.PingPong, 10);
                    return 1;

                case UiPreset.Flash:
                    buffer[0] = AlphaTrack(duration, Ease.OutSine, 1f, 0.2f, LoopMode.PingPong, 2);
                    return 1;

                case UiPreset.HeartBeat:
                    buffer[0] = ScaleTrack(duration, Ease.InOutSine, curScale, curScale * (dist > 0f ? 1f + dist : 1.15f), LoopMode.PingPong, 2);
                    return 1;

                default:
                    // Approximate() が必ず実装済みプリセットへ写像するため通常は到達しない安全弁。
                    buffer[0] = AlphaTrack(duration, ease, 0f, 1f);
                    return 1;
            }
        }

        private static bool IsDefaultEase(in EaseDef ease) => ease.Equals(default(EaseDef));

        private static OffScreenDirection DirOf(UiPreset preset)
        {
            switch (preset)
            {
                case UiPreset.SlideInLeft:
                case UiPreset.SlideOutLeft:
                case UiPreset.SlideFadeInLeft:
                case UiPreset.SlideFadeOutLeft:
                    return OffScreenDirection.Left;
                case UiPreset.SlideInRight:
                case UiPreset.SlideOutRight:
                case UiPreset.SlideFadeInRight:
                case UiPreset.SlideFadeOutRight:
                    return OffScreenDirection.Right;
                case UiPreset.SlideInTop:
                case UiPreset.SlideOutTop:
                case UiPreset.SlideFadeInTop:
                case UiPreset.SlideFadeOutTop:
                    return OffScreenDirection.Top;
                default:
                    return OffScreenDirection.Bottom;
            }
        }

        private static Vector2 SlideFrom(RectTransform target, OffScreenDirection dir, float distance)
        {
            if (distance > 0f)
            {
                var cur = target.anchoredPosition;
                switch (dir)
                {
                    case OffScreenDirection.Left: return cur + new Vector2(-distance, 0f);
                    case OffScreenDirection.Right: return cur + new Vector2(distance, 0f);
                    case OffScreenDirection.Top: return cur + new Vector2(0f, distance);
                    default: return cur + new Vector2(0f, -distance);
                }
            }

            return UiTweenManager.ComputeOffScreenAnchoredPosition(target, dir);
        }

        private static TweenTrack Track(
            TweenProperty prop, float duration, Ease ease, ParamValue from, ParamValue to,
            LoopMode loop = LoopMode.Once, int loopCount = 0)
        {
            return new TweenTrack
            {
                Property = prop,
                Motion = new ValueDef
                {
                    Mode = ValueMode.Parametric,
                    Parametric = EaseDef.Named(ease),
                    Time = TimeDef.Duration(Mathf.Max(0.0001f, duration)),
                    Loop = loop,
                    LoopCount = loopCount,
                },
                From = TweenFromMode.Absolute,
                FromValue = from,
                ToValue = to,
            };
        }

        private static TweenTrack PosTrack(float duration, Ease ease, Vector2 from, Vector2 to, LoopMode loop = LoopMode.Once, int loopCount = 0)
            => Track(TweenProperty.AnchoredPosition, duration, ease, VecParam(from.x, from.y), VecParam(to.x, to.y), loop, loopCount);

        private static TweenTrack ScaleTrack(float duration, Ease ease, float from, float to, LoopMode loop = LoopMode.Once, int loopCount = 0)
            => Track(TweenProperty.Scale, duration, ease, VecParam(from, from, from), VecParam(to, to, to), loop, loopCount);

        private static TweenTrack AlphaTrack(float duration, Ease ease, float from, float to, LoopMode loop = LoopMode.Once, int loopCount = 0)
            => Track(TweenProperty.Alpha, duration, ease, ParamValue.Of(from), ParamValue.Of(to), loop, loopCount);

        private static TweenTrack RotTrack(float duration, Ease ease, float from, float to, LoopMode loop = LoopMode.Once, int loopCount = 0)
            => Track(TweenProperty.Rotation, duration, ease, ParamValue.Of(from), ParamValue.Of(to), loop, loopCount);

        private static TweenTrack SizeTrack(float duration, Ease ease, Vector2 from, Vector2 to)
            => Track(TweenProperty.SizeDelta, duration, ease, VecParam(from.x, from.y), VecParam(to.x, to.y));

        private static ParamValue VecParam(float x, float y, float z = 0f)
            => new() { Type = ParamValueType.Vector, VectorValue = new Vector4(x, y, z, 0f) };
    }
}
