using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Handle;
using R3;

namespace DDrive.Runtime.Presentation
{
    // [08_presentation.md] §3.5 — ゲームコードとの同期用 API。他種別(Vfx/Audio 等)は Handle<TMarker> +
    // 拡張メソッドで済ませているが、Presentation は Signal/Cancel/Pause 等の「制御」がここに集約されるため
    // 専用の readonly struct にした(中身は Handle<PresentationMarker> 1 個だけで GC alloc 0。実体は
    // 全て Presentation 静的ファサード→PresentationManager へ委譲する。ADR#3 と同じ考え方)。
    public readonly struct PresentationHandle : IEquatable<PresentationHandle>
    {
        public readonly Handle<PresentationMarker> Raw;

        internal PresentationHandle(Handle<PresentationMarker> raw) => Raw = raw;

        public static readonly PresentationHandle Invalid = new(Handle<PresentationMarker>.Invalid);

        public bool IsPlaying => Presentation.IsPlaying(Raw);

        // 0..1。無効なら -1(Anim/Vfx と同じ規約)。
        public float NormalizedTime => Presentation.NormalizedTime(Raw);

        public void Signal(string key) => Presentation.Signal(Raw, key);

        public void Cancel() => Presentation.Cancel(Raw);

        public void Pause() => Presentation.Pause(Raw);

        public void Resume() => Presentation.Resume(Raw);

        public void SetSpeed(float speed) => Presentation.SetSpeed(Raw, speed);

        public void Seek(float time) => Presentation.Seek(Raw, time);

        public Observable<Unit> OnCompleted => Presentation.OnCompleted(Raw);

        public Observable<Unit> OnCancelled => Presentation.OnCancelled(Raw);

        public Observable<string> OnMarker => Presentation.OnMarker(Raw);

        public Observable<PresentationTrack> OnTrackFired => Presentation.OnTrackFired(Raw);

        public UniTask WaitAsync(CancellationToken ct = default) => Presentation.WaitAsync(Raw, ct);

        public bool Equals(PresentationHandle other) => Raw.Equals(other.Raw);
        public override bool Equals(object obj) => obj is PresentationHandle other && Equals(other);
        public override int GetHashCode() => Raw.GetHashCode();
        public override string ToString() => $"PresentationHandle({Raw})";

        public static bool operator ==(PresentationHandle a, PresentationHandle b) => a.Equals(b);
        public static bool operator !=(PresentationHandle a, PresentationHandle b) => !a.Equals(b);
    }
}
