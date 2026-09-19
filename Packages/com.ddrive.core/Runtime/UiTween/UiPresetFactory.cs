using DDrive.Foundation.Data;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Values;
using UnityEngine;
using UnityEngine.UI;

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

            // 2026-09-11(4-11 完了): ここから下は Approximate() 委譲を撤去し実装した分の既定値。
            SetDefault(UiPreset.FlipInX, 0.35f, Ease.OutCubic);
            SetDefault(UiPreset.FlipInY, 0.35f, Ease.OutCubic);
            SetDefault(UiPreset.FlipOutX, 0.25f, Ease.InCubic);
            SetDefault(UiPreset.FlipOutY, 0.25f, Ease.InCubic);
            SetDefault(UiPreset.RotateIn, 0.35f, Ease.OutBack);
            SetDefault(UiPreset.RotateOut, 0.25f, Ease.InCubic);
            SetDefault(UiPreset.TypeFillIn, 0.6f, Ease.Linear);
            SetDefault(UiPreset.RainbowTint, 2f, Ease.Linear);
            SetDefault(UiPreset.WobbleLoop, 0.6f, Ease.InOutQuad);
            SetDefault(UiPreset.ColorFlash, 0.3f, Ease.OutSine);
            SetDefault(UiPreset.Jelly, 0.5f, Ease.OutElastic);
            SetDefault(UiPreset.Tada, 0.6f, Ease.InOutSine);
            SetDefault(UiPreset.RubberBand, 0.6f, Ease.OutElastic);
            SetDefault(UiPreset.AttentionJump, 0.4f, Ease.OutQuad);
        }

        private static void SetDefault(UiPreset preset, float duration, Ease ease)
        {
            DefaultDurations[(int)preset] = duration;
            DefaultEases[(int)preset] = ease;
        }

        // 2026-09-11(4-11 完了): 全プリセットが実装されたため、現在は Approximate(x) == x が常に成り立つ。
        // IsApproximation/Approximate 自体は将来また未実装プリセットを追加する可能性に備えて安全弁として残す
        // (docs [15] 実装メモ参照。ギャラリー等はこの API で「近似実装が残っていないか」を検出できる)。
        public static bool IsApproximation(UiPreset preset) => Approximate(preset) != preset;

        private static UiPreset Approximate(UiPreset preset) => preset;

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

                // Codex レビュー対応(2026-09-11): From=cur-dist/To=cur+dist の対称往復だと、loopCount が偶数の
                // PingPong は「From」で止まる(Mathf.PingPong の仕様。Jelly のコメント参照)ため、cur ではなく
                // cur-dist で静止していた。From=cur(静止姿勢)/To=cur+dist に変更し、偶数 loopCount で必ず
                // cur(元の位置)に戻るようにする。
                case UiPreset.Shake:
                    buffer[0] = PosTrack(duration, Ease.OutSine, cur, cur + new Vector2(dist > 0f ? dist : 8f, 0f), LoopMode.PingPong, 6);
                    return 1;

                case UiPreset.ShakeHard:
                    buffer[0] = PosTrack(duration, Ease.OutSine, cur, cur + new Vector2(dist > 0f ? dist : 16f, 0f), LoopMode.PingPong, 10);
                    return 1;

                case UiPreset.Flash:
                    buffer[0] = AlphaTrack(duration, Ease.OutSine, 1f, 0.2f, LoopMode.PingPong, 2);
                    return 1;

                case UiPreset.HeartBeat:
                    buffer[0] = ScaleTrack(duration, Ease.InOutSine, curScale, curScale * (dist > 0f ? 1f + dist : 1.15f), LoopMode.PingPong, 2);
                    return 1;

                // ── 2026-09-11(4-11 完了)から下、実装追加分 ──

                // FlipInX/Y: RotationX/Y を 90°→0° へ戻しつつ Alpha も 0→1(裏面が透けて見える違和感を避ける)。
                // 「90° スタートで反転しながら現れる」向きに統一(FlipOut は逆順で 0°→90°)。
                case UiPreset.FlipInX:
                    buffer[0] = RotXTrack(duration, ease, 90f, 0f);
                    if (buffer.Length > 1)
                    {
                        buffer[1] = AlphaTrack(duration, ease, 0f, 1f);
                        return 2;
                    }

                    return 1;

                case UiPreset.FlipInY:
                    buffer[0] = RotYTrack(duration, ease, 90f, 0f);
                    if (buffer.Length > 1)
                    {
                        buffer[1] = AlphaTrack(duration, ease, 0f, 1f);
                        return 2;
                    }

                    return 1;

                case UiPreset.FlipOutX:
                    buffer[0] = RotXTrack(duration, ease, 0f, 90f);
                    if (buffer.Length > 1)
                    {
                        buffer[1] = AlphaTrack(duration, ease, 1f, 0f);
                        return 2;
                    }

                    return 1;

                case UiPreset.FlipOutY:
                    buffer[0] = RotYTrack(duration, ease, 0f, 90f);
                    if (buffer.Length > 1)
                    {
                        buffer[1] = AlphaTrack(duration, ease, 1f, 0f);
                        return 2;
                    }

                    return 1;

                // RotateIn/Out: Z回転 ±180°→0°(逆側)+ Alpha フェード。符号は「時計回りに回り込みながら現れる」で統一。
                case UiPreset.RotateIn:
                    buffer[0] = RotTrack(duration, ease, 180f, 0f);
                    if (buffer.Length > 1)
                    {
                        buffer[1] = AlphaTrack(duration, ease, 0f, 1f);
                        return 2;
                    }

                    return 1;

                case UiPreset.RotateOut:
                    buffer[0] = RotTrack(duration, ease, 0f, -180f);
                    if (buffer.Length > 1)
                    {
                        buffer[1] = AlphaTrack(duration, ease, 1f, 0f);
                        return 2;
                    }

                    return 1;

                case UiPreset.TypeFillIn:
                {
                    var image = target.GetComponent<Image>();
                    if (image == null || image.type != Image.Type.Filled)
                    {
                        // 例外にはしない(CLAUDE.md #4)。デザイナーへの気づき用に警告だけ出して継続する。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Debug.LogWarning($"[DDrive] TypeFillIn は Image.Type.Filled を想定していますが、'{target.name}' はそうなっていません。FillAmount は反映されない可能性があります。");
#endif
                    }

                    buffer[0] = FillTrack(duration, ease, 0f, 1f);
                    return 1;
                }

                // RainbowTint: ColorHue を 0→1 で Loop(HSV(H,1,1) を毎フレーム計算。単純な RGB 直線補間では
                // 彩度が落ちて虹に見えないため専用プロパティを追加した。[15] 実装メモ参照)。
                case UiPreset.RainbowTint:
                    buffer[0] = HueTrack(duration, Ease.Linear, 0f, 1f, LoopMode.Loop);
                    return 1;

                // WobbleLoop: Sway と同系統(Z回転 PingPong)だが、周期を短く・角度をやや大きく・Ease を変えて
                // 「揺れ」ではなく「振動」に近い体感にする(Sway=ゆったり左右、WobbleLoop=小刻み)。
                case UiPreset.WobbleLoop:
                    buffer[0] = RotTrack(duration, Ease.InOutQuad, -(dist > 0f ? dist : 6f), dist > 0f ? dist : 6f, LoopMode.PingPong);
                    return 1;

                // ColorFlash: 対象 Graphic の現在色→白→現在色(PingPong x2)。Flash(Alpha)と違い色そのものが光る。
                case UiPreset.ColorFlash:
                {
                    var graphic = target.GetComponent<Graphic>();
                    var baseColor = graphic != null ? graphic.color : Color.white;
                    buffer[0] = ColorTrack(duration, ease, baseColor, Color.white, LoopMode.PingPong, 2);
                    return 1;
                }

                // Jelly: Scale の非均一 PingPong(x が伸びる間 y が縮む=カウンターフェイズ)。loopCount=2 で
                // 1 往復して必ず (1,1,1) 相当の元スケールへ戻る。
                case UiPreset.Jelly:
                    buffer[0] = ScaleTrackNonUniform(
                        duration, Ease.OutElastic,
                        new Vector3(curScale, curScale, curScale),
                        new Vector3(curScale * 1.25f, curScale * 0.8f, curScale),
                        LoopMode.PingPong, 2);
                    return 1;

                // Tada: Scale パルス(PingPong x2)+ Z回転の小刻みな揺れ(PingPong x4、Scale とは異なる周期感を出す)。
                // 2 トラックは Instance 内で並列に走る。
                case UiPreset.Tada:
                    buffer[0] = ScaleTrack(duration, Ease.InOutSine, curScale, curScale * (dist > 0f ? 1f + dist : 1.1f), LoopMode.PingPong, 2);
                    if (buffer.Length > 1)
                    {
                        // Codex レビュー対応(2026-09-11): From=-dist*30/To=dist*30 だと loopCount=4(偶数)は
                        // From(-dist*30°)で静止してしまう。From=0(回転無し)/To=dist*30° にして 0° へ戻す。
                        buffer[1] = RotTrack(duration, Ease.InOutSine, 0f, dist > 0f ? dist * 30f : 6f, LoopMode.PingPong, 4);
                        return 2;
                    }

                    return 1;

                // RubberBand: 「X伸び/Y縮み → 元スケール」の単発(Once)着地演出として解釈する(このエンジンは
                // 1 Track = 1 Ease による単一補間のため、真の意味での逐次(X→Y の順送り)は表現できない。
                // Jelly[PingPong 往復] とは違う一発ネタとして、OutElastic の大きめオーバーシュートで差別化する)。
                case UiPreset.RubberBand:
                    buffer[0] = ScaleTrackNonUniform(
                        duration, Ease.OutElastic,
                        new Vector3(curScale * 1.3f, curScale * 0.7f, curScale),
                        new Vector3(curScale, curScale, curScale));
                    return 1;

                // AttentionJump: 縦方向に跳ねて戻る。1 Ease では「上がって下がる」を表現できないため、
                // 前半(OutQuad, Delay=0)と後半(InQuad, Delay=duration/2)の 2 Track に分けて連結する
                // (UiTweenManager は Track ごとに独立した Delay を持てるためこれで表現できる)。
                case UiPreset.AttentionJump:
                {
                    var hop = dist > 0f ? dist : 20f;
                    var half = duration * 0.5f;
                    buffer[0] = PosTrack(half, Ease.OutQuad, cur, cur + new Vector2(0f, hop), LoopMode.Once, 0, 0f);
                    if (buffer.Length > 1)
                    {
                        buffer[1] = PosTrack(half, Ease.InQuad, cur + new Vector2(0f, hop), cur, LoopMode.Once, 0, half);
                        return 2;
                    }

                    return 1;
                }

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
            LoopMode loop = LoopMode.Once, int loopCount = 0, float delay = 0f)
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
                Delay = delay,
                From = TweenFromMode.Absolute,
                FromValue = from,
                ToValue = to,
            };
        }

        private static TweenTrack PosTrack(float duration, Ease ease, Vector2 from, Vector2 to, LoopMode loop = LoopMode.Once, int loopCount = 0, float delay = 0f)
            => Track(TweenProperty.AnchoredPosition, duration, ease, VecParam(from.x, from.y), VecParam(to.x, to.y), loop, loopCount, delay);

        private static TweenTrack ScaleTrack(float duration, Ease ease, float from, float to, LoopMode loop = LoopMode.Once, int loopCount = 0)
            => Track(TweenProperty.Scale, duration, ease, VecParam(from, from, from), VecParam(to, to, to), loop, loopCount);

        // 非均一スケール版(Jelly/RubberBand 用)。x/y/z を個別に指定できる。
        private static TweenTrack ScaleTrackNonUniform(float duration, Ease ease, Vector3 from, Vector3 to, LoopMode loop = LoopMode.Once, int loopCount = 0)
            => Track(TweenProperty.Scale, duration, ease, VecParam(from.x, from.y, from.z), VecParam(to.x, to.y, to.z), loop, loopCount);

        private static TweenTrack AlphaTrack(float duration, Ease ease, float from, float to, LoopMode loop = LoopMode.Once, int loopCount = 0)
            => Track(TweenProperty.Alpha, duration, ease, ParamValue.Of(from), ParamValue.Of(to), loop, loopCount);

        private static TweenTrack RotTrack(float duration, Ease ease, float from, float to, LoopMode loop = LoopMode.Once, int loopCount = 0)
            => Track(TweenProperty.Rotation, duration, ease, ParamValue.Of(from), ParamValue.Of(to), loop, loopCount);

        private static TweenTrack RotXTrack(float duration, Ease ease, float from, float to, LoopMode loop = LoopMode.Once, int loopCount = 0)
            => Track(TweenProperty.RotationX, duration, ease, ParamValue.Of(from), ParamValue.Of(to), loop, loopCount);

        private static TweenTrack RotYTrack(float duration, Ease ease, float from, float to, LoopMode loop = LoopMode.Once, int loopCount = 0)
            => Track(TweenProperty.RotationY, duration, ease, ParamValue.Of(from), ParamValue.Of(to), loop, loopCount);

        private static TweenTrack SizeTrack(float duration, Ease ease, Vector2 from, Vector2 to)
            => Track(TweenProperty.SizeDelta, duration, ease, VecParam(from.x, from.y), VecParam(to.x, to.y));

        private static TweenTrack ColorTrack(float duration, Ease ease, Color from, Color to, LoopMode loop = LoopMode.Once, int loopCount = 0)
            => new()
            {
                Property = TweenProperty.Color,
                Motion = new ValueDef
                {
                    Mode = ValueMode.Parametric,
                    Parametric = EaseDef.Named(ease),
                    Time = TimeDef.Duration(Mathf.Max(0.0001f, duration)),
                    Loop = loop,
                    LoopCount = loopCount,
                },
                From = TweenFromMode.Absolute,
                FromValue = new ParamValue { Type = ParamValueType.Color, ColorValue = from },
                ToValue = new ParamValue { Type = ParamValueType.Color, ColorValue = to },
            };

        private static TweenTrack HueTrack(float duration, Ease ease, float from, float to, LoopMode loop = LoopMode.Once, int loopCount = 0)
            => Track(TweenProperty.ColorHue, duration, ease, ParamValue.Of(from), ParamValue.Of(to), loop, loopCount);

        private static TweenTrack FillTrack(float duration, Ease ease, float from, float to, LoopMode loop = LoopMode.Once, int loopCount = 0)
            => Track(TweenProperty.FillAmount, duration, ease, ParamValue.Of(from), ParamValue.Of(to), loop, loopCount);

        private static ParamValue VecParam(float x, float y, float z = 0f)
            => new() { Type = ParamValueType.Vector, VectorValue = new Vector4(x, y, z, 0f) };
    }
}
