using System;
using DDrive.Editor.Preview;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Manager;
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
        private double _lastTick;
        private bool _ticking;

        public HapticsManager Manager { get; }

        // registry/output: 省略時はそれぞれ EditorAnchorRegistry.Build() / GamepadHapticOutput(実パッド)。
        // テストは Fake を注入する。
        public EditorHapticsPreviewDriver(AssetRegistry registry = null, IHapticOutput output = null)
        {
            _registry = registry ?? EditorAnchorRegistry.Build();
            _output = output ?? new GamepadHapticOutput();
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
            Tick(dt);
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
