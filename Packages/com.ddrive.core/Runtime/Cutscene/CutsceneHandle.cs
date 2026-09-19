using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Handle;
using R3;

namespace DDrive.Runtime.Cutscene
{
    // [26_timeline.md] §4.5 — Presentation と同じ API 形(Cancel/Skip/Pause/Resume/SetSpeed/Seek/IsPlaying/
    // NormalizedTime/OnCompleted/OnCancelled/OnMarker/WaitAsync)。**Signal は持たない**
    // (Cutscene は「コードの合図を待つ」トラックを持たない。[26] §3.1/§4.5)。
    public readonly struct CutsceneHandle : IEquatable<CutsceneHandle>
    {
        public readonly Handle<CutsceneMarker> Raw;

        internal CutsceneHandle(Handle<CutsceneMarker> raw) => Raw = raw;

        public static readonly CutsceneHandle Invalid = new(Handle<CutsceneMarker>.Invalid);

        public bool IsPlaying => Cutscene.IsPlaying(Raw);

        public float NormalizedTime => Cutscene.NormalizedTime(Raw);

        // [26] §4.5.1 — Data.LockInput && IsPlaying。実際に入力を止めるのはゲーム側の責務。
        public bool IsInputLocked => Cutscene.IsHandleInputLocked(Raw);

        public void Cancel() => Cutscene.Cancel(Raw);

        public void Skip() => Cutscene.Skip(Raw);

        public void Pause() => Cutscene.Pause(Raw);

        public void Resume() => Cutscene.Resume(Raw);

        public void SetSpeed(float speed) => Cutscene.SetSpeed(Raw, speed);

        public void Seek(float time) => Cutscene.Seek(Raw, time);

        public Observable<Unit> OnCompleted => Cutscene.OnCompleted(Raw);

        public Observable<Unit> OnCancelled => Cutscene.OnCancelled(Raw);

        public Observable<string> OnMarker => Cutscene.OnMarker(Raw);

        public UniTask WaitAsync(CancellationToken ct = default) => Cutscene.WaitAsync(Raw, ct);

        public bool Equals(CutsceneHandle other) => Raw.Equals(other.Raw);
        public override bool Equals(object obj) => obj is CutsceneHandle other && Equals(other);
        public override int GetHashCode() => Raw.GetHashCode();
        public override string ToString() => $"CutsceneHandle({Raw})";

        public static bool operator ==(CutsceneHandle a, CutsceneHandle b) => a.Equals(b);
        public static bool operator !=(CutsceneHandle a, CutsceneHandle b) => !a.Equals(b);
    }
}
