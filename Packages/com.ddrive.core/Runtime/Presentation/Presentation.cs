using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Handle;
using R3;
using PresentationId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Presentation.PresentationMarker>;

namespace DDrive.Runtime.Presentation
{
    // デザイナー/プログラマー向けの薄い静的ファサード(Audio.cs / Vfx.cs と同じ設計。ADR#3)。
    // 未 Bind の呼び出しは全て no-op(Invalid Handle)で継続する(例外で止めない)。
    //
    //   Presentation.Play(PRESENTID.SkillSlash, ctx) の 1 行で剣攻撃演出が再生できる([08] §1)。
    public static class Presentation
    {
        private static PresentationManager _instance;

        public static void Bind(PresentationManager instance) => _instance = instance;

        public static bool IsBound => _instance != null;

        public static PresentationHandle Play(PresentationId id, in PlayContext ctx)
            => _instance != null ? new PresentationHandle(_instance.Play(id, in ctx)) : PresentationHandle.Invalid;

        public static PresentationHandle PlayData(PresentationData data, in PlayContext ctx)
            => _instance != null ? new PresentationHandle(_instance.PlayData(data, in ctx)) : PresentationHandle.Invalid;

        // ── Handle 操作(PresentationHandle からも同じ内容を呼べる) ──

        public static void Signal(Handle<PresentationMarker> h, string key) => _instance?.Signal(h, key);

        public static void Cancel(Handle<PresentationMarker> h) => _instance?.Cancel(h);

        public static void Pause(Handle<PresentationMarker> h) => _instance?.SetPaused(h, true);

        public static void Resume(Handle<PresentationMarker> h) => _instance?.SetPaused(h, false);

        public static void SetSpeed(Handle<PresentationMarker> h, float speed) => _instance?.SetSpeed(h, speed);

        public static void Seek(Handle<PresentationMarker> h, float time) => _instance?.Seek(h, time);

        public static bool IsPlaying(Handle<PresentationMarker> h) => _instance?.IsPlaying(h) ?? false;

        public static float NormalizedTime(Handle<PresentationMarker> h) => _instance?.GetNormalizedTime(h) ?? -1f;

        public static Observable<Unit> OnCompleted(Handle<PresentationMarker> h)
            => _instance != null ? _instance.OnCompleted(h) : Observable.Empty<Unit>();

        public static Observable<Unit> OnCancelled(Handle<PresentationMarker> h)
            => _instance != null ? _instance.OnCancelled(h) : Observable.Empty<Unit>();

        public static Observable<string> OnMarker(Handle<PresentationMarker> h)
            => _instance != null ? _instance.OnMarker(h) : Observable.Empty<string>();

        public static Observable<PresentationTrack> OnTrackFired(Handle<PresentationMarker> h)
            => _instance != null ? _instance.OnTrackFired(h) : Observable.Empty<PresentationTrack>();

        public static UniTask WaitAsync(Handle<PresentationMarker> h, CancellationToken ct = default)
            => _instance != null ? _instance.WaitAsync(h, ct) : UniTask.CompletedTask;
    }
}
