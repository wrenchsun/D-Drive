using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Data;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Values;
using UnityEngine;
using UnityEngine.UI;
using UiTweenId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Ui.UiTweenMarker>;

namespace DDrive.Runtime.Ui
{
    // [15_ui_interaction.md] B-3/B-5 — チケット 4-8。構造体ベース・0 alloc の Tween エンジン。
    //
    // 0 alloc 方針([12_review.md] §3。Tick 内で LINQ・クロージャ・boxing 禁止):
    //  - Instance は class だが Play のたびに new せず、フリーリスト(_pool)から使い回す
    //  - 1 Instance あたりの Track バッファ(OwnedTracks/States)は固定長(MaxTracksPerTween)の配列を
    //    Instance 生成時に 1 回だけ確保し、以後は上書きして再利用する
    //  - Tick は for ループのみで走査し、新しい配列・デリゲート・ボックス化を一切発生させない
    //  - SplinePath の構築(弧長テーブル計算)は Play() 時(Tick 外)にのみ行う
    public sealed class UiTweenManager : IAssetManager
    {
        public const int MaxTracksPerTween = 8;

        private struct TrackRuntime
        {
            public ParamValue From;
            public ParamValue To;
            public SplinePath Path;
        }

        private sealed class TweenInstance
        {
            public RectTransform Target;
            public CanvasGroup Group;
            public Graphic Graphic;
            public Image Image;

            public TweenTrack[] OwnedTracks;
            public TrackRuntime[] States;
            public TweenTrack[] Tracks; // Data.Tracks への参照、または OwnedTracks
            public int TrackCount;

            public float Elapsed;
            public float Speed = 1f;
            public bool UseScaledTime;
            public bool PauseWithGame;
            public bool Paused;
            public float ResolvedDuration;

            public UniTaskCompletionSource Waiter;
        }

        private readonly IAssetRegistry _registry;
        private readonly InstanceStore<UiTweenMarker, TweenInstance> _instances = new();
        private readonly List<Handle<UiTweenMarker>> _active = new();
        private readonly Stack<TweenInstance> _pool = new();
        // 警告済みの Data(インスタンス単位。以前は static で ScriptableObject を保持し続けていた。docs/24 整理項目 6、2026-09-14)。
        private readonly HashSet<UiTweenData> _placeholderWarned = new();

        // PlayPreset 用。PlayTracks が OwnedTracks へコピーするため、この呼び出し内だけで使い回せる(定常経路で alloc しない)。
        private static readonly TweenTrack[] PresetScratch = new TweenTrack[MaxTracksPerTween];

        public AssetType Type => AssetType.UiTween;

        // 現在再生中の Tween 数(テスト・デバッグ表示用)。
        public int ActiveCount => _active.Count;

        // [18_ui_controls.md] B-4 — OptionKey.UiSpeedScale が反映する全体速度倍率(既定 1=等速)。
        // Instance.Speed(個別倍率)と掛け合わせて Tick の effectiveDt に使う。
        public float GlobalSpeed = 1f;

