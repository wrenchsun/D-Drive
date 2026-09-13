using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Values;
using UnityEngine;
using HapticId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Haptics.HapticMarker>;

namespace DDrive.Runtime.Haptics
{
    // [16_camera_haptics.md] Part B — コントローラー振動の中核。毎 Tick、再生中インスタンスの
    // Low/High を ValueDef で評価し「チャンネルごとの Max 合成」(加算だと飽和する)× GlobalScale で
    // 出力する。出力先は IHapticOutput で差し替え可能(既定 GamepadHapticOutput)。
    //
    // 実装メモ(2026-09-14): AC「Pause で出力 0」を確実に満たすため、Pause 中は per-instance の
    // Flags.Pause を見ずに一律で出力 0 にする(Vfx/CameraFx は Flags.Pause=IgnorePause のものを
    // 継続させるが、実機のモーターを鳴らし続けるのは事故のリスクが高いため安全側に倒した。要判断)。
    public sealed class HapticsManager : IAssetManager
    {
        private sealed class HapticInstance
        {
            public HapticsData Data;
            public float Elapsed;
            public float StrengthScale = 1f;
        }

        private readonly IAssetRegistry _registry;
        private readonly IHapticOutput _output;
        private readonly InstanceStore<HapticMarker, HapticInstance> _instances = new();
        private readonly List<Handle<HapticMarker>> _active = new();
        private bool _extensionsWarned;
        private bool _paused;

        private float _globalScale = 1f;

        public AssetType Type => AssetType.Haptics;

        public HapticsManager(IAssetRegistry registry, IHapticOutput output = null)
        {
            _registry = registry;
            _output = output ?? new GamepadHapticOutput();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterPlaceholder()
        {
            PlaceholderProvider.Register(CreatePlaceholder);
        }

        // FR-1.4: 未登録/未ロードの Haptic は「振動しない」プレースホルダで代替する。
        private static HapticsData CreatePlaceholder()
        {
            var data = ScriptableObject.CreateInstance<HapticsData>();
            data.DisplayName = "<Placeholder:HAPTIC>";
            data.LowFreq = ValueDef.Constant01(0f);
            data.HighFreq = ValueDef.Constant01(0f);
            return data;
        }

        // ── Play ──

        public Handle<HapticMarker> Play(HapticId id)
            => PlayData(_registry.ResolveOrPlaceholder<HapticsData>(id.Value));

        public Handle<HapticMarker> Play(HapticId id, float strengthScale)
            => PlayData(_registry.ResolveOrPlaceholder<HapticsData>(id.Value), strengthScale);

        public Handle<HapticMarker> PlayData(HapticsData data, float strengthScale = 1f)
        {
            if (data == null)
            {
                return Handle<HapticMarker>.Invalid;
            }

            if (data.Extensions != null && data.Extensions.Length > 0)
            {
                WarnExtensionsUnimplemented();
            }

            var instance = new HapticInstance
            {
                Data = data,
                StrengthScale = Mathf.Max(0f, strengthScale),
            };

            var handle = _instances.Add(instance);
            _active.Add(handle);
            return handle;
        }

        public void Stop(Handle<HapticMarker> handle) => Remove(handle);

        public bool IsPlaying(Handle<HapticMarker> handle) => _instances.IsValidSilent(handle);

        // デザイナー/プログラマー向け API([16] Part B)。IAssetManager.StopAll(StopReason) とは別オーバーロード。
        public void StopAll()
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                Remove(_active[i]);
            }
        }

        public void SetGlobalScale(float scale) => _globalScale = Mathf.Max(0f, scale);

        // アプリ終了・フォーカス喪失時など、再生中インスタンスは残したままモーターだけ即座に 0 に戻す。
        public void ResetOutput() => _output.SetMotors(0f, 0f);

        private void Remove(Handle<HapticMarker> handle)
        {
            _active.Remove(handle);
            _instances.Remove(handle);
        }

        // ── Tick ──

        public void Tick(float dt)
        {
            if (_paused)
            {
                return;
            }

            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var handle = _active[i];
                if (!_instances.TryGet(handle, out var instance))
                {
                    _active.RemoveAt(i);
                    continue;
                }

                instance.Elapsed += dt;
                if (IsExpired(instance))
                {
                    Remove(handle);
                }
            }

            ComposeAndOutput();
        }

        private static bool IsExpired(HapticInstance instance)
        {
            var duration = Mathf.Max(instance.Data.LowFreq.Duration, instance.Data.HighFreq.Duration);
            return duration <= 0f || instance.Elapsed >= duration;
        }

        // チャンネルごとの Max 合成(加算だと飽和するため)。
        private void ComposeAndOutput()
        {
            var low = 0f;
            var high = 0f;

            for (var i = 0; i < _active.Count; i++)
            {
                if (!_instances.TryGet(_active[i], out var instance))
                {
                    continue;
                }

                var l = Mathf.Max(0f, instance.Data.LowFreq.EvaluateAt(instance.Elapsed)) * instance.StrengthScale;
                var h = Mathf.Max(0f, instance.Data.HighFreq.EvaluateAt(instance.Elapsed)) * instance.StrengthScale;
                low = Mathf.Max(low, l);
                high = Mathf.Max(high, h);
            }

            low = Mathf.Clamp01(low * _globalScale);
            high = Mathf.Clamp01(high * _globalScale);
            _output.SetMotors(low, high);
        }

        public void OnPause(PauseChannel channel, bool paused)
        {
            _paused = paused;
            if (paused)
            {
                _output.SetMotors(0f, 0f);
            }
        }

        public void StopAll(StopReason reason)
        {
            StopAll();
            _output.SetMotors(0f, 0f);
        }

        public void OnSceneUnload() => StopAll(StopReason.SceneUnload);

        private void WarnExtensionsUnimplemented()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (!_extensionsWarned)
            {
                _extensionsWarned = true;
                Debug.LogWarning("[DDrive] Haptics: HapticExt(プラットフォーム別拡張)は未実装です。基本の 2 モーターカーブのみ再生します。");
            }
#endif
        }
    }
}
