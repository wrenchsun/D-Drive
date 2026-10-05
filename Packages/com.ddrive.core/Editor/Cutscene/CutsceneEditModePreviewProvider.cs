using System.Collections.Generic;
using DDrive.Foundation.Handle;
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
    //
    // 外部パッケージの `ICutsceneMarker`([51] §4.5 FC-4)も同じ規則で監視する(`ExternalMarkerCursor`、
    // この asm 内 internal。`CutsceneMarkerCursor<T>` は `where T : Marker` で interface を受けられず、
    // Runtime の internal はここから見えないため Editor 側に持つ)。`IsEditPreview = true`、例外は隔離。
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
            public readonly ExternalMarkerCursor ExternalCursor = new();

            public void Collect(TimelineAsset timeline)
            {
                Timeline = timeline;
                ExternalCursor.Collect(timeline);
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
                ExternalCursor.ResetCursor();
                ExternalCursor.Advance(elapsed, fire: false, null);
                EventCursor.Advance(elapsed, fire: false, null);
                SignalCursor.Advance(elapsed, fire: false, null);
                ShakeCursor.Advance(elapsed, fire: false, null);
                HapticCursor.Advance(elapsed, fire: false, null);
            }
        }

        private static CutsceneEditModeManagers _managers;
        private static readonly Dictionary<int, Session> _sessions = new();
        private static readonly List<int> _staleIds = new();

        // PrepareContext で用意したプレビュー用 Director の Context。プレビュー用 Director は HideFlags.DontSave で作られ
        // (CutsceneEditModeDirectorSetup.EnsureDirector)、Object.FindObjectsByType は DontSave のオブジェクトを返さないため、
        // 検索だけに頼ると見つからず何も駆動されない。用意した時点でここに覚えておき、毎フレームの検索結果に足す。
        private static readonly List<CutsceneDirectorContext> _preparedContexts = new();
        private static readonly List<CutsceneDirectorContext> _contextBuffer = new();
        private static double _lastTick;

        // 停止中の再生位置がこの秒数以下なら「先頭(0 秒)」とみなす(浮動小数の誤差の吸収だけ。更新間隔とは無関係)。
        private const double StartAtZeroEpsilon = 1e-4d;

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
            if (!_preparedContexts.Contains(context))
            {
                _preparedContexts.Add(context);
            }

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

        // docs/45 P1-5(2026-09-20) — `CutsceneEditModeDirectorSetup.TearDown`(Despawn 用)専用。
        // `EnsureAndGetManagers` と違い、無ければ null を返すだけで新規生成しない
        // (テアダウンのためだけに Manager 群を作ってしまわないようにする)。
        public static CutsceneEditModeManagers PeekManagers() => _managers;

        private static void EnsureManagers() => _managers ??= new CutsceneEditModeManagers();

        private static CutsceneDirectorManagerRefs BuildManagerRefs() => new()
        {
            Audio = _managers.Audio,
            Vfx = _managers.Vfx,
            Ui = _managers.Ui,
            Groups = _managers.Groups,
        };

        // 駆動する Context の一覧: シーン内の検索結果 + PrepareContext で用意したもの(DontSave で検索に出ないもの)。
        // 破棄済みの登録はここで外す。戻り値は使い回しのバッファ(呼び出しのたびに作り直す)。
        private static List<CutsceneDirectorContext> CollectContexts()
        {
            _contextBuffer.Clear();
            _contextBuffer.AddRange(Object.FindObjectsByType<CutsceneDirectorContext>(FindObjectsSortMode.None));
            for (var i = _preparedContexts.Count - 1; i >= 0; i--)
            {
                var prepared = _preparedContexts[i];
                if (prepared == null)
                {
                    _preparedContexts.RemoveAt(i);
                    continue;
                }

                if (prepared.isActiveAndEnabled && !_contextBuffer.Contains(prepared))
                {
                    _contextBuffer.Add(prepared);
                }
            }

            return _contextBuffer;
        }

        // Timeline ウィンドウが今開いている Director(`UnityEditor.Timeline.TimelineEditor.inspectedDirector`)。この asmdef は
        // Unity.Timeline.Editor を参照していないので、reflection で読む(CutsceneEditModeDirectorSetup.FocusTimelineWindowOn と同じ事情)。
        private static readonly System.Reflection.PropertyInfo InspectedDirectorProperty =
            System.Type.GetType("UnityEditor.Timeline.TimelineEditor, Unity.Timeline.Editor")
                ?.GetProperty("inspectedDirector", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        // テスト用の差し替え口(Timeline ウィンドウを開かずに「開いている / いない」を切り替える)。null なら実際の Timeline ウィンドウを見る。
        public static System.Func<PlayableDirector, bool> IsInspectedOverrideForTests;

        private static bool IsInspectedByTimelineWindow(PlayableDirector director)
        {
            if (IsInspectedOverrideForTests != null)
            {
                return IsInspectedOverrideForTests(director);
            }

            if (InspectedDirectorProperty == null)
            {
                return true; // 判定できない Unity の版では、従来どおり書く(片付けのときの復元は効く)
            }

            return (InspectedDirectorProperty.GetValue(null) as PlayableDirector) == director;
        }

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

            var contexts = CollectContexts();
            if (contexts.Count == 0)
            {
                // 駆動する Director が無くなった(破棄された)。カメラへ書いていたなら元の姿勢へ戻す。
                CutsceneEditModeCameraWriter.ResetCapture();
                return;
            }

            var cameraWritten = false;

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
                    // FX-R-04: 「先頭からの再生か」は時間の許容ではなく、再生を始める直前(停止中)の位置で決める。
                    // session.LastTime は停止中の更新ごとに記録した再生位置なので、再生開始の操作時点の位置 = LastTime。
                    // 0 なら先頭から、尺の末尾などから巻き戻って始まった(elapsed < LastTime)ときも先頭からの再生とみなす。
                    // スクラブ(LastTime > 0)してから再生した場合は、更新の間隔や 1 フレームの進みに関係なく途中から。
                    if (session.LastTime <= StartAtZeroEpsilon || elapsed + StartAtZeroEpsilon < session.LastTime)
                    {
                        // 先頭からのプレビュー再生(開始位置が 0 = Play Mode の通常の Play と同じ)。時刻 0 のマーカーも発火する(FC-R-03)。
                        session.EventCursor.Advance(elapsed, true, FireEvent);
                        session.SignalCursor.Advance(elapsed, true, FireSignal);
                        session.ShakeCursor.Advance(elapsed, true, FireShake);
                        session.HapticCursor.Advance(elapsed, true, FireHaptic);
                        session.ExternalCursor.Advance(elapsed, true, director);
                    }
                    else
                    {
                        // 途中から(スクラブしてから再生・一時停止からの再開)。再生を始める直前の位置(session.LastTime)までは
                        // 無音で追い付く(その位置ちょうどのマーカーも無音)。最初の更新までに進んだ区間(LastTime より後)のマーカーは
                        // 発火する(Play Mode の Seek + Tick と同じ。FY-R-05)。
                        session.SilentAdvanceTo(session.LastTime);
                        session.EventCursor.Advance(elapsed, true, FireEvent);
                        session.SignalCursor.Advance(elapsed, true, FireSignal);
                        session.ShakeCursor.Advance(elapsed, true, FireShake);
                        session.HapticCursor.Advance(elapsed, true, FireHaptic);
                        session.ExternalCursor.Advance(elapsed, true, director);
                    }
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
                    session.ExternalCursor.Advance(elapsed, playing, director);
                }

                session.WasPlaying = playing;
                session.LastTime = elapsed;

                // カメラへ書くのは、Timeline ウィンドウがこの Director を開いている間だけ。プレビュー用 Director は Timeline ウィンドウを
                // 閉じても残る(片付くのはシーン切替・Play Mode 突入など)ので、残っている間じゅう書き続けると、確認用シーンのカメラを
                // 手で動かせず、保存するとカットシーンの姿勢が残る。
                if (IsInspectedByTimelineWindow(director))
                {
                    CutsceneEditModeCameraWriter.Apply(director.gameObject);
                    cameraWritten = true;
                }
            }

            if (!cameraWritten)
            {
                // どの Director も Timeline ウィンドウで開かれていない(閉じた・別のものを開いた)。書いていたなら元の姿勢へ戻す。
                CutsceneEditModeCameraWriter.ResetCapture();
            }

            foreach (var staleId in _staleIds)
            {
                _sessions.Remove(staleId);
            }
        }

        // [51] §4.5(FC-4) — 外部 ICutsceneMarker 用カーソル。CutsceneMarkerCursor<T> と同じ「跨いだら進む、
        // fire=false は無音」。Fire の例外は 1 マーカーごとに隔離する。
        internal sealed class ExternalMarkerCursor
        {
            private readonly List<(double Time, ICutsceneMarker Marker)> _items = new();
            private int _cursor;

            public int Count => _items.Count;

            public void Collect(TimelineAsset timeline)
            {
                _items.Clear();
                _cursor = 0;
                if (timeline == null)
                {
                    return;
                }

                foreach (var track in timeline.GetOutputTracks())
                {
                    if (track == null)
                    {
                        continue;
                    }

                    foreach (var marker in track.GetMarkers())
                    {
                        if (marker is ICutsceneMarker external)
                        {
                            _items.Add((marker.time, external));
                        }
                    }
                }

                _items.Sort((a, b) => a.Time.CompareTo(b.Time));
            }

            public void ResetCursor() => _cursor = 0;

            public void Advance(double newElapsed, bool fire, PlayableDirector director)
            {
                while (_cursor < _items.Count && _items[_cursor].Time <= newElapsed)
                {
                    var (time, marker) = _items[_cursor];
                    _cursor++;

                    if (!fire)
                    {
                        continue;
                    }

                    try
                    {
                        var context = new CutsceneMarkerContext(time, newElapsed, director, Handle<CutsceneMarker>.Invalid, true);
                        marker.Fire(in context);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
        }

        // マーカーの発火は 1 件ずつ例外を隔離する(外部マーカーと同じ)。発火が例外で抜けると、その更新でカーソルと
        // 「再生中か」の記録が進まず、次の更新で同じマーカーを鳴らし直してしまう。
        private static void FireEvent(CutsceneEventNotification marker)
        {
            try
            {
                _managers.RaiseEvent(in marker.Event);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void FireSignal(CutsceneSignalNotification marker)
            => Debug.Log($"[DDrive] Cutscene Signal (Edit Mode プレビュー): '{marker.Key}'");

        private static void FireShake(CutsceneShakeNotification marker)
        {
            if (!marker.ShakeId.IsValid)
            {
                return;
            }

            try
            {
                var data = _managers.Registry.ResolveOrPlaceholder<CameraShakeData>(marker.ShakeId.Value);
                _managers.ShakeDriver.Play(data);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void FireHaptic(CutsceneHapticNotification marker)
        {
            if (!marker.HapticId.IsValid)
            {
                return;
            }

            try
            {
                var data = _managers.Registry.ResolveOrPlaceholder<HapticsData>(marker.HapticId.Value);
                _managers.HapticsDriver.Play(data);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                // Play Mode に入る直前に、Shake/Haptics の出力とカメラの書き込みを止める
                // (SceneCameraShakePreviewDriver/EditorHapticsPreviewDriver 自身も同じ通知で自浄する)。
                CutsceneEditModeCameraWriter.ResetCapture();
                // docs/45 P1-5(2026-09-20) — プレビュー用 Director(playOnAwake=true のまま保存された
                // シーンで Play Mode に入ると CutsceneManager を経由せず勝手に再生してしまう)と、そこに
                // Spawn された Model を Play Mode 突入前に消す。
                CutsceneEditModeDirectorSetup.TearDown();
                _sessions.Clear();
            }
        }

        private static void OnActiveSceneChanged(Scene previous, Scene current) => ResetSessions();

        private static void OnPrefabStageChanged(PrefabStage stage) => ResetSessions();

        private static void ResetSessions()
        {
            _sessions.Clear();
            CutsceneEditModeCameraWriter.ResetCapture();
            // docs/45 P1-5(2026-09-20) — シーン切替・Prefab ステージ切替でプレビュー用 Director を残さない。
            CutsceneEditModeDirectorSetup.TearDown();
        }

        private static void TearDown()
        {
            ResetSessions();
            _preparedContexts.Clear();
            IsInspectedOverrideForTests = null;
            _managers?.Dispose();
            _managers = null;
        }

        // テスト専用: 静的 Manager 群とプレビュー用 Director を破棄する。`EnsureManagers`/`EnsureAndGetManagers`
        // を経由するテストが、開いているシーンにプレビュー用ルートを残さないようにする
        // (docs/45 テストの穴 8、2026-09-20)。
        public static void TearDownForTests() => TearDown();
    }
}