        public UiTweenManager(IAssetRegistry registry)
        {
            _registry = registry;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterPlaceholder() => PlaceholderProvider.Register(CreatePlaceholder);

        // FR-1.4: 未登録の UiTweenId は「トラック無し・0.25秒で即完了」のプレースホルダで代替する。
        private static UiTweenData CreatePlaceholder()
        {
            var data = ScriptableObject.CreateInstance<UiTweenData>();
            data.DisplayName = "<Placeholder:UITWEEN>";
            data.Tracks = Array.Empty<TweenTrack>();
            data.TotalDuration = 0.25f;
            return data;
        }

        // ── Play ──

        public Handle<UiTweenMarker> Play(UiTweenId id, RectTransform target)
            => PlayData(_registry.ResolveOrPlaceholder<UiTweenData>(id.Value), target);

        public Handle<UiTweenMarker> PlayData(UiTweenData data, RectTransform target)
        {
            if (target == null)
            {
                return Handle<UiTweenMarker>.Invalid;
            }

            if (data == null)
            {
                data = CreatePlaceholder();
            }

            if (data.Tracks == null || data.Tracks.Length == 0)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (_placeholderWarned.Add(data))
                {
                    Debug.LogWarning($"[DDrive] UiTweenData '{data.DisplayName}' に Tracks がありません。何もせず即完了します。");
                }
#endif
            }

            var inst = RentInstance();
            inst.Target = target;
            inst.Tracks = data.Tracks ?? Array.Empty<TweenTrack>();
            inst.TrackCount = Mathf.Min(inst.Tracks.Length, MaxTracksPerTween);
            inst.PauseWithGame = data.Flags.Pause == PauseMode.PauseWithGame;
            ResolveComponents(inst);
            PrepareTracks(inst);

            var handle = _instances.Add(inst);
            _active.Add(handle);

            if (inst.TrackCount == 0)
            {
                CompleteInstance(handle, inst);
            }

            return handle;
        }

        // UiPresetRef → TweenTrack[] へ展開して再生する。UiFx.Play と UiManager の ElementFx が同じ手順を別々に持っていた
        // のを集約した(docs/24 整理項目 1、2026-09-14)。SE があれば同時に鳴らす。
        public Handle<UiTweenMarker> PlayPreset(in UiPresetRef p, RectTransform target)
        {
            if (target == null || p.Preset == UiPreset.None)
            {
                return Handle<UiTweenMarker>.Invalid;
            }

            var count = UiPresetFactory.Build(in p, target, PresetScratch);
            if (count <= 0)
            {
                return Handle<UiTweenMarker>.Invalid;
            }

            if (p.Se.IsValid)
            {
                Audio.Audio.PlaySe(p.Se);
            }

            return PlayTracks(PresetScratch, count, target);
        }

        public Handle<UiTweenMarker> PlayTracks(TweenTrack[] tracks, int count, RectTransform target)
        {
            if (target == null || tracks == null || count <= 0)
            {
                return Handle<UiTweenMarker>.Invalid;
            }

            var inst = RentInstance();
            inst.Target = target;
            // Codex レビュー対応(2026-09-11): MaxTracksPerTween だけでなく tracks.Length にもクランプする
            // (呼び出し側が実際のバッファ長より大きい count を渡すと Array.Copy が例外になっていた)。
            count = Mathf.Min(count, Mathf.Min(MaxTracksPerTween, tracks.Length));
            Array.Copy(tracks, inst.OwnedTracks, count);
            inst.Tracks = inst.OwnedTracks;
            inst.TrackCount = count;
            ResolveComponents(inst);
            PrepareTracks(inst);

            var handle = _instances.Add(inst);
            _active.Add(handle);
            return handle;
        }

        public Handle<UiTweenMarker> PlayTracks(ReadOnlySpan<TweenTrack> tracks, RectTransform target)
        {
            if (target == null || tracks.Length == 0)
            {
                return Handle<UiTweenMarker>.Invalid;
            }

            var inst = RentInstance();
            inst.Target = target;
            var count = Mathf.Min(tracks.Length, MaxTracksPerTween);
            for (var i = 0; i < count; i++)
            {
                inst.OwnedTracks[i] = tracks[i];
            }

            inst.Tracks = inst.OwnedTracks;
            inst.TrackCount = count;
            ResolveComponents(inst);
            PrepareTracks(inst);

            var handle = _instances.Add(inst);
            _active.Add(handle);
            return handle;
        }

        // ── アドホック ──

        public Handle<UiTweenMarker> MoveTo(RectTransform target, Vector2 to, float sec, Ease ease = Ease.OutCubic)
            => PlaySingleTrack(target, new TweenTrack
            {
                Property = TweenProperty.AnchoredPosition,
                Motion = BuildMotion(sec, ease),
                From = TweenFromMode.Current,
                ToValue = VecParam(to.x, to.y),
            });

