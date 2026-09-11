using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Handle;
using UnityEngine;
using UiTweenId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Ui.UiTweenMarker>;

namespace DDrive.Runtime.Ui
{
    // [15_ui_interaction.md] B-5 — プログラマー向けの薄い静的ファサード(Audio.cs / Vfx.cs と同じ設計。ADR#3)。
    // 未 Bind の呼び出しは全て no-op(Invalid Handle)で継続する(例外で止めない)。
    public static class UiFx
    {
        private static UiTweenManager _instance;

        // Codex レビュー対応(2026-09-11): Play のたびに new TweenTrack[MaxTracksPerTween] していた
        // (定常経路での alloc、[12_review.md] §3)。PlayTracks が OwnedTracks へコピーするため、
        // このスクラッチは呼び出しをまたいで使い回せる(この Play 呼び出し内でしか参照しない)。
        private static readonly TweenTrack[] Scratch = new TweenTrack[UiTweenManager.MaxTracksPerTween];

        public static void Bind(UiTweenManager instance) => _instance = instance;

        public static bool IsBound => _instance != null;

        // ── Data / Preset 再生 ──

        public static Handle<UiTweenMarker> Play(UiTweenId id, RectTransform target)
            => _instance?.Play(id, target) ?? Handle<UiTweenMarker>.Invalid;

        public static Handle<UiTweenMarker> PlayData(UiTweenData data, RectTransform target)
            => _instance?.PlayData(data, target) ?? Handle<UiTweenMarker>.Invalid;

        public static Handle<UiTweenMarker> Play(UiPreset preset, RectTransform target, in UiPresetRef p = default)
        {
            if (_instance == null || target == null || preset == UiPreset.None)
            {
                return Handle<UiTweenMarker>.Invalid;
            }

            var actual = p;
            actual.Preset = preset;
            var count = UiPresetFactory.Build(in actual, target, Scratch);
            if (count <= 0)
            {
                return Handle<UiTweenMarker>.Invalid;
            }

            if (actual.Se.IsValid)
            {
                Audio.Audio.PlaySe(actual.Se);
            }

            return _instance.PlayTracks(Scratch, count, target);
        }

        public static Handle<UiTweenMarker> Appear(RectTransform t) => FadeIn(t);

        public static Handle<UiTweenMarker> Disappear(RectTransform t) => FadeOut(t);

        // ── プリセット 1 行関数(実装済み分。docs [15] B-5 の想定シグネチャ) ──

