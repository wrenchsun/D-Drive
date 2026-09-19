using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Handle;
using DDrive.Runtime.Presentation;
using R3;
using CutsceneId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Cutscene.CutsceneMarker>;

namespace DDrive.Runtime.Cutscene
{
    // デザイナー/プログラマー向けの薄い静的ファサード(Audio.cs / Presentation.cs と同じ設計。ADR#3)。
    // 未 Bind の呼び出しは全て no-op(Invalid Handle)で継続する(例外で止めない)。
    //
    //   Cutscene.Play(CUTID.Opening, ctx) の 1 行で Timeline カットシーンが再生できる([26] §4.5)。
    public static class Cutscene
    {
        private static CutsceneManager _instance;

        public static void Bind(CutsceneManager instance) => _instance = instance;

        public static bool IsBound => _instance != null;

        public static CutsceneHandle Play(CutsceneId id, in PlayContext ctx)
            => _instance != null ? new CutsceneHandle(_instance.Play(id, in ctx)) : CutsceneHandle.Invalid;

        public static CutsceneHandle PlayData(CutsceneData data, in PlayContext ctx)
            => _instance != null ? new CutsceneHandle(_instance.PlayData(data, in ctx)) : CutsceneHandle.Invalid;

        // ── Handle 操作(CutsceneHandle からも同じ内容を呼べる) ──

        public static void Cancel(Handle<CutsceneMarker> h) => _instance?.Cancel(h);

        public static void Skip(Handle<CutsceneMarker> h) => _instance?.Skip(h);

        public static void Pause(Handle<CutsceneMarker> h) => _instance?.SetPaused(h, true);

        public static void Resume(Handle<CutsceneMarker> h) => _instance?.SetPaused(h, false);

        public static void SetSpeed(Handle<CutsceneMarker> h, float speed) => _instance?.SetSpeed(h, speed);

        public static void Seek(Handle<CutsceneMarker> h, float time) => _instance?.Seek(h, time);

        public static bool IsPlaying(Handle<CutsceneMarker> h) => _instance?.IsPlaying(h) ?? false;

        // CutsceneHandle.IsInputLocked から呼ばれる Handle 単位の判定(静的プロパティ Cutscene.IsInputLocked
        // は「1 つでも再生中か」の集計のため名前が衝突する。C# はメソッド/プロパティを同名で共存できないため分ける)。
        public static bool IsHandleInputLocked(Handle<CutsceneMarker> h) => _instance?.IsInputLocked(h) ?? false;

        public static float NormalizedTime(Handle<CutsceneMarker> h) => _instance?.GetNormalizedTime(h) ?? -1f;

        public static Observable<Unit> OnCompleted(Handle<CutsceneMarker> h)
            => _instance != null ? _instance.OnCompleted(h) : Observable.Empty<Unit>();

        public static Observable<Unit> OnCancelled(Handle<CutsceneMarker> h)
            => _instance != null ? _instance.OnCancelled(h) : Observable.Empty<Unit>();

        public static Observable<string> OnMarker(Handle<CutsceneMarker> h)
            => _instance != null ? _instance.OnMarker(h) : Observable.Empty<string>();

        public static UniTask WaitAsync(Handle<CutsceneMarker> h, CancellationToken ct = default)
            => _instance != null ? _instance.WaitAsync(h, ct) : UniTask.CompletedTask;

        // [26_timeline.md] §4.5.1 — 「LockInput な Cutscene が 1 つでも再生中か」。未 Bind なら常に false。
        public static bool IsInputLocked => _instance?.IsInputLockedAny ?? false;

        public static Observable<bool> OnInputLockChanged
            => _instance != null ? _instance.OnInputLockChanged : Observable.Empty<bool>();
    }
}