        public Handle<UiTweenMarker> Scale(RectTransform target, float to, float sec, Ease ease = Ease.OutBack)
            => PlaySingleTrack(target, new TweenTrack
            {
                Property = TweenProperty.Scale,
                Motion = BuildMotion(sec, ease),
                From = TweenFromMode.Current,
                ToValue = VecParam(to, to, to),
            });

        public Handle<UiTweenMarker> Fade(CanvasGroup group, float to, float sec, Ease ease = Ease.Linear)
        {
            if (group == null)
            {
                return Handle<UiTweenMarker>.Invalid;
            }

            var rt = group.GetComponent<RectTransform>();
            return PlaySingleTrack(rt, new TweenTrack
            {
                Property = TweenProperty.Alpha,
                Motion = BuildMotion(sec, ease),
                From = TweenFromMode.Current,
                ToValue = ParamValue.Of(to),
            });
        }

        public Handle<UiTweenMarker> Rotate(RectTransform target, float toZ, float sec, Ease ease = Ease.OutCubic)
            => PlaySingleTrack(target, new TweenTrack
            {
                Property = TweenProperty.Rotation,
                Motion = BuildMotion(sec, ease),
                From = TweenFromMode.Current,
                ToValue = ParamValue.Of(toZ),
            });

        // 既に構築済みの SplinePath をそのまま使う(データ化されていないその場限りの経路移動向け)。
        public Handle<UiTweenMarker> MoveAlong(RectTransform target, SplinePath path, float sec, Ease ease = Ease.Linear)
        {
            if (target == null || path == null)
            {
                return Handle<UiTweenMarker>.Invalid;
            }

            var inst = RentInstance();
            inst.Target = target;
            inst.OwnedTracks[0] = new TweenTrack { Property = TweenProperty.PathMove, Motion = BuildMotion(sec, ease) };
            inst.Tracks = inst.OwnedTracks;
            inst.TrackCount = 1;
            ResolveComponents(inst);
            PrepareTracks(inst);
            inst.States[0].Path = path; // Def からの再構築をスキップして直接差し替える

            var handle = _instances.Add(inst);
            _active.Add(handle);
            return handle;
        }

        private Handle<UiTweenMarker> PlaySingleTrack(RectTransform target, in TweenTrack track)
        {
            if (target == null)
            {
                return Handle<UiTweenMarker>.Invalid;
            }

            var inst = RentInstance();
            inst.Target = target;
            inst.OwnedTracks[0] = track;
            inst.Tracks = inst.OwnedTracks;
            inst.TrackCount = 1;
            ResolveComponents(inst);
            PrepareTracks(inst);

            var handle = _instances.Add(inst);
            _active.Add(handle);
            return handle;
        }

        private static ValueDef BuildMotion(float sec, Ease ease) => new()
        {
            Mode = ValueMode.Parametric,
            Parametric = EaseDef.Named(ease),
            Time = TimeDef.Duration(Mathf.Max(0.0001f, sec)),
            Loop = LoopMode.Once,
        };

        // ── 問い合わせ / 操作 ──

        // 終了済み Handle への問い合わせは正常系(ポーリング)なので警告を出さない([04_vfx.md] IsPlaying と同じ規則)。
        public bool IsPlaying(Handle<UiTweenMarker> handle) => _instances.IsValidSilent(handle);

        public float Progress(Handle<UiTweenMarker> handle)
        {
            if (!_instances.TryGet(handle, out var inst) || inst.ResolvedDuration <= 0f)
            {
                return _instances.IsValidSilent(handle) ? 1f : 0f;
            }

            return Mathf.Clamp01(inst.Elapsed / inst.ResolvedDuration);
        }

