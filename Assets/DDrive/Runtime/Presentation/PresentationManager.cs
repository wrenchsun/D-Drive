using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Camera;
using DDrive.Runtime.Haptics;
using DDrive.Runtime.Ui;
using DDrive.Runtime.Vfx;
using R3;
using UnityEngine;
using PresentationId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Presentation.PresentationMarker>;

namespace DDrive.Runtime.Presentation
{
    // [08_presentation.md] §3 / [01_architecture.md] §8 — 「剣攻撃」等の演出データを 1 API で再生する
    // オーケストレータ(5-1)。自身は何も再生せず、Tracks を各 Manager(Audio/Vfx/Anim/Anim2D/Canvas/UiTween/
    // CameraFx/Haptics)へ委譲するだけ。Timeline のみ 6-10 待ちのため警告 1 回 + no-op。
    //
    // Tick は GameLoop 経由で TimeService.ScaledDeltaTime(unscaledDt) を受け取るため、HitStop 中は
    // (他の全 Manager 同様)AtTime の進行も自動的に止まる([16_camera_haptics.md] 参照。特別な配線は不要)。
    // CameraFxManager 自体は(このトラックの発火とは別に)Unscaled dt で駆動されるため、HitStop 中も
    // 揺れ自体は止まらない([16] Part A 実装メモ / DDriveRuntimeBootstrap の UnscaledCameraFxAdapter 参照)。
    public sealed class PresentationManager : IAssetManager
    {
        private sealed class PresentationInstance
        {
            public PresentationData Data;
            public PlayContext Ctx;
            public float Elapsed;
            public float Speed = 1f;
            public bool Paused;
            public bool Done;
            public bool[] Fired;

            public Subject<Unit> CompletedSubject;
            public Subject<Unit> CancelledSubject;
            public Subject<string> MarkerSubject;
            public Subject<PresentationTrack> TrackFiredSubject;

            // await Presentation.Play(...).WaitAsync() 用(UiTweenManager と同じ設計。UniTask.WaitUntil の
            // ポーリングに頼らず、Complete/Cancel 時に同期的に TrySetResult する)。
            public UniTaskCompletionSource Waiter;

            // StopOnCancel=true で発火した実体(Cancel 時にまとめて停止する。AssetEventDispatcher の
            // _keptVfx 等と同じ設計。型ごとに Handle の型が違うため個別リストに分ける)。
            public List<(int track, Handle<VfxMarker> handle)> FiredVfx;
            public List<(int track, Handle<SeMarker> handle)> FiredSe;
            public List<(int track, Handle<AnimMarker> handle)> FiredAnim;
            public List<(int track, Handle<UiTweenMarker> handle)> FiredUiTween;
            public List<(int track, Handle<CanvasMarker> handle)> FiredCanvas;
            public List<(int track, Handle<ShakeMarker> handle)> FiredShake;
            public List<(int track, Handle<HapticMarker> handle)> FiredHaptic;
        }

        private readonly IAssetRegistry _registry;
        private readonly TimeService _time;
        private readonly AudioManager _audio;
        private readonly BgmManager _bgm;
        private readonly VfxManager _vfx;
        private readonly AnimManager _anim;
        private readonly UiManager _ui;
        private readonly UiTweenManager _uiTween;
        private readonly CameraFxManager _cameraFx;
        private readonly HapticsManager _haptics;

        private readonly InstanceStore<PresentationMarker, PresentationInstance> _instances = new();
        private readonly List<Handle<PresentationMarker>> _active = new();
        private readonly HashSet<PresentationData> _nonInterruptibleWarned = new();
        private readonly HashSet<TrackKind> _unimplementedWarned = new();
        private readonly HashSet<TrackKind> _missingManagerWarned = new();

        public AssetType Type => AssetType.Presentation;

