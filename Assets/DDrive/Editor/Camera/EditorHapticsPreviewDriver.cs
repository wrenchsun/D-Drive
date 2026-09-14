using System;
using DDrive.Editor.Preview;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Haptics;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.CameraFx
{
    // [16_camera_haptics.md] §C-2(5-2c) — HapticsEditor の「Test on Pad」。接続中のゲームパッドを
    // Editor 再生外から実 HapticsManager 経由で直接振動させる(ADR-4)。出力先は IHapticOutput
    // (既定 GamepadHapticOutput。テストは Fake を注入して実パッドに依存しない)。
    //
    // 「止め忘れ防止」(CLAUDE.md §0-7 / [11_tasks.md] 5-2c AC): 明示的な停止・ウィンドウを閉じる
    // (Dispose)・ドメインリロード直前・Play Mode 突入直前・エディタのフォーカス喪失、いずれの経路でも
    // 必ずモーターを 0 に戻す。ティック自体も止めるので、フォーカスが無い間に古い Instance が
    // 出力を上書きし続けることもない。
    public sealed class EditorHapticsPreviewDriver : IDisposable
    {
        private readonly AssetRegistry _registry;
        private readonly IHapticOutput _output;

        // P5 レビュー対応(2026-09-14) 5-4 追補(b): PresentationEditor の統合プレビューから渡された場合、
        // 自前の Unscaled dt に TimeService.ScaledDeltaTime を掛けてから Tick する(HitStop で止まるように
        // する)。単体の HapticsEditor「Test on Pad」から使う場合は null のままで、従来どおり Unscaled。
        private readonly TimeService _timeService;
        private double _lastTick;
        private bool _ticking;

        public HapticsManager Manager { get; }

        // registry/output: 省略時はそれぞれ EditorAnchorRegistry.Build() / GamepadHapticOutput(実パッド)。
        // テストは Fake を注入する。timeService: 省略時は Unscaled(従来どおり)。
        public EditorHapticsPreviewDriver(AssetRegistry registry = null, IHapticOutput output = null, TimeService timeService = null)
        {
            _registry = registry ?? EditorAnchorRegistry.Build();
            _output = output ?? new GamepadHapticOutput();
            _timeService = timeService;
            Manager = new HapticsManager(_registry, _output);
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.focusChanged += OnFocusChanged;
            AssemblyReloadEvents.beforeAssemblyReload += ResetAndStop;
        }

        public Handle<HapticMarker> Play(HapticsData data, float strengthScale = 1f)
        {
            if (data == null)
            {
                return Handle<HapticMarker>.Invalid;
            }

            EnsureTicking();
            return Manager.PlayData(data, strengthScale);
        }

        private void EnsureTicking()
        {
            if (_ticking)
            {
                return;
            }

            _ticking = true;
            _lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += EditorTick;
        }

        private void EditorTick()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastTick), 0f, 0.25f);
            _lastTick = now;
            // _timeService.Tick(...) はここでは呼ばない(HitStop の残り時間を進めるのは
            // ScenePresentationPreviewDriver.Tick が 1 フレームに 1 回だけ行う。ScaledDeltaTime は
            // 現在の TimeScale を読むだけの純関数なので、複数箇所から呼んでも二重にはならない)。
            Tick(_timeService != null ? _timeService.ScaledDeltaTime(dt) : dt);
        }

        // テストからも直接呼べる公開 Tick(EditorApplication.update の実体。SceneVfxPreviewDriver と同じ形)。
        public void Tick(float dt) => Manager.Tick(dt);

        // 停止ボタン / 各種安全弁から呼ぶ公開 API。ティックを止めて Manager.StopAll(台帳クリア + 出力0)。
        // Manager 自体は破棄しないので、次の Play() ですぐ再開できる(連打テストの合間の意図しない解放を避ける)。
        public void ResetAndStop()
        {
            if (_ticking)
            {
                EditorApplication.update -= EditorTick;
                _ticking = false;
            }

            Manager.StopAll(StopReason.Manual);
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                ResetAndStop();
            }
        }

        // エディタ(アプリ)がフォーカスを失った瞬間に必ず 0 へ(実機のモーターを鳴らし続ける事故を防ぐ、
        // HapticsManager 本体の Pause 時の安全側設計と同じ考え方。[16] 実装メモ参照)。
        private void OnFocusChanged(bool focused)
        {
            if (!focused)
            {
                ResetAndStop();
            }
        }

        public void Dispose()
        {
            ResetAndStop();
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.focusChanged -= OnFocusChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= ResetAndStop;
        }
    }
}