        public UniTask WaitAsync(Handle<UiTweenMarker> handle)
        {
            if (!_instances.TryGet(handle, out var inst))
            {
                return UniTask.CompletedTask;
            }

            inst.Waiter ??= new UniTaskCompletionSource();
            return inst.Waiter.Task;
        }

        public void SetSpeed(Handle<UiTweenMarker> handle, float speed)
        {
            if (_instances.TryGet(handle, out var inst))
            {
                inst.Speed = Mathf.Max(0f, speed);
            }
        }

        public void SetUseScaledTime(Handle<UiTweenMarker> handle, bool useScaledTime)
        {
            if (_instances.TryGet(handle, out var inst))
            {
                inst.UseScaledTime = useScaledTime;
            }
        }

        // Handle 単位の一時停止(AnimManager.SetPaused と同じ設計。エディタのプレビュー用)。ゲームのポーズ(OnPause)と
        // 同じ Paused フラグを使うため独立ではない: PauseWithGame の Tween はゲームのポーズ解除でこの一時停止も解ける。
        public void SetPaused(Handle<UiTweenMarker> handle, bool paused)
        {
            if (_instances.TryGet(handle, out var inst))
            {
                inst.Paused = paused;
            }
        }

        public bool IsPaused(Handle<UiTweenMarker> handle) => _instances.TryGet(handle, out var inst) && inst.Paused;

        // complete=true は最終状態(shape=1)へ即座に反映してから終了する。
        // [M-1c、2026-09-25] 冪等操作なので TryGetQuiet で警告なしにガードする。
        public void Stop(Handle<UiTweenMarker> handle, bool complete = false)
        {
            if (!_instances.TryGetQuiet(handle, out var inst))
            {
                return;
            }

            // Codex レビュー対応(2026-09-11): Target が(シーン破棄等で)既に破棄されている可能性がある。
            // ApplyTrack は Target のコンポーネントへ直接書き込むため、null なら complete 分岐をスキップする。
            if (complete && inst.Target != null)
            {
                for (var i = 0; i < inst.TrackCount; i++)
                {
                    ApplyTrack(inst, inst.Tracks[i], ref inst.States[i], 1f);
                }
            }

            CompleteInstance(handle, inst);
        }