        // audio/bgm/vfx/anim/ui/uiTween は null 許容(未配線の種別トラックは警告 1 回 + no-op で継続する。
        // テストが必要な Manager だけを差し替えて構成できるようにするため)。
        public PresentationManager(
            IAssetRegistry registry,
            TimeService timeService,
            AudioManager audio = null,
            BgmManager bgm = null,
            VfxManager vfx = null,
            AnimManager anim = null,
            UiManager ui = null,
            UiTweenManager uiTween = null,
            CameraFxManager cameraFx = null,
            HapticsManager haptics = null)
        {
            _registry = registry;
            _time = timeService;
            _audio = audio;
            _bgm = bgm;
            _vfx = vfx;
            _anim = anim;
            _ui = ui;
            _uiTween = uiTween;
            _cameraFx = cameraFx;
            _haptics = haptics;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterPlaceholder()
        {
            PlaceholderProvider.Register(CreatePlaceholder);
        }

        // FR-1.4: 未登録 ID はトラック 0 個・尺 0 秒の Presentation(Play 直後の Tick で即完了)。
        private static PresentationData CreatePlaceholder()
        {
            var data = ScriptableObject.CreateInstance<PresentationData>();
            data.DisplayName = "<Placeholder:PRESENTATION>";
            data.Tracks = System.Array.Empty<PresentationTrack>();
            data.TotalDuration = 0f;
            data.Interruptible = true;
            return data;
        }

        // ── Play ──

        public Handle<PresentationMarker> Play(PresentationId id, in PlayContext ctx)
            => PlayData(_registry.ResolveOrPlaceholder<PresentationData>(id.Value), in ctx);

        public Handle<PresentationMarker> PlayData(PresentationData data, in PlayContext ctx)
        {
            if (data == null)
            {
                return Handle<PresentationMarker>.Invalid;
            }

            var instance = new PresentationInstance
            {
                Data = data,
                Ctx = ctx,
                Fired = data.Tracks != null ? new bool[data.Tracks.Length] : System.Array.Empty<bool>(),
                CompletedSubject = new Subject<Unit>(),
                CancelledSubject = new Subject<Unit>(),
                MarkerSubject = new Subject<string>(),
                TrackFiredSubject = new Subject<PresentationTrack>(),
                FiredVfx = new List<(int, Handle<VfxMarker>)>(),
                FiredSe = new List<(int, Handle<SeMarker>)>(),
                FiredAnim = new List<(int, Handle<AnimMarker>)>(),
                FiredUiTween = new List<(int, Handle<UiTweenMarker>)>(),
                FiredCanvas = new List<(int, Handle<CanvasMarker>)>(),
                FiredShake = new List<(int, Handle<ShakeMarker>)>(),
                FiredHaptic = new List<(int, Handle<HapticMarker>)>(),
            };

            var handle = _instances.Add(instance);

            // AtTime(0.00) は Play() 呼び出し時に即時委譲する([08] §3)。
            FireDueTracks(handle, instance);

            _active.Add(handle);
            return handle;
        }

        // ── Signal / Cancel / Pause など ──

        public void Signal(Handle<PresentationMarker> handle, string key)
        {
            if (string.IsNullOrEmpty(key) || !_instances.TryGet(handle, out var instance))
            {
                return;
            }

            var tracks = instance.Data.Tracks;
            if (tracks == null)
            {
                return;
            }

            for (var t = 0; t < tracks.Length; t++)
            {
                if (instance.Fired[t] || tracks[t].Trigger != TrackTrigger.OnSignal || tracks[t].SignalKey != key)
                {
                    continue;
                }

                FireTrack(handle, instance, t, in tracks[t]);
            }
        }

        public void Cancel(Handle<PresentationMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance) || instance.Done)
            {
                return;
            }

            if (!instance.Data.Interruptible)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (_nonInterruptibleWarned.Add(instance.Data))
                {
                    Debug.LogWarning($"[DDrive] Presentation '{instance.Data.DisplayName}' は Interruptible=false のため Cancel() を無視しました。");
                }
#endif
                return;
            }

