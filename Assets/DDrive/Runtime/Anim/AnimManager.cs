using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using UnityEngine;
using AnimId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anim.AnimMarker>;

namespace DDrive.Runtime.Anim
{
    // [05_model_animation.md] B-3 — 3D アニメーションの再生 Manager(チケット 3-1)。
    // AnimatorController を「土台」にしたまま、AnimData 単位で CrossFade 再生し、経過時間を自前で追跡して
    // Frame/Time/OnLoop/OnDestroy 等の共通 AssetEvent を EventBus で発火する(Clip に AnimationEvent を埋めない)。
    // 対象 Animator には AnimatorProxy を自動アタッチし、IK と BlendShape の適用を代行させる。
    //
    // ライフサイクルと発火するトリガ:
    //   Play        → OnSpawn, OnEnable
    //   周回(Loop)  → OnLoop(Frame/Time は毎周リセットして再発火)
    //   自然終了    → OnDisable, OnDestroy
    //   Stop / 同じ Animator+Layer への別 Play による中断 → OnDisable のみ(OnDestroy は出ない = 「中断」の区別)
    //
    // 対象 Animator にコントローラが無い / ステートが無い場合も例外にせず、時間追跡とイベントだけ行う
    // (Placeholder や未配線のモデルでもゲームは止まらない。開発ビルドで警告 1 回)。
    public sealed class AnimManager : IAssetManager
    {
        private sealed class AnimInstance
        {
            public AnimData Data;
            public Animator Animator;
            public AnimatorProxy Proxy;
            public float ElapsedSeconds;
            public float Speed = 1f;
            public bool Paused;
            public int LoopCount;
            public InstanceContext Context;
            public bool SpeedTouched; // SetSpeed / Pause で Animator.speed を書いたか(解放時に戻すため)
        }

        private readonly IAssetRegistry _registry;
        private readonly EventBus _events;
        private readonly InstanceStore<AnimMarker, AnimInstance> _instances = new();
        private readonly List<Handle<AnimMarker>> _allActive = new();
        private readonly HashSet<AnimData> _stateWarned = new();

        public AssetType Type => AssetType.Anim;

        public EventBus Events => _events;

        public AnimManager(IAssetRegistry registry, EventBus events = null)
        {
            _registry = registry;
            _events = events ?? new EventBus();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterPlaceholder()
        {
            PlaceholderProvider.Register(CreatePlaceholder);
        }

        // FR-1.4: 未登録 ID は「何も再生しないが FallbackLengthSec だけ再生中扱い」の Placeholder。
        private static AnimData CreatePlaceholder()
        {
            var data = ScriptableObject.CreateInstance<AnimData>();
            data.DisplayName = "<Placeholder:ANIM>";
            return data;
        }

        // ── Play ──

        public Handle<AnimMarker> Play(AnimId id, Animator target)
            => PlayData(_registry.ResolveOrPlaceholder<AnimData>(id.Value), target);

        public Handle<AnimMarker> Play(AnimId id, Animator target, float fade)
            => PlayData(_registry.ResolveOrPlaceholder<AnimData>(id.Value), target, fade);

        // fade < 0 なら Data.DefaultCrossFade。
        public Handle<AnimMarker> PlayData(AnimData data, Animator target, float fade = -1f)
        {
            if (data == null)
            {
                return Handle<AnimMarker>.Invalid;
            }

            if (target == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning($"[DDrive] Anim '{data.DisplayName}' was played without an Animator; ignored.");
#endif
                return Handle<AnimMarker>.Invalid;
            }

            // 同じ Animator の同じレイヤーで再生中のものは中断(OnDisable のみ)。
            for (var i = _allActive.Count - 1; i >= 0; i--)
            {
                if (_instances.TryGet(_allActive[i], out var other) && other.Animator == target && other.Data.Layer == data.Layer)
                {
                    Interrupt(_allActive[i], other);
                }
            }

            var proxy = target.GetComponent<AnimatorProxy>();
            if (proxy == null)
            {
                proxy = target.gameObject.AddComponent<AnimatorProxy>();
            }

            CrossFade(data, target, fade < 0f ? data.DefaultCrossFade : fade);

            var instance = new AnimInstance
            {
                Data = data,
                Animator = target,
                Proxy = proxy,
            };
            var handle = _instances.Add(instance);
            instance.Context = new InstanceContext(handle.Index, handle.Generation);
            _allActive.Add(handle);

            _events.Begin(instance.Context, data.Events);
            _events.Fire(instance.Context, EventTrigger.OnSpawn);
            _events.Fire(instance.Context, EventTrigger.OnEnable);

            proxy.SetActive(data, 0f);
            proxy.ApplyBlendShapes(data, 0f);
            return handle;
        }

        private void CrossFade(AnimData data, Animator target, float fade)
        {
            var state = data.ResolvedStateName;
            if (target.runtimeAnimatorController == null || string.IsNullOrEmpty(state))
            {
                return; // 土台無し: 時間追跡とイベントだけ行う
            }

            var hash = Animator.StringToHash(state);
            if (data.Layer < target.layerCount && target.HasState(data.Layer, hash))
            {
                target.CrossFadeInFixedTime(hash, Mathf.Max(0f, fade), data.Layer);
                return;
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (_stateWarned.Add(data))
            {
                Debug.LogWarning($"[DDrive] Anim '{data.DisplayName}': state '{state}' (layer {data.Layer}) not found on '{target.name}'. Events still fire.");
            }
#endif
        }

        // ── Handle 操作 ──

        public bool IsPlaying(Handle<AnimMarker> handle) => _instances.IsValid(handle);

        public int ActiveCount => _allActive.Count;

        // その Animator で再生中のものを全て中断する(OnDisable のみ)。ModelsManager.Despawn が呼ぶ:
        // プールへ返した GameObject が別モデルとして再利用されても、旧アニメーションの時間・イベント・
        // BlendShape / IK が動き続けないようにする([05] A-3)。
        public void StopAllFor(Animator animator)
        {
            if (animator == null)
            {
                return;
            }

            for (var i = _allActive.Count - 1; i >= 0; i--)
            {
                if (_instances.TryGet(_allActive[i], out var instance) && instance.Animator == animator)
                {
                    Interrupt(_allActive[i], instance);
                }
            }
        }

        // 0..1(ループ中は周回内の位置)。無効なら -1。
        public float GetNormalizedTime(Handle<AnimMarker> handle)
            => _instances.TryGet(handle, out var instance) ? Mathf.Clamp01(instance.ElapsedSeconds / instance.Data.LengthSec) : -1f;

        public int GetLoopCount(Handle<AnimMarker> handle)
            => _instances.TryGet(handle, out var instance) ? instance.LoopCount : 0;

        // 発火元 Instance(InstanceContext)の Transform。AssetEventDispatcher が SE/VFX の contextRoot に使う。
        public Transform GetContextTransform(InstanceContext ctx)
        {
            // Handle のコンストラクタは Foundation 内部限定のため、台帳から Context 一致で探す(再生数は小さい)。
            for (var i = 0; i < _allActive.Count; i++)
            {
                if (_instances.TryGet(_allActive[i], out var instance) && instance.Context.Equals(ctx))
                {
                    return instance.Animator != null ? instance.Animator.transform : null;
                }
            }

            return null;
        }

        // 対象 Animator と再生中の Data(エディタ表示用)。
        public bool TryGetPlayback(Handle<AnimMarker> handle, out AnimData data, out Animator animator)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                data = instance.Data;
                animator = instance.Animator;
                return true;
            }

            data = null;
            animator = null;
            return false;
        }

