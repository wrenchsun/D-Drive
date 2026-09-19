using System.Collections.Generic;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using DDrive.Runtime.Haptics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace DDrive.Editor.Cutscene
{
    // [26_timeline.md] §4.4(Edit Mode プレビュー「Timeline ウィンドウ主導」、2026-09-19 ユーザー決定) —
    // Edit Mode では CutsceneManager を通さず、標準 Timeline ウィンドウの再生状態をそのまま
    // `CutsceneDirectorContext.FireEnabled` に映して実 Manager を駆動する。
    //
    // 「Timeline ウィンドウが実際に再生中か」の判定: `UnityEditor.Timeline.TimelineEditor` / 内部の
    // `TimelinePlaybackControls` に公開の bool は無いが、実機確認(Unity MCP `execute_code`)の結果、
    // `TimelinePlaybackControls.Play()`/`Pause()` を叩くと **公開 API である `PlayableDirector.state`
    // (`PlayState.Playing`/`Paused`)がそのまま追従する**ことを確認した(スクラブ〔`SetCurrentTime`〕は
    // `Paused` のまま変化しない)。そのため「dt 相当で time が単調に進んだら再生中」という近似(§4.4 の
    // 想定していたフォールバック)は不要で、`director.state == PlayState.Playing` を直接使う。
    //
    // マーカー(Event/Signal/Shake/Haptic)は CutsceneManager と同じ「時刻順カーソル、跨いだら発火」方式
    // (`CutsceneMarkerCursor<T>`、CutsceneManager.CollectMarkers/AdvanceMarkers と同じパターンを共有)。
    // スクラブ(非再生)中は無音でカーソルだけ進める。巻き戻し(time が後退)を検出したらカーソルを
    // リセットして無音で追いつかせる(Skip/ApplySeek と同じ「Seek は無音」方針)。
    // public(InternalsVisibleTo 未設定のため、テスト asmdef から直接検証できるようにする。
    // SpecDiffService.cs 等の既存クラスと同じ理由)。
    [InitializeOnLoad]
    public static class CutsceneEditModePreviewProvider
    {
        private sealed class Session
        {
            public TimelineAsset Timeline;
            public double LastTime;
            public bool WasPlaying;
            public readonly CutsceneMarkerCursor<CutsceneEventNotification> EventCursor = new();
            public readonly CutsceneMarkerCursor<CutsceneSignalNotification> SignalCursor = new();
            public readonly CutsceneMarkerCursor<CutsceneShakeNotification> ShakeCursor = new();
            public readonly CutsceneMarkerCursor<CutsceneHapticNotification> HapticCursor = new();

            public void Collect(TimelineAsset timeline)
            {
                Timeline = timeline;
                EventCursor.Collect(timeline);
                SignalCursor.Collect(timeline);
                ShakeCursor.Collect(timeline);
                HapticCursor.Collect(timeline);
            }

            public void SilentAdvanceTo(double elapsed)
            {
                EventCursor.ResetCursor();
                SignalCursor.ResetCursor();
                ShakeCursor.ResetCursor();
                HapticCursor.ResetCursor();
                EventCursor.Advance(elapsed, fire: false, null);
                SignalCursor.Advance(elapsed, fire: false, null);
                ShakeCursor.Advance(elapsed, fire: false, null);
                HapticCursor.Advance(elapsed, fire: false, null);
            }
        }

        private static CutsceneEditModeManagers _managers;
        private static readonly Dictionary<int, Session> _sessions = new();
        private static readonly List<int> _staleIds = new();
        private static double _lastTick;

        static CutsceneEditModePreviewProvider()
        {
            _lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
            AssemblyReloadEvents.beforeAssemblyReload += TearDown;
        }

        // `CutsceneDataEditor` の「▶ Timeline ウィンドウで開く」から呼ぶ: プレビュー用 Director の
        // GameObject に `CutsceneDirectorContext` を付け(無ければ追加)、Editor 用 Manager 群を割り当てる。
        // FireEnabled は初期状態では false(まだ再生していない = ドラッグ中と同じ無音状態)。
        public static CutsceneDirectorContext PrepareContext(GameObject directorRoot)
        {
            EnsureManagers();
            var context = directorRoot.GetComponent<CutsceneDirectorContext>();
            if (context == null)
            {
                context = directorRoot.AddComponent<CutsceneDirectorContext>();
            }

            context.ManagerRefs = BuildManagerRefs();
            context.FireEnabled = false;
            return context;
        }

        // アセット追加後の再解決(EditorAnchorRegistry.Refresh)を外から要求できるようにする。
        public static void RefreshRegistry() => _managers?.RefreshRegistry();

        // `CutsceneEditModeDirectorSetup` が Bindings の Target=SpawnModel を解決するために使う
        // (同じ Editor 用 ModelsManager/Registry を共有する。二重に Manager 群を作らない)。
        public static CutsceneEditModeManagers EnsureAndGetManagers()
        {
            EnsureManagers();
            return _managers;
        }

        private static void EnsureManagers() => _managers ??= new CutsceneEditModeManagers();

        private static CutsceneDirectorManagerRefs BuildManagerRefs() => new()
        {
            Audio = _managers.Audio,
            Vfx = _managers.Vfx,
            Ui = _managers.Ui,
            Groups = _managers.Groups,
        };

        private static void OnEditorUpdate()
        {
            // Play Mode は CutsceneManager が別経路で駆動する([26] §4.4 決定)。二重駆動を避けるため、
            // Play Mode 中はこの監視役を完全に止める(Play Mode の CutsceneRoot にも同じ
            // CutsceneDirectorContext が付いているため、ここで動くと Play Mode の再生と衝突する)。
            if (Application.isPlaying)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastTick), 0f, 0.25f);
            _lastTick = now;

            var contexts = Object.FindObjectsByType<CutsceneDirectorContext>(FindObjectsSortMode.None);
            if (contexts.Length == 0)
            {
                return;
            }

            EnsureManagers();
            _managers.Tick(dt);

            _staleIds.Clear();
            foreach (var kv in _sessions)
            {
                _staleIds.Add(kv.Key);
            }

            foreach (var context in contexts)
            {
                var director = context.GetComponent<PlayableDirector>();
                if (director == null)
                {
                    continue;
                }

                if (context.ManagerRefs == null)
                {
                    // ドメインリロード後([System.NonSerialized] のため null に戻る)の再構築。
                    context.ManagerRefs = BuildManagerRefs();
                }

                var id = director.gameObject.GetInstanceID();
                _staleIds.Remove(id);

                var timeline = director.playableAsset as TimelineAsset;
                if (!_sessions.TryGetValue(id, out var session) || session.Timeline != timeline)
                {
                    session = new Session();
                    session.Collect(timeline);
                    session.LastTime = director.time;
                    _sessions[id] = session;
                }

                var playing = director.state == PlayState.Playing;
                context.FireEnabled = playing;

                var elapsed = director.time;

                // 再生開始の立ち上がり(false→true)で、一時停止中にデザイナーが Timeline ウィンドウで
                // 足した新規マーカーを拾うため再収集する(同じ TimelineAsset インスタンスにトラック/マーカーを
                // 追加しただけでは §Timeline != timeline の判定に引っかからないため)。再収集直後は現在地点まで
                // 無音で追いつかせ、既に過ぎているはずのマーカーを再生開始と同時に誤発火させない。
                if (playing && !session.WasPlaying)
                {
                    session.Collect(timeline);
                    session.SilentAdvanceTo(elapsed);
                }
                else if (elapsed + 1e-4d < session.LastTime)
                {
                    // 巻き戻し(スクラブで戻した)。跨いだ扱いを無音でやり直す([26] §4.4「スクラブで連打しない」)。
                    session.SilentAdvanceTo(elapsed);
                }
                else
                {
                    session.EventCursor.Advance(elapsed, playing, FireEvent);
                    session.SignalCursor.Advance(elapsed, playing, FireSignal);
                    session.ShakeCursor.Advance(elapsed, playing, FireShake);
                    session.HapticCursor.Advance(elapsed, playing, FireHaptic);
                }

                session.WasPlaying = playing;
                session.LastTime = elapsed;
                CutsceneEditModeCameraWriter.Apply(director.gameObject);
            }

            foreach (var staleId in _staleIds)
            {
                _sessions.Remove(staleId);
            }
        }

        private static void FireEvent(CutsceneEventNotification marker) => _managers.RaiseEvent(in marker.Event);

        private static void FireSignal(CutsceneSignalNotification marker)
            => Debug.Log($"[DDrive] Cutscene Signal (Edit Mode プレビュー): '{marker.Key}'");

        private static void FireShake(CutsceneShakeNotification marker)
        {
            if (!marker.ShakeId.IsValid)
            {
                return;
            }

            var data = _managers.Registry.ResolveOrPlaceholder<CameraShakeData>(marker.ShakeId.Value);
            _managers.ShakeDriver.Play(data);
        }

        private static void FireHaptic(CutsceneHapticNotification marker)
        {
            if (!marker.HapticId.IsValid)
            {
                return;
            }

            var data = _managers.Registry.ResolveOrPlaceholder<HapticsData>(marker.HapticId.Value);
            _managers.HapticsDriver.Play(data);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                // Play Mode に入る直前に、Shake/Haptics の出力とカメラの書き込みを止める
                // (SceneCameraShakePreviewDriver/EditorHapticsPreviewDriver 自身も同じ通知で自浄する)。
                CutsceneEditModeCameraWriter.ResetCapture();
                _sessions.Clear();
            }
        }

        private static void OnActiveSceneChanged(Scene previous, Scene current) => ResetSessions();

        private static void OnPrefabStageChanged(PrefabStage stage) => ResetSessions();

        private static void ResetSessions()
        {
            _sessions.Clear();
            CutsceneEditModeCameraWriter.ResetCapture();
        }

        private static void TearDown()
        {
            ResetSessions();
            _managers?.Dispose();
            _managers = null;
        }
    }
}