        // U-23(2026-09-17) バグ修正: 以前は complete 引数が無く、中断された Tween を最終値へ進めずに
        // 取り除いていた。呼び出し側(例: CanvasEditorWindow.PlayPhasePreview の ElementFx「▶ 再生」)が
        // StopAll の直後に UiPresetFactory.Build で target の "現在位置" を新しい Tween の基準(cur)として
        // 読み直すため、完了前に連打すると毎回「中断された時点の位置」が新しい静止位置として採用されてしまい、
        // 連打するたびに本来の位置からずれていく不具合があった(Stop(handle, complete:true) と同じ規則に揃える)。
        // 既存の呼び出し元との互換性のため既定値は false のまま(Stop(handle, complete=false) と同じ規約)。
        public void StopAll(RectTransform target, bool complete = false)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                if (_instances.TryGet(_active[i], out var inst) && inst.Target == target)
                {
                    Stop(_active[i], complete);
                }
            }
        }

        // ── Tick / Pause / StopAll(IAssetManager) ──

        public void Tick(float dt)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var handle = _active[i];
                if (!_instances.TryGet(handle, out var inst))
                {
                    _active.RemoveAt(i);
                    continue;
                }

                if (inst.Target == null)
                {
                    CompleteInstance(handle, inst);
                    continue;
                }

                if (inst.Paused)
                {
                    continue;
                }

                // 実装メモ(2026-09-11): GameLoopDriver は現状 IAssetManager.Tick へ単一の dt(TimeService で
                // 既にスケール済み)しか渡さないため、Tick(dt) は他の Manager と同じくこの dt をそのまま使う
                // (VfxManager 等と同じ規則。EditMode/PlayMode テストが manager.Tick(dt) を直接叩いて決定的に
                // 検証できることを優先する)。UseScaledTime は「将来 GameLoopDriver が scaled/unscaled の
                // 2 系統の dt を配るようになったときにどちらを使うか」を Instance 単位で選べるようにする
                // ための予約フラグで、現時点では実効値に差は無い(deferred。docs/15 実装メモ参照)。
                var effectiveDt = dt * inst.Speed * GlobalSpeed;
                inst.Elapsed += effectiveDt;

                var allDone = true;
                for (var k = 0; k < inst.TrackCount; k++)
                {
                    ref var track = ref inst.Tracks[k];
                    ref var state = ref inst.States[k];
                    var trackElapsed = inst.Elapsed - track.Delay;
                    if (trackElapsed < 0f)
                    {
                        allDone = false;
                        continue;
                    }

                    var t01 = track.Motion.ResolveNormalizedT(trackElapsed);
                    var shape = EvaluateShape(track.Motion, t01);
                    ApplyTrack(inst, track, ref state, shape);

                    if (!IsTrackFinished(track, trackElapsed))
                    {
                        allDone = false;
                    }
                }

                if (allDone)
                {
                    CompleteInstance(handle, inst);
                }
            }
        }

        private static bool IsTrackFinished(in TweenTrack track, float trackElapsed)
        {
            if (track.Motion.Loop == LoopMode.Once)
            {
                return trackElapsed >= track.Motion.Duration;
            }

            if (track.Motion.LoopCount > 0)
            {
                return trackElapsed >= track.Motion.Duration * track.Motion.LoopCount;
            }

            return false; // 無限ループは Stop まで終わらない
        }

        // ポーズ中はゲームに追従するフラグ(Data.Flags.Pause=PauseWithGame)が立っている Tween のみ止める。
        // アドホック Tween(Data 無し)は追従しない([15] 実装メモに準拠。UI 演出は基本ポーズ非依存)。
        public void OnPause(PauseChannel channel, bool paused)
        {
            for (var i = 0; i < _active.Count; i++)
            {
                if (_instances.TryGet(_active[i], out var inst) && inst.PauseWithGame)
                {
                    inst.Paused = paused;
                }
            }
        }

        public void StopAll(StopReason reason)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                if (_instances.TryGet(_active[i], out var inst))
                {
                    CompleteInstance(_active[i], inst);
                }
            }
        }

        public void OnSceneUnload() => StopAll(StopReason.SceneUnload);

        // ── 内部: 準備 / 完了 / プール ──

        private void CompleteInstance(Handle<UiTweenMarker> handle, TweenInstance inst)
        {
            _active.Remove(handle);
            _instances.Remove(handle);
            inst.Waiter?.TrySetResult();
            ReturnInstance(inst);
        }

        private TweenInstance RentInstance()
        {
            if (_pool.Count > 0)
            {
                return _pool.Pop();
            }

            return new TweenInstance
            {
                OwnedTracks = new TweenTrack[MaxTracksPerTween],
                States = new TrackRuntime[MaxTracksPerTween],
            };
        }

        private void ReturnInstance(TweenInstance inst)
        {
            inst.Target = null;
            inst.Group = null;
            inst.Graphic = null;
            inst.Image = null;
            inst.Tracks = null;
            inst.TrackCount = 0;
            inst.Elapsed = 0f;
            inst.Speed = 1f;
            inst.UseScaledTime = false;
            inst.PauseWithGame = false;
            inst.Paused = false;
            inst.ResolvedDuration = 0f;
            inst.Waiter = null;

            for (var i = 0; i < inst.States.Length; i++)
            {
                inst.States[i] = default;
            }

            _pool.Push(inst);
        }

        private static void ResolveComponents(TweenInstance inst)
        {
            for (var i = 0; i < inst.TrackCount; i++)
            {
                switch (inst.Tracks[i].Property)
                {
                    case TweenProperty.Alpha:
                        if (inst.Group == null)
                        {
                            inst.Group = inst.Target.GetComponent<CanvasGroup>();
                            if (inst.Group == null)
                            {
                                inst.Group = inst.Target.gameObject.AddComponent<CanvasGroup>();
                            }
                        }

                        break;

                    case TweenProperty.Color:
                    case TweenProperty.ColorHue:
                        if (inst.Graphic == null)
                        {
                            inst.Graphic = inst.Target.GetComponent<Graphic>();
                        }

                        break;

                    case TweenProperty.FillAmount:
                        if (inst.Image == null)
                        {
                            inst.Image = inst.Target.GetComponent<Image>();
                        }

                        break;
                }
            }
        }

        // Tick の外(Play 直後の 1 回だけ)で From/To の解決と SplinePath の構築を行う。ここは alloc して良い。
        private static void PrepareTracks(TweenInstance inst)
        {
            var maxEnd = 0f;
            for (var i = 0; i < inst.TrackCount; i++)
            {
                ref var track = ref inst.Tracks[i];
                ref var state = ref inst.States[i];

                state.From = ResolveFrom(inst, track);
                state.To = ResolveTo(inst, track);
                state.Path = track.Property == TweenProperty.PathMove && track.Path.IsValid
                    ? new SplinePath(track.Path.Points, track.Path.Type)
                    : null;

                var end = UiTweenData.TrackEnd(track);
                if (end > maxEnd)
                {
                    maxEnd = end;
                }
            }

            inst.ResolvedDuration = maxEnd;
        }

        private static float EvaluateShape(in ValueDef motion, float t)
        {
            switch (motion.Mode)
            {
                case ValueMode.Parametric:
                    return motion.Parametric.Evaluate(t);
                case ValueMode.Curve:
                    return motion.Curve != null ? motion.Curve.Evaluate(t) : t;
                default: // Constant
                    return 1f;
            }
        }

        private static void ApplyTrack(TweenInstance inst, in TweenTrack track, ref TrackRuntime state, float shape)
        {
            switch (track.Property)
            {
                case TweenProperty.AnchoredPosition:
                {
                    var v = Vector4.LerpUnclamped(state.From.VectorValue, state.To.VectorValue, shape);
                    inst.Target.anchoredPosition = new Vector2(v.x, v.y);
                    break;
                }

                case TweenProperty.SizeDelta:
                {
                    var v = Vector4.LerpUnclamped(state.From.VectorValue, state.To.VectorValue, shape);
                    inst.Target.sizeDelta = new Vector2(v.x, v.y);
                    break;
                }

                case TweenProperty.Scale:
                {
                    var v = Vector4.LerpUnclamped(state.From.VectorValue, state.To.VectorValue, shape);
                    inst.Target.localScale = new Vector3(v.x, v.y, v.z);
                    break;
                }

                case TweenProperty.Rotation:
                {
                    var z = Mathf.LerpUnclamped(state.From.FloatValue, state.To.FloatValue, shape);
                    var e = inst.Target.localEulerAngles;
                    e.z = z;
                    inst.Target.localEulerAngles = e;
                    break;
                }

                case TweenProperty.RotationX:
                {
                    var x = Mathf.LerpUnclamped(state.From.FloatValue, state.To.FloatValue, shape);
                    var e = inst.Target.localEulerAngles;
                    e.x = x;
                    inst.Target.localEulerAngles = e;
                    break;
                }

                case TweenProperty.RotationY:
                {
                    var y = Mathf.LerpUnclamped(state.From.FloatValue, state.To.FloatValue, shape);
                    var e = inst.Target.localEulerAngles;
                    e.y = y;
                    inst.Target.localEulerAngles = e;
                    break;
                }

                case TweenProperty.Alpha:
                {
                    if (inst.Group != null)
                    {
                        inst.Group.alpha = Mathf.LerpUnclamped(state.From.FloatValue, state.To.FloatValue, shape);
                    }

                    break;
                }

                case TweenProperty.Color:
                {
                    if (inst.Graphic != null)
                    {
                        inst.Graphic.color = Color.LerpUnclamped(state.From.ColorValue, state.To.ColorValue, shape);
                    }

                    break;
                }

                case TweenProperty.FillAmount:
                {
                    if (inst.Image != null)
                    {
                        inst.Image.fillAmount = Mathf.LerpUnclamped(state.From.FloatValue, state.To.FloatValue, shape);
                    }

                    break;
                }

                case TweenProperty.PathMove:
                {
                    if (state.Path != null)
                    {
                        var p = state.Path.Evaluate(shape);
                        inst.Target.anchoredPosition = new Vector2(p.x, p.y);
                    }

                    break;
                }

                case TweenProperty.ColorHue:
                {
                    // RainbowTint 用: From/To.FloatValue を色相[0,1]として扱い、S=1,V=1 固定で毎フレーム RGB へ変換する。
                    // 現在の Alpha は保持する(RainbowTint と併用の Alpha フェードを壊さないため)。
                    if (inst.Graphic != null)
                    {
                        var hue = Mathf.LerpUnclamped(state.From.FloatValue, state.To.FloatValue, shape);
                        hue -= Mathf.Floor(hue); // HSVToRGB は [0,1) 範囲を期待するため wrap する
                        var rgb = Color.HSVToRGB(hue, 1f, 1f);
                        rgb.a = inst.Graphic.color.a;
                        inst.Graphic.color = rgb;
                    }

                    break;
                }
            }
        }

        private static ParamValue ResolveFrom(TweenInstance inst, in TweenTrack track)
        {
            var current = GetCurrentValue(inst, track.Property);
            switch (track.From)
            {
                case TweenFromMode.Absolute:
                    return CoerceToPropertyType(track.Property, track.FromValue);
                case TweenFromMode.Relative:
                    return AddParam(track.Property, current, CoerceToPropertyType(track.Property, track.FromValue));
                case TweenFromMode.OffScreen:
                    return ApplyOffScreenShift(inst, track.Property, track.OffScreen);
                default: // Current
                    return current;
            }
        }

        // OffScreen は「画面外から現在位置へ」を意味するため、To は ToValue を無視して現在位置に戻す。
        private static ParamValue ResolveTo(TweenInstance inst, in TweenTrack track)
        {
            if (track.From == TweenFromMode.OffScreen)
            {
                return GetCurrentValue(inst, track.Property);
            }

            return CoerceToPropertyType(track.Property, track.ToValue);
        }

        private static ParamValue GetCurrentValue(TweenInstance inst, TweenProperty prop)
        {
            switch (prop)
            {
                case TweenProperty.AnchoredPosition:
                    return VecParam(inst.Target.anchoredPosition.x, inst.Target.anchoredPosition.y);
                case TweenProperty.SizeDelta:
                    return VecParam(inst.Target.sizeDelta.x, inst.Target.sizeDelta.y);
                case TweenProperty.Scale:
                {
                    var s = inst.Target.localScale;
                    return VecParam(s.x, s.y, s.z);
                }

                case TweenProperty.Rotation:
                    return ParamValue.Of(inst.Target.localEulerAngles.z);
                case TweenProperty.RotationX:
                    return ParamValue.Of(inst.Target.localEulerAngles.x);
                case TweenProperty.RotationY:
                    return ParamValue.Of(inst.Target.localEulerAngles.y);
                case TweenProperty.Alpha:
                    return ParamValue.Of(inst.Group != null ? inst.Group.alpha : 1f);
                case TweenProperty.Color:
                    return new ParamValue { Type = ParamValueType.Color, ColorValue = inst.Graphic != null ? inst.Graphic.color : Color.white };
                case TweenProperty.FillAmount:
                    return ParamValue.Of(inst.Image != null ? inst.Image.fillAmount : 0f);
                case TweenProperty.ColorHue:
                    // Hue には「現在値」の安定した読み戻しが無い(RGB→Hue はコースの往復にならない)ため、
                    // RainbowTint は常に From=Absolute で 0→1 を明示駆動する前提。Current 使用時は 0 を返す。
                    return ParamValue.Of(0f);
                default:
                    return default;
            }
        }

        // デザイナーが Scale に float(uniform)を入れても Vector に正規化するなど、プロパティごとの期待型へ揃える。
        private static ParamValue CoerceToPropertyType(TweenProperty prop, ParamValue raw)
        {
            switch (prop)
            {
                case TweenProperty.AnchoredPosition:
                case TweenProperty.SizeDelta:
                case TweenProperty.Scale:
                    return raw.Type == ParamValueType.Vector ? raw : VecParam(raw.FloatValue, raw.FloatValue, raw.FloatValue);
                case TweenProperty.Rotation:
                case TweenProperty.RotationX:
                case TweenProperty.RotationY:
                case TweenProperty.Alpha:
                case TweenProperty.FillAmount:
                case TweenProperty.ColorHue:
                    return raw.Type == ParamValueType.Float ? raw : ParamValue.Of(raw.VectorValue.x);
                default:
                    return raw;
            }
        }

        private static ParamValue AddParam(TweenProperty prop, ParamValue a, ParamValue b)
        {
            switch (prop)
            {
                case TweenProperty.AnchoredPosition:
                case TweenProperty.SizeDelta:
                case TweenProperty.Scale:
                    return VecParam(a.VectorValue.x + b.VectorValue.x, a.VectorValue.y + b.VectorValue.y, a.VectorValue.z + b.VectorValue.z);
                case TweenProperty.Rotation:
                case TweenProperty.RotationX:
                case TweenProperty.RotationY:
                case TweenProperty.Alpha:
                case TweenProperty.FillAmount:
                case TweenProperty.ColorHue:
                    return ParamValue.Of(a.FloatValue + b.FloatValue);
                case TweenProperty.Color:
                    return new ParamValue { Type = ParamValueType.Color, ColorValue = a.ColorValue + b.ColorValue };
                default:
                    return a;
            }
        }

        private static ParamValue ApplyOffScreenShift(TweenInstance inst, TweenProperty prop, OffScreenDirection dir)
        {
            if (prop != TweenProperty.AnchoredPosition)
            {
                return GetCurrentValue(inst, prop);
            }

            var pos = ComputeOffScreenAnchoredPosition(inst.Target, dir);
            return VecParam(pos.x, pos.y);
        }

        // Slide 系プリセット(UiPresetFactory)からも同じ計算式を使うため internal 公開する。
        internal static Vector2 ComputeOffScreenAnchoredPosition(RectTransform target, OffScreenDirection dir)
        {
            var parent = target.parent as RectTransform;
            var pw = parent != null ? parent.rect.width : Screen.width;
            var ph = parent != null ? parent.rect.height : Screen.height;
            var sw = target.rect.width;
            var sh = target.rect.height;
            var cur = target.anchoredPosition;

            switch (dir)
            {
                case OffScreenDirection.Left:
                    return cur + new Vector2(-(pw / 2f + sw / 2f), 0f);
                case OffScreenDirection.Right:
                    return cur + new Vector2(pw / 2f + sw / 2f, 0f);
                case OffScreenDirection.Top:
                    return cur + new Vector2(0f, ph / 2f + sh / 2f);
                default:
                    return cur + new Vector2(0f, -(ph / 2f + sh / 2f));
            }
        }

        private static ParamValue VecParam(float x, float y, float z = 0f, float w = 0f)
            => new() { Type = ParamValueType.Vector, VectorValue = new Vector4(x, y, z, w) };
    }
}