        public static Handle<UiTweenMarker> FadeIn(RectTransform t, float sec = 0.25f) => Play(UiPreset.FadeIn, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> FadeOut(RectTransform t, float sec = 0.25f) => Play(UiPreset.FadeOut, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> SlideInLeft(RectTransform t, float sec = 0.3f, float distance = 0f) => Play(UiPreset.SlideInLeft, t, new UiPresetRef { Duration = sec, Distance = distance });
        public static Handle<UiTweenMarker> SlideInRight(RectTransform t, float sec = 0.3f, float distance = 0f) => Play(UiPreset.SlideInRight, t, new UiPresetRef { Duration = sec, Distance = distance });
        public static Handle<UiTweenMarker> SlideInTop(RectTransform t, float sec = 0.3f, float distance = 0f) => Play(UiPreset.SlideInTop, t, new UiPresetRef { Duration = sec, Distance = distance });
        public static Handle<UiTweenMarker> SlideInBottom(RectTransform t, float sec = 0.3f, float distance = 0f) => Play(UiPreset.SlideInBottom, t, new UiPresetRef { Duration = sec, Distance = distance });
        public static Handle<UiTweenMarker> SlideOutLeft(RectTransform t, float sec = 0.25f, float distance = 0f) => Play(UiPreset.SlideOutLeft, t, new UiPresetRef { Duration = sec, Distance = distance });
        public static Handle<UiTweenMarker> SlideOutRight(RectTransform t, float sec = 0.25f, float distance = 0f) => Play(UiPreset.SlideOutRight, t, new UiPresetRef { Duration = sec, Distance = distance });
        public static Handle<UiTweenMarker> SlideOutTop(RectTransform t, float sec = 0.25f, float distance = 0f) => Play(UiPreset.SlideOutTop, t, new UiPresetRef { Duration = sec, Distance = distance });
        public static Handle<UiTweenMarker> SlideOutBottom(RectTransform t, float sec = 0.25f, float distance = 0f) => Play(UiPreset.SlideOutBottom, t, new UiPresetRef { Duration = sec, Distance = distance });
        public static Handle<UiTweenMarker> ScaleIn(RectTransform t, float sec = 0.25f) => Play(UiPreset.ScaleIn, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> ScaleOut(RectTransform t, float sec = 0.2f) => Play(UiPreset.ScaleOut, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> PopIn(RectTransform t, float sec = 0.3f) => Play(UiPreset.PopIn, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> PopOut(RectTransform t, float sec = 0.2f) => Play(UiPreset.PopOut, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> BounceIn(RectTransform t, float sec = 0.5f) => Play(UiPreset.BounceIn, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> ElasticIn(RectTransform t, float sec = 0.6f) => Play(UiPreset.ElasticIn, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> ZoomInFade(RectTransform t, float sec = 0.3f) => Play(UiPreset.ZoomInFade, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> ZoomOutFade(RectTransform t, float sec = 0.25f) => Play(UiPreset.ZoomOutFade, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> ExpandWidth(RectTransform t, float sec = 0.3f) => Play(UiPreset.ExpandWidth, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> ExpandHeight(RectTransform t, float sec = 0.3f) => Play(UiPreset.ExpandHeight, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> CollapseWidth(RectTransform t, float sec = 0.25f) => Play(UiPreset.CollapseWidth, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> CollapseHeight(RectTransform t, float sec = 0.25f) => Play(UiPreset.CollapseHeight, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> Pulse(RectTransform t, float scale = 1.06f, float period = 1.2f) => Play(UiPreset.Pulse, t, new UiPresetRef { Duration = period, Distance = scale - 1f });
        public static Handle<UiTweenMarker> Blink(RectTransform t, float period = 0.6f) => Play(UiPreset.Blink, t, new UiPresetRef { Duration = period });
        public static Handle<UiTweenMarker> Float(RectTransform t, float distance = 10f, float period = 1.6f) => Play(UiPreset.Float, t, new UiPresetRef { Duration = period, Distance = distance });
        public static Handle<UiTweenMarker> Sway(RectTransform t, float degrees = 8f, float period = 1.4f) => Play(UiPreset.Sway, t, new UiPresetRef { Duration = period, Distance = degrees });
        public static Handle<UiTweenMarker> Breathe(RectTransform t, float scale = 1.08f, float period = 1.8f) => Play(UiPreset.Breathe, t, new UiPresetRef { Duration = period, Distance = scale - 1f });
        public static Handle<UiTweenMarker> RotateLoop(RectTransform t, float period = 2f) => Play(UiPreset.RotateLoop, t, new UiPresetRef { Duration = period });
        public static Handle<UiTweenMarker> ShimmerAlpha(RectTransform t, float period = 1.2f) => Play(UiPreset.ShimmerAlpha, t, new UiPresetRef { Duration = period });
        public static Handle<UiTweenMarker> PunchScale(RectTransform t, float strength = 0.2f, float sec = 0.35f) => Play(UiPreset.PunchScale, t, new UiPresetRef { Duration = sec, Distance = strength });
        public static Handle<UiTweenMarker> PunchRotation(RectTransform t, float degrees = 15f, float sec = 0.35f) => Play(UiPreset.PunchRotation, t, new UiPresetRef { Duration = sec, Distance = degrees });
        public static Handle<UiTweenMarker> Shake(RectTransform t, float strength = 8f, float sec = 0.4f) => Play(UiPreset.Shake, t, new UiPresetRef { Duration = sec, Distance = strength });
        public static Handle<UiTweenMarker> ShakeHard(RectTransform t, float strength = 16f, float sec = 0.5f) => Play(UiPreset.ShakeHard, t, new UiPresetRef { Duration = sec, Distance = strength });
        public static Handle<UiTweenMarker> Flash(RectTransform t, float sec = 0.3f) => Play(UiPreset.Flash, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> HeartBeat(RectTransform t, float sec = 0.6f) => Play(UiPreset.HeartBeat, t, new UiPresetRef { Duration = sec });

        // 2026-09-11(4-11 完了): 残っていた UiPreset 全メンバーの 1 行関数を追加(既存分と命名/引数順を揃える)。
        public static Handle<UiTweenMarker> BounceOut(RectTransform t, float sec = 0.4f) => Play(UiPreset.BounceOut, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> ElasticOut(RectTransform t, float sec = 0.4f) => Play(UiPreset.ElasticOut, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> FlipInX(RectTransform t, float sec = 0.35f) => Play(UiPreset.FlipInX, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> FlipInY(RectTransform t, float sec = 0.35f) => Play(UiPreset.FlipInY, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> FlipOutX(RectTransform t, float sec = 0.25f) => Play(UiPreset.FlipOutX, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> FlipOutY(RectTransform t, float sec = 0.25f) => Play(UiPreset.FlipOutY, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> RotateIn(RectTransform t, float sec = 0.35f) => Play(UiPreset.RotateIn, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> RotateOut(RectTransform t, float sec = 0.25f) => Play(UiPreset.RotateOut, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> TypeFillIn(RectTransform t, float sec = 0.6f) => Play(UiPreset.TypeFillIn, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> SlideFadeInLeft(RectTransform t, float sec = 0.3f, float distance = 0f) => Play(UiPreset.SlideFadeInLeft, t, new UiPresetRef { Duration = sec, Distance = distance });
        public static Handle<UiTweenMarker> SlideFadeInRight(RectTransform t, float sec = 0.3f, float distance = 0f) => Play(UiPreset.SlideFadeInRight, t, new UiPresetRef { Duration = sec, Distance = distance });
        public static Handle<UiTweenMarker> SlideFadeInTop(RectTransform t, float sec = 0.3f, float distance = 0f) => Play(UiPreset.SlideFadeInTop, t, new UiPresetRef { Duration = sec, Distance = distance });
        public static Handle<UiTweenMarker> SlideFadeInBottom(RectTransform t, float sec = 0.3f, float distance = 0f) => Play(UiPreset.SlideFadeInBottom, t, new UiPresetRef { Duration = sec, Distance = distance });
        public static Handle<UiTweenMarker> SlideFadeOutLeft(RectTransform t, float sec = 0.25f, float distance = 0f) => Play(UiPreset.SlideFadeOutLeft, t, new UiPresetRef { Duration = sec, Distance = distance });
        public static Handle<UiTweenMarker> SlideFadeOutRight(RectTransform t, float sec = 0.25f, float distance = 0f) => Play(UiPreset.SlideFadeOutRight, t, new UiPresetRef { Duration = sec, Distance = distance });
        public static Handle<UiTweenMarker> SlideFadeOutTop(RectTransform t, float sec = 0.25f, float distance = 0f) => Play(UiPreset.SlideFadeOutTop, t, new UiPresetRef { Duration = sec, Distance = distance });
        public static Handle<UiTweenMarker> SlideFadeOutBottom(RectTransform t, float sec = 0.25f, float distance = 0f) => Play(UiPreset.SlideFadeOutBottom, t, new UiPresetRef { Duration = sec, Distance = distance });
        public static Handle<UiTweenMarker> RainbowTint(RectTransform t, float period = 2f) => Play(UiPreset.RainbowTint, t, new UiPresetRef { Duration = period });
        public static Handle<UiTweenMarker> WobbleLoop(RectTransform t, float degrees = 6f, float period = 0.6f) => Play(UiPreset.WobbleLoop, t, new UiPresetRef { Duration = period, Distance = degrees });
        public static Handle<UiTweenMarker> ColorFlash(RectTransform t, float sec = 0.3f) => Play(UiPreset.ColorFlash, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> Jelly(RectTransform t, float sec = 0.5f) => Play(UiPreset.Jelly, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> Tada(RectTransform t, float sec = 0.6f) => Play(UiPreset.Tada, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> RubberBand(RectTransform t, float sec = 0.6f) => Play(UiPreset.RubberBand, t, new UiPresetRef { Duration = sec });
        public static Handle<UiTweenMarker> AttentionJump(RectTransform t, float height = 20f, float sec = 0.4f) => Play(UiPreset.AttentionJump, t, new UiPresetRef { Duration = sec, Distance = height });

        // ── アドホック(データ化するほどでない場面用) ──

        public static Handle<UiTweenMarker> MoveTo(RectTransform t, Vector2 to, float sec, Ease e = Ease.OutCubic) => _instance?.MoveTo(t, to, sec, e) ?? Handle<UiTweenMarker>.Invalid;
        public static Handle<UiTweenMarker> Scale(RectTransform t, float to, float sec, Ease e = Ease.OutBack) => _instance?.Scale(t, to, sec, e) ?? Handle<UiTweenMarker>.Invalid;
        public static Handle<UiTweenMarker> Fade(CanvasGroup g, float to, float sec, Ease e = Ease.Linear) => _instance?.Fade(g, to, sec, e) ?? Handle<UiTweenMarker>.Invalid;
        public static Handle<UiTweenMarker> Rotate(RectTransform t, float toZ, float sec, Ease e = Ease.OutCubic) => _instance?.Rotate(t, toZ, sec, e) ?? Handle<UiTweenMarker>.Invalid;
        public static Handle<UiTweenMarker> MoveAlong(RectTransform t, SplinePath path, float sec, Ease e = Ease.Linear) => _instance?.MoveAlong(t, path, sec, e) ?? Handle<UiTweenMarker>.Invalid;

        // ── Handle 操作 ──

        public static void Stop(Handle<UiTweenMarker> h, bool complete = false) => _instance?.Stop(h, complete);

        public static void StopAll(RectTransform target) => _instance?.StopAll(target);

        public static bool IsPlaying(Handle<UiTweenMarker> h) => _instance?.IsPlaying(h) ?? false;

        public static float Progress(Handle<UiTweenMarker> h) => _instance?.Progress(h) ?? 0f;

        public static void SetSpeed(Handle<UiTweenMarker> h, float speed) => _instance?.SetSpeed(h, speed);

        public static UniTask WaitAsync(Handle<UiTweenMarker> h) => _instance != null ? _instance.WaitAsync(h) : UniTask.CompletedTask;

        // ── 連結・同時 ──

        public static TweenSequence Sequence() => new(_instance);
    }

    // `h.Stop()` / `await h` の書き味を Handle 型を汚さずに提供する拡張。実体は UiFx ファサードへ委譲する。
    public static class TweenHandleExtensions
    {
        public static bool IsPlaying(this Handle<UiTweenMarker> h) => UiFx.IsPlaying(h);
        public static void Stop(this Handle<UiTweenMarker> h) => UiFx.Stop(h);
        public static void Complete(this Handle<UiTweenMarker> h) => UiFx.Stop(h, true);
        public static float Progress(this Handle<UiTweenMarker> h) => UiFx.Progress(h);
        public static void SetSpeed(this Handle<UiTweenMarker> h, float speed) => UiFx.SetSpeed(h, speed);
        public static UniTask WaitAsync(this Handle<UiTweenMarker> h) => UiFx.WaitAsync(h);
    }

    // [15_ui_interaction.md] B-5 — 「ちょっとした演出シーケンス」を UniTask で連結する薄いビルダー。
    // Tick のホットパスではないため、通常の List/async を使ってよい([12_review.md] §3 は定常経路限定)。
    public sealed class TweenSequence
    {
        private struct Step
        {
            public Handle<UiTweenMarker> Handle;
            public float IntervalSec;
            public bool IsInterval;
            public bool Join;
        }

        private readonly List<Step> _steps = new();
        private readonly UiTweenManager _manager;

        internal TweenSequence(UiTweenManager manager) => _manager = manager;

        public TweenSequence Append(Handle<UiTweenMarker> handle)
        {
            _steps.Add(new Step { Handle = handle });
            return this;
        }

        // 直前の Append/Join と同時に再生する(同じ「区間」として扱われ、次の AppendInterval/Append まで並列で走る)。
        public TweenSequence Join(Handle<UiTweenMarker> handle)
        {
            _steps.Add(new Step { Handle = handle, Join = true });
            return this;
        }

        public TweenSequence AppendInterval(float sec)
        {
            _steps.Add(new Step { IntervalSec = sec, IsInterval = true });
            return this;
        }

        public async UniTask Play()
        {
            var parallel = new List<Handle<UiTweenMarker>>();
            for (var i = 0; i < _steps.Count; i++)
            {
                var step = _steps[i];
                if (step.IsInterval)
                {
                    await AwaitParallel(parallel);
                    parallel.Clear();
                    await UniTask.Delay(TimeSpan.FromSeconds(step.IntervalSec));
                    continue;
                }

                if (step.Join && parallel.Count > 0)
                {
                    parallel.Add(step.Handle);
                }
                else
                {
                    await AwaitParallel(parallel);
                    parallel.Clear();
                    parallel.Add(step.Handle);
                }
            }

            await AwaitParallel(parallel);
        }

        private async UniTask AwaitParallel(List<Handle<UiTweenMarker>> handles)
        {
            if (_manager == null)
            {
                return;
            }

            for (var i = 0; i < handles.Count; i++)
            {
                await _manager.WaitAsync(handles[i]);
            }
        }
    }
}