            CancelInternal(handle, instance);
        }

        private void CancelInternal(Handle<PresentationMarker> handle, PresentationInstance instance)
        {
            instance.Done = true;
            StopFiredForCancel(instance);
            instance.CancelledSubject.OnNext(Unit.Default);
            instance.Waiter?.TrySetResult();
            Cleanup(handle, instance);
        }

        private void StopFiredForCancel(PresentationInstance instance)
        {
            for (var i = 0; i < instance.FiredVfx.Count; i++)
            {
                var h = instance.FiredVfx[i].handle;
                if (_vfx != null && _vfx.IsPlaying(h))
                {
                    _vfx.Stop(h);
                }
            }

            for (var i = 0; i < instance.FiredSe.Count; i++)
            {
                var h = instance.FiredSe[i].handle;
                if (_audio != null && _audio.IsPlaying(h))
                {
                    _audio.Stop(h);
                }
            }

            for (var i = 0; i < instance.FiredAnim.Count; i++)
            {
                var h = instance.FiredAnim[i].handle;
                if (_anim != null && _anim.IsPlaying(h))
                {
                    _anim.Stop(h);
                }
            }

            for (var i = 0; i < instance.FiredUiTween.Count; i++)
            {
                var h = instance.FiredUiTween[i].handle;
                if (_uiTween != null && _uiTween.IsPlaying(h))
                {
                    _uiTween.Stop(h);
                }
            }

            for (var i = 0; i < instance.FiredCanvas.Count; i++)
            {
                var h = instance.FiredCanvas[i].handle;
                if (_ui != null && _ui.IsOpen(h))
                {
                    _ui.Close(h);
                }
            }

            for (var i = 0; i < instance.FiredShake.Count; i++)
            {
                var h = instance.FiredShake[i].handle;
                if (_cameraFx != null && _cameraFx.IsPlaying(h))
                {
                    _cameraFx.Stop(h, 0f);
                }
            }

            for (var i = 0; i < instance.FiredHaptic.Count; i++)
            {
                var h = instance.FiredHaptic[i].handle;
                if (_haptics != null && _haptics.IsPlaying(h))
                {
                    _haptics.Stop(h);
                }
            }
        }

        public void SetPaused(Handle<PresentationMarker> handle, bool paused)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                instance.Paused = paused;
            }
        }

        public void SetSpeed(Handle<PresentationMarker> handle, float speed)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                instance.Speed = Mathf.Max(0f, speed);
            }
        }

        // デバッグ/スキップ用。通過したトラックはまとめて発火する。巻き戻し(過去への Seek)は
        // 既発火のトラックを再発火しない(Fired は保持したまま)。
        public void Seek(Handle<PresentationMarker> handle, float time)
        {
            if (!_instances.TryGet(handle, out var instance))
            {
                return;
            }

            instance.Elapsed = Mathf.Max(0f, time);
            FireDueTracks(handle, instance);
        }

        // ── 問い合わせ ──

        // 終了済み Handle の問い合わせは正常系(ポーリング/WaitAsync)なので警告を出さない。
        public bool IsPlaying(Handle<PresentationMarker> handle) => _instances.IsValidSilent(handle);

        public float GetNormalizedTime(Handle<PresentationMarker> handle)
        {
            if (!TryGetInstanceSilent(handle, out var instance))
            {
                return -1f;
            }

            var duration = PresentationTiming.EffectiveDuration(instance.Data);
            return duration > 0f ? Mathf.Clamp01(instance.Elapsed / duration) : 0f;
        }

        public Observable<Unit> OnCompleted(Handle<PresentationMarker> handle)
            => TryGetInstanceSilent(handle, out var instance) ? instance.CompletedSubject : Observable.Empty<Unit>();

        public Observable<Unit> OnCancelled(Handle<PresentationMarker> handle)
            => TryGetInstanceSilent(handle, out var instance) ? instance.CancelledSubject : Observable.Empty<Unit>();

        public Observable<string> OnMarker(Handle<PresentationMarker> handle)
            => TryGetInstanceSilent(handle, out var instance) ? instance.MarkerSubject : Observable.Empty<string>();

        public Observable<PresentationTrack> OnTrackFired(Handle<PresentationMarker> handle)
            => TryGetInstanceSilent(handle, out var instance) ? instance.TrackFiredSubject : Observable.Empty<PresentationTrack>();

        public UniTask WaitAsync(Handle<PresentationMarker> handle, CancellationToken ct)
        {
            if (!TryGetInstanceSilent(handle, out var instance))
            {
                return UniTask.CompletedTask;
            }

            if (instance.Waiter == null)
            {
                instance.Waiter = new UniTaskCompletionSource();
                if (ct.CanBeCanceled)
                {
                    var waiter = instance.Waiter;
                    ct.Register(() => waiter.TrySetCanceled(ct));
                }
            }

            return instance.Waiter.Task;
        }

        private bool TryGetInstanceSilent(Handle<PresentationMarker> handle, out PresentationInstance instance)
        {
            if (_instances.IsValidSilent(handle))
            {
                return _instances.TryGet(handle, out instance);
            }

            instance = null;
            return false;
        }

        // ── Tick / Pause / StopAll ──

        public void Tick(float dt)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var handle = _active[i];
                if (!_instances.TryGet(handle, out var instance))
                {
                    _active.RemoveAt(i);
                    continue;
                }

                if (instance.Paused)
                {
                    continue;
                }

                instance.Elapsed += dt * instance.Speed;
                FireDueTracks(handle, instance);

                if (!instance.Done && instance.Elapsed >= PresentationTiming.EffectiveDuration(instance.Data))
                {
                    Complete(handle, instance);
                }
            }
        }

        private void Complete(Handle<PresentationMarker> handle, PresentationInstance instance)
        {
            instance.Done = true;
            instance.CompletedSubject.OnNext(Unit.Default);
            instance.Waiter?.TrySetResult();
            Cleanup(handle, instance);
        }

        private void Cleanup(Handle<PresentationMarker> handle, PresentationInstance instance)
        {
            _active.Remove(handle);
            _instances.Remove(handle);
            instance.CompletedSubject.Dispose();
            instance.CancelledSubject.Dispose();
            instance.MarkerSubject.Dispose();
            instance.TrackFiredSubject.Dispose();
        }

        public void OnPause(PauseChannel channel, bool paused) => ApplyPause(paused, respectFlags: true);

        private void ApplyPause(bool paused, bool respectFlags)
        {
            for (var i = 0; i < _active.Count; i++)
            {
                if (!_instances.TryGet(_active[i], out var instance))
                {
                    continue;
                }

                if (respectFlags && instance.Data.Flags.Pause != PauseMode.PauseWithGame)
                {
                    continue;
                }

                instance.Paused = paused;
            }
        }

        // シーン破棄等の強制停止。Interruptible=false でも止める(通常の Cancel() とは別経路)。
        public void StopAll(StopReason reason)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                if (_instances.TryGet(_active[i], out var instance) && !instance.Done)
                {
                    CancelInternal(_active[i], instance);
                }
                else
                {
                    _active.RemoveAt(i);
                }
            }
        }

        public void OnSceneUnload() => StopAll(StopReason.SceneUnload);

        // ── トラック発火 ──

        private static Transform ResolveContextRoot(in PlayContext ctx, TrackTargetMode mode)
        {
            switch (mode)
            {
                case TrackTargetMode.Self:
                    return ctx.Self;
                case TrackTargetMode.ContextTarget:
                    return ctx.Target;
                default:
                    // World / Anchor: PlayContext を参照せず、Anchor.LocalOffset を絶対ワールド座標として使う
                    // (要判断: [08_presentation.md] 実装メモ参照。現状は両者を区別していない)。
                    return null;
            }
        }

        private void FireDueTracks(Handle<PresentationMarker> handle, PresentationInstance instance)
        {
            var tracks = instance.Data.Tracks;
            if (tracks == null)
            {
                return;
            }

            for (var t = 0; t < tracks.Length; t++)
            {
                if (instance.Fired[t] || tracks[t].Trigger != TrackTrigger.AtTime || tracks[t].Time > instance.Elapsed)
                {
                    continue;
                }

                FireTrack(handle, instance, t, in tracks[t]);
            }
        }

        private void FireTrack(Handle<PresentationMarker> handle, PresentationInstance instance, int trackIndex, in PresentationTrack track)
        {
            instance.Fired[trackIndex] = true;

            switch (track.Kind)
            {
                case TrackKind.Anim:
                case TrackKind.Anim2D:
                    FireAnim(instance, trackIndex, in track);
                    break;

                case TrackKind.Se:
                    FireSe(instance, trackIndex, in track);
                    break;

                case TrackKind.Bgm:
                    FireBgm(in track);
                    break;

                case TrackKind.Vfx:
                    FireVfx(instance, trackIndex, in track);
                    break;

                case TrackKind.Canvas:
                    FireCanvas(instance, trackIndex, in track);
                    break;

                case TrackKind.UiTween:
                    FireUiTween(instance, trackIndex, in track);
                    break;

                case TrackKind.HitStop:
                    FireHitStop(in track);
                    break;

                case TrackKind.Marker:
                    instance.MarkerSubject.OnNext(track.SignalKey);
                    break;

                case TrackKind.Signal:
                    instance.Ctx.OnSignal?.Invoke(track.SignalKey);
                    break;

                case TrackKind.CameraShake:
                    FireCameraShake(instance, trackIndex, in track);
                    break;

                case TrackKind.Haptic:
                    FireHaptic(instance, trackIndex, in track);
                    break;

                case TrackKind.Timeline:
                    WarnUnimplemented(track.Kind);
                    break;
            }

            instance.TrackFiredSubject.OnNext(track);
        }

        private void FireVfx(PresentationInstance instance, int trackIndex, in PresentationTrack track)
        {
            if (_vfx == null)
            {
                WarnMissingManager(TrackKind.Vfx);
                return;
            }

            var data = _registry.ResolveOrPlaceholder<VfxData>(track.Asset.Id);
            var root = ResolveContextRoot(instance.Ctx, track.Target);
            var spec = AnchorSpawnSpec.FromDef(track.Anchor);
            var h = _vfx.SpawnData(data, in spec, root);

            if (track.StopOnCancel && _vfx.IsPlaying(h))
            {
                instance.FiredVfx.Add((trackIndex, h));
            }
        }

        private void FireSe(PresentationInstance instance, int trackIndex, in PresentationTrack track)
        {
            if (_audio == null)
            {
                WarnMissingManager(TrackKind.Se);
                return;
            }

            var data = _registry.ResolveOrPlaceholder<SeData>(track.Asset.Id);
            var root = ResolveContextRoot(instance.Ctx, track.Target);
            var spec = AnchorSpawnSpec.FromDef(track.Anchor);
            var h = _audio.PlaySeData(data, in spec, root);

            if (track.StopOnCancel && _audio.IsPlaying(h))
            {
                instance.FiredSe.Add((trackIndex, h));
            }
        }

        private void FireBgm(in PresentationTrack track)
        {
            if (_bgm == null)
            {
                WarnMissingManager(TrackKind.Bgm);
                return;
            }

            var data = _registry.ResolveOrPlaceholder<BgmData>(track.Asset.Id);
            var fadeIn = track.Params != null && track.Params.Length > 0 ? track.Params[0].FloatValue : -1f;
            _bgm.PlayBgmData(data, fadeIn);
        }

        private void FireAnim(PresentationInstance instance, int trackIndex, in PresentationTrack track)
        {
            if (_anim == null)
            {
                WarnMissingManager(track.Kind);
                return;
            }

            var root = ResolveContextRoot(instance.Ctx, track.Target);
            var animator = root != null ? root.GetComponentInChildren<Animator>(true) : null;
            if (animator == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning($"[DDrive] Presentation '{instance.Data.DisplayName}': track {trackIndex}({track.Kind}) に Animator が見つかりません(Target={track.Target})。");
#endif
                return;
            }

            var data = _registry.ResolveOrPlaceholder<AnimData>(track.Asset.Id);
            var h = _anim.PlayData(data, animator);

            if (track.StopOnCancel && _anim.IsPlaying(h))
            {
                instance.FiredAnim.Add((trackIndex, h));
            }
        }

        private void FireCanvas(PresentationInstance instance, int trackIndex, in PresentationTrack track)
        {
            if (_ui == null)
            {
                WarnMissingManager(TrackKind.Canvas);
                return;
            }

            var id = new AssetId<CanvasMarker>(track.Asset.Id, AssetType.Canvas);
            var h = _ui.Open(id);

            if (track.StopOnCancel && _ui.IsOpen(h))
            {
                instance.FiredCanvas.Add((trackIndex, h));
            }
        }

        private void FireUiTween(PresentationInstance instance, int trackIndex, in PresentationTrack track)
        {
            if (_uiTween == null)
            {
                WarnMissingManager(TrackKind.UiTween);
                return;
            }

            var root = ResolveContextRoot(instance.Ctx, track.Target);
            var rect = root as RectTransform;
            if (rect == null && root != null)
            {
                rect = root.GetComponentInChildren<RectTransform>(true);
            }

            if (rect == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning($"[DDrive] Presentation '{instance.Data.DisplayName}': track {trackIndex}(UiTween) に RectTransform が見つかりません(Target={track.Target})。");
#endif
                return;
            }

            var data = _registry.ResolveOrPlaceholder<UiTweenData>(track.Asset.Id);
            var h = _uiTween.PlayData(data, rect);

            if (track.StopOnCancel && _uiTween.IsPlaying(h))
            {
                instance.FiredUiTween.Add((trackIndex, h));
            }
        }

        // sourcePos には ctx.Position を渡す(ShakeSpace.FromSource 用。CameraLocal/World は無視するので常に渡してよい)。
        private void FireCameraShake(PresentationInstance instance, int trackIndex, in PresentationTrack track)
        {
            if (_cameraFx == null)
            {
                WarnMissingManager(TrackKind.CameraShake);
                return;
            }

            var data = _registry.ResolveOrPlaceholder<CameraShakeData>(track.Asset.Id);
            var h = _cameraFx.ShakeData(data, instance.Ctx.Position);

            if (track.StopOnCancel && _cameraFx.IsPlaying(h))
            {
                instance.FiredShake.Add((trackIndex, h));
            }
        }

        private void FireHaptic(PresentationInstance instance, int trackIndex, in PresentationTrack track)
        {
            if (_haptics == null)
            {
                WarnMissingManager(TrackKind.Haptic);
                return;
            }

            var data = _registry.ResolveOrPlaceholder<HapticsData>(track.Asset.Id);
            var h = _haptics.PlayData(data);

            if (track.StopOnCancel && _haptics.IsPlaying(h))
            {
                instance.FiredHaptic.Add((trackIndex, h));
            }
        }

        private void FireHitStop(in PresentationTrack track)
        {
            if (_time == null)
            {
                WarnMissingManager(TrackKind.HitStop);
                return;
            }

            if (track.Params == null || track.Params.Length == 0)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning("[DDrive] Presentation: HitStop トラックに Params[0](秒数)が設定されていません。");
#endif
                return;
            }

            _time.HitStop(track.Params[0].FloatValue);
        }

        private void WarnUnimplemented(TrackKind kind)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (_unimplementedWarned.Add(kind))
            {
                Debug.LogWarning($"[DDrive] Presentation: TrackKind.{kind} は未実装です(5-2/5-2b/6-10 で対応予定)。no-op で継続します。");
            }
#endif
        }

        private void WarnMissingManager(TrackKind kind)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (_missingManagerWarned.Add(kind))
            {
                Debug.LogWarning($"[DDrive] Presentation: TrackKind.{kind} を委譲する Manager が未設定です。no-op で継続します。");
            }
#endif
        }
    }
}