        // タイムラインのシーク(エディタ用)。イベントは発火せず、その時刻以前を発火済みに揃える。
        public void Seek(Handle<AnimMarker> handle, float normalizedTime)
        {
            if (!_instances.TryGet(handle, out var instance))
            {
                return;
            }

            var length = instance.Data.LengthSec;
            instance.ElapsedSeconds = Mathf.Clamp01(normalizedTime) * length;
            _events.SeekAnimation(instance.Context, instance.ElapsedSeconds, instance.Data.FrameRate);
            SamplePose(instance, 0f, force: true);
            instance.Proxy.SetActive(instance.Data, Mathf.Clamp01(normalizedTime));
            instance.Proxy.ApplyBlendShapes(instance.Data, Mathf.Clamp01(normalizedTime));
        }

        // EditMode では Animator が自動で進まないため、プレビューはここでポーズを進める。
        // Controller があれば Animator.Update、無ければ Clip を直接サンプリングする。PlayMode 中は Unity に任せる。
        private void SamplePose(AnimInstance instance, float dt, bool force)
        {
            if (Application.isPlaying && !force)
            {
                return;
            }

            var animator = instance.Animator;
            if (animator == null)
            {
                return;
            }

            if (animator.runtimeAnimatorController != null)
            {
                var state = instance.Data.ResolvedStateName;
                var hash = Animator.StringToHash(state);
                if (force && !string.IsNullOrEmpty(state) && instance.Data.Layer < animator.layerCount && animator.HasState(instance.Data.Layer, hash))
                {
                    animator.Play(hash, instance.Data.Layer, Mathf.Clamp01(instance.ElapsedSeconds / instance.Data.LengthSec));
                    animator.Update(0f);
                    return;
                }

                if (!Application.isPlaying)
                {
                    animator.Update(dt * instance.Speed);
                }

                return;
            }

            if (instance.Data.Clip != null)
            {
                instance.Data.Clip.SampleAnimation(animator.gameObject, instance.ElapsedSeconds);
            }
        }

        public void SetSpeed(Handle<AnimMarker> handle, float speed)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                instance.Speed = Mathf.Max(0f, speed);
                if (!instance.Paused && instance.Animator != null)
                {
                    instance.Animator.speed = instance.Speed;
                    instance.SpeedTouched = true;
                }
            }
        }

        // 中断扱いで止める(OnDisable のみ)。fade は AnimatorController の遷移に任せるため現状は未使用
        // (ステートマシン側の Exit 遷移が戻りを担当する)。
        public void Stop(Handle<AnimMarker> handle, float fade = 0f)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                Interrupt(handle, instance);
            }
        }

        private void Interrupt(Handle<AnimMarker> handle, AnimInstance instance)
        {
            _events.Fire(instance.Context, EventTrigger.OnDisable);
            Release(handle, instance);
        }

        private void Finish(Handle<AnimMarker> handle, AnimInstance instance)
        {
            _events.Fire(instance.Context, EventTrigger.OnDisable);
            _events.Fire(instance.Context, EventTrigger.OnDestroy);
            Release(handle, instance);
        }

        private void Release(Handle<AnimMarker> handle, AnimInstance instance)
        {
            _events.End(instance.Context);
            if (instance.Proxy != null)
            {
                instance.Proxy.ClearActive(instance.Data);
            }

            _instances.Remove(handle);
            _allActive.Remove(handle);
            RestoreAnimatorSpeed(instance);
        }

        // SetSpeed / Pause で書き換えた Animator.speed を、解放時に戻す。同じ Animator の別再生が残っていれば
        // その速度、無ければ 1(触っていなければ何もしない: 外部が設定した speed を壊さない)。
        private void RestoreAnimatorSpeed(AnimInstance released)
        {
            var animator = released.Animator;
            if (animator == null || !released.SpeedTouched)
            {
                return;
            }

            for (var i = 0; i < _allActive.Count; i++)
            {
                if (_instances.TryGet(_allActive[i], out var other) && other.Animator == animator)
                {
                    animator.speed = other.Paused ? 0f : other.Speed;
                    return;
                }
            }

            animator.speed = 1f;
        }

        // ── Tick / Pause / StopAll ──

        public void Tick(float dt)
        {
            for (var i = _allActive.Count - 1; i >= 0; i--)
            {
                var handle = _allActive[i];
                if (!_instances.TryGet(handle, out var instance))
                {
                    _allActive.RemoveAt(i);
                    continue;
                }

                // Animator が破棄された(シーン破棄等)。中断扱いで台帳から外す。
                if (instance.Animator == null)
                {
                    Interrupt(handle, instance);
                    continue;
                }

                if (instance.Paused)
                {
                    continue;
                }

                var length = instance.Data.LengthSec;
                instance.ElapsedSeconds += dt * instance.Speed;

                // 大きい dt(フレーム落ち・復帰直後・倍速)で 1 Tick に複数周回分進んでも、周回ごとの OnLoop と
                // Frame/Time を取りこぼさないよう、通過した周回数だけ繰り返す(length は LengthSec が正を保証)。
                var finished = false;
                while (length > 0f && instance.ElapsedSeconds >= length)
                {
                    // 終端の Frame/Time を取りこぼさないよう、終了 / 周回の前にクリップ末尾で判定する。
                    _events.TickAnimation(instance.Context, length, instance.Data.FrameRate);
                    if (!instance.Data.Loop)
                    {
                        Finish(handle, instance);
                        finished = true;
                        break;
                    }

                    // 周回: 巻き戻して Frame/Time を再発火可能にする。
                    instance.ElapsedSeconds -= length;
                    instance.LoopCount++;
                    _events.ResetOnce(instance.Context);
                    _events.Fire(instance.Context, EventTrigger.OnLoop);
                    if (!_instances.IsValid(handle))
                    {
                        finished = true; // OnLoop のハンドラ内で Stop された
                        break;
                    }
                }

                if (finished)
                {
                    continue;
                }

                _events.TickAnimation(instance.Context, instance.ElapsedSeconds, instance.Data.FrameRate);

                var normalized = Mathf.Clamp01(instance.ElapsedSeconds / length);
                SamplePose(instance, dt, force: false);
                instance.Proxy.SetActive(instance.Data, normalized);
                instance.Proxy.ApplyBlendShapes(instance.Data, normalized);
            }
        }

        public void OnPause(PauseChannel channel, bool paused)
        {
            for (var i = 0; i < _allActive.Count; i++)
            {
                if (!_instances.TryGet(_allActive[i], out var instance) || instance.Data.Flags.Pause != PauseMode.PauseWithGame)
                {
                    continue;
                }

                instance.Paused = paused;
                if (instance.Animator != null)
                {
                    instance.Animator.speed = paused ? 0f : instance.Speed;
                    instance.SpeedTouched = true;
                }
            }
        }

        public void StopAll(StopReason reason)
        {
            for (var i = _allActive.Count - 1; i >= 0; i--)
            {
                Stop(_allActive[i]);
            }
        }

        public void OnSceneUnload() => StopAll(StopReason.SceneUnload);
    }
}
