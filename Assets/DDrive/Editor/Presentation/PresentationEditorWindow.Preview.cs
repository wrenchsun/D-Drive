using DDrive.Editor.Common;
using DDrive.Editor.Preview;
using DDrive.Runtime.Model;
using DDrive.Runtime.Presentation;
using R3;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Presentation
{
    // [08_presentation.md] §4 の「統合プレビュー」「Signal レーンの手動発火」「環境切替」。
    // 実 PresentationManager は ScenePresentationPreviewDriver が駆動する(ADR-4。ウィンドウはボタン/ログのみ)。
    public sealed partial class PresentationEditorWindow
    {
        // U-7(2026-09-17): シークバーは AnimEditorWindow(3-3)と同じ形(暗い背景 + 目盛り付きバー + 白い
        // 再生ヘッド + クリックでシーク)にするため、UI Toolkit の Slider をやめて SeekBarGui(共通ヘルパー)で
        // 描く IMGUIContainer に置き換えた。ピクセル高さも AnimEditorWindow.TimelineHeight と同じ値。
        private const float SeekBarHeight = 62f;

        private bool _paused;
        private IMGUIContainer _seekBarContainer;
        private Button _pauseButton;

        // 巻き戻し(過去への Seek)では発火済みトラックが再発火しない([08] Runtime 実装メモ)ことの注意書きを、
        // 1 回の再生につき最初の巻き戻しだけログへ出す(要望: ツールチップだけでは気づかれにくい)。
        private bool _rewindNoticeShown;

        private void BuildPreviewSection(VisualElement root)
        {
            var foldout = new Foldout { text = "統合プレビュー(モデル選択 → 実 Manager で同時再生)", value = true };
            root.Add(foldout);

            _modelField = new ObjectField("モデル選択(ModelData)")
            {
                objectType = typeof(ModelData),
                tooltip = "ctx.Self として確認用シーンに配置するモデル。Animator が無くても VFX/SE/Shake/Haptic の基準点として使える",
            };
            _modelField.SetValueWithoutNotify(_model);
            _modelField.RegisterValueChangedCallback(evt => _model = evt.newValue as ModelData);
            foldout.Add(_modelField);

            var modelRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginBottom = 4 } }; // [09] §7.1
            modelRow.Add(PreviewPlacementButton.Create(
                "配置",
                "確認用シーンを開き、その原点にモデルを配置して ctx.Self にする(未保存、DontSave)",
                PlaceModel));
            modelRow.Add(new Button(() =>
            {
                _preview?.ReleaseModel();
                AppendLog("配置解除");
            })
            { text = "配置解除" });
            foldout.Add(modelRow);

            var playRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 4 } };
            playRow.Add(new Button(Play) { text = "▶ 再生", tooltip = "一時停止中は再開します。最初からやり直すには「⏮ 最初から」か「■ 停止」→「▶ 再生」" });
            _pauseButton = new Button(TogglePause) { text = "⏸ 一時停止" };
            playRow.Add(_pauseButton);
            playRow.Add(new Button(Stop) { text = "■ 停止" });
            playRow.Add(new Button(Restart) { text = "⏮ 最初から", tooltip = "一時停止中でも最初から再生し直します" });
            var loopToggle = new Toggle("ループ") { value = _loopPreview, tooltip = "完了したら自動でもう一度再生する" };
            loopToggle.RegisterValueChangedCallback(evt => _loopPreview = evt.newValue);
            playRow.Add(loopToggle);
            _statusLabel = new Label("■ 停止中") { style = { marginLeft = 12, opacity = 0.8f } };
            playRow.Add(_statusLabel);
            foldout.Add(playRow);

            var speed = new Slider("速度(0.1x〜2x、スロー再生)", 0.1f, 2f) { value = _speed, showInputField = true };
            speed.RegisterValueChangedCallback(evt =>
            {
                _speed = evt.newValue;
                _preview?.SetSpeed(_speed);
            });
            foldout.Add(speed);

            _seekBarContainer = new IMGUIContainer(DrawSeekBar)
            {
                style = { height = SeekBarHeight },
                tooltip = "デバッグ用。クリックでシーク。通過したトラックはまとめて発火する(巻き戻しでは既発火のトラックを再発火しない)。再生中・一時停止中は現在位置に追従します",
            };
            foldout.Add(_seekBarContainer);

            var signalFoldout = new Foldout { text = "Signal レーン(手動発火)", value = true };
            _signalRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            signalFoldout.Add(_signalRow);
            foldout.Add(signalFoldout);

            BuildEnvironmentSection(foldout);
        }

        private void BuildEnvironmentSection(VisualElement root)
        {
            var envFoldout = new Foldout { text = "環境切替(確認用シーンの既存ライト・背景)", value = false };

            var lightSlider = new Slider("ライト強度", 0f, 3f) { showInputField = true, tooltip = "確認用シーンの Directional Light(見つかった最初の Light)の強度を切り替える" };
            lightSlider.RegisterValueChangedCallback(evt =>
            {
                var light = Object.FindFirstObjectByType<Light>();
                if (light != null)
                {
                    light.intensity = evt.newValue;
                }
            });
            envFoldout.Add(lightSlider);

            var bgField = new ColorField("背景色(Camera.main)");
            bgField.RegisterValueChangedCallback(evt =>
            {
                if (Camera.main != null)
                {
                    Camera.main.backgroundColor = evt.newValue;
                }
            });
            envFoldout.Add(bgField);

            envFoldout.Add(new HelpBox("スロー再生は上の「速度」スライダー(0.1x〜2x)で行います。", HelpBoxMessageType.Info));
            root.Add(envFoldout);
        }

        // ── 再生 ──

        // U-6(2026-09-17): 「確認用シーンを開く」の実体。他のエディタ(Model / Anim / Anim2D)と同じ
        // 「止める → 確認用シーンを開く → 配置する」の順に揃える。
        private void OpenPreviewScene(PreviewPlaceMode mode)
        {
            Stop();
            if (!PreviewPlacement.PrepareScene(mode, VfxPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            // シーンは開けたので、以降はモデル配置だけ行う(モデル未選択なら PlaceModel が案内を出す)。
            PlaceModel(mode, sceneAlreadyPrepared: true);
        }

        private void PlaceModel(PreviewPlaceMode mode) => PlaceModel(mode, sceneAlreadyPrepared: false);

        private void PlaceModel(PreviewPlaceMode mode, bool sceneAlreadyPrepared)
        {
            if (_preview == null)
            {
                return;
            }

            if (_model == null)
            {
                AppendLog("⚠ モデル(ModelData)を選択してください(未設定でも World/Anchor 基準のトラックだけなら再生できます)");
                return;
            }

            if (!sceneAlreadyPrepared && !PreviewPlacement.PrepareScene(mode, VfxPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            if (PreviewPlacement.IsPersistent(mode))
            {
                // 本配置は Manager が追跡しない実体(Prefab リンク付き)にする。ctx.Self には使わない。
                var placed = PreviewPlacement.PlacePrefabPersistent(_model.Prefab, Vector3.zero, Quaternion.identity);
                AppendLog(placed != null
                    ? $"モデル '{_model.DisplayName ?? _model.name}' をこのシーンに本配置しました(プレビュー対象=ctx.Self にはなりません)"
                    : "⚠ 本配置に失敗しました(ModelData に Prefab がありません)");
                return;
            }

            var animator = _preview.SpawnModel(_model, Vector3.zero, Quaternion.identity);
            PreviewPlacement.Focus(_preview.SelfRoot != null ? _preview.SelfRoot.gameObject : null);
            AppendLog(animator != null
                ? $"確認用モデル '{_model.DisplayName ?? _model.name}' を配置(Animator あり、Anim/Anim2D トラックも再生可)"
                : $"確認用モデル '{_model.DisplayName ?? _model.name}' を配置(Animator なし。VFX/SE/Shake/Haptic の基準点として使用)");
        }

        // 5-4 追補(2026-09-14) — ユーザー報告「一時停止から再生すると最初から再生になっている」対応:
        // 一時停止中(handle が有効なまま _paused=true)の「▶ 再生」は再開にする。最初からやり直したい場合は
        // 「⏮ 最初から」(Restart)か「■ 停止」→「▶ 再生」を使う。判断自体は PresentationPreviewPlayback
        // (ウィンドウを起動せずテスト可能な純粋関数)に切り出した。
        private void Play()
        {
            if (_target == null)
            {
                AppendLog("⚠ 対象アセット(PresentationData)が未選択です");
                return;
            }

            if (_preview == null)
            {
                return;
            }

            if (PresentationPreviewPlayback.DecideOnPlay(_preview.IsPlaying, _paused) == PresentationPreviewPlayback.PlayAction.Resume)
            {
                Resume();
                return;
            }

            StartFresh("▶");
        }

        // 一時停止中でも最初から再生し直す(「▶ 再生」が再開になったため、明示的なやり直し手段として追加)。
        private void Restart()
        {
            if (_target == null || _preview == null)
            {
                return;
            }

            StartFresh("⏮");
        }

        private void StartFresh(string logPrefix)
        {
            DisposeSubscriptions();
            _paused = false;
            _rewindNoticeShown = false;
            _preview.Play(_target);
            SubscribeToCurrent();
            _seekBarContainer?.MarkDirtyRepaint();
            UpdatePauseButtonLabel();
            AppendLog($"{logPrefix} '{_target.DisplayName ?? _target.name}' を再生" + (_preview.HasSelf ? string.Empty : "(モデル未配置。Self 基準のトラックは対象が見つからず警告のうえ no-op になります)"));
        }

        private void Resume()
        {
            _paused = false;
            _preview.SetPaused(false);
            UpdatePauseButtonLabel();
            AppendLog("▶ 再開");
        }

        private void Stop()
        {
            _preview?.StopCurrent();
            _paused = false;
            _rewindNoticeShown = false;
            UpdatePauseButtonLabel();
            AppendLog("■ 停止");
        }

        private void TogglePause()
        {
            if (_preview == null)
            {
                return;
            }

            if (!_preview.IsPlaying)
            {
                Play();
                return;
            }

            _paused = !_paused;
            _preview.SetPaused(_paused);
            UpdatePauseButtonLabel();
            AppendLog(_paused ? "⏸ 一時停止" : "▶ 再開");
        }

        // 一時停止中は「▶ 再開」に表示を切り替える(要望: ボタンの見た目で状態が分かるように)。
        private void UpdatePauseButtonLabel()
        {
            if (_pauseButton != null)
            {
                _pauseButton.text = _paused ? "▶ 再開" : "⏸ 一時停止";
            }
        }

        // タイムライン(ルーラーのクリック/ドラッグ)とシークバー(クリック)の両方から呼ぶ共通のシーク処理。
        // 実際の再生時間(EffectiveDuration。表示用の DisplayDuration ではない)にクランプする。巻き戻し
        // (過去への Seek)を検出したら、その再生の最初の 1 回だけログへ注意書きを出す。
        private void SeekToTime(float absoluteSeconds)
        {
            if (_target == null || _preview == null)
            {
                return;
            }

            var duration = PresentationTiming.EffectiveDuration(_target);
            var clamped = Mathf.Clamp(absoluteSeconds, 0f, Mathf.Max(0f, duration));
            var previous = duration > 0f ? Mathf.Clamp01(_preview.NormalizedTime) * duration : 0f;

            if (!_rewindNoticeShown && PresentationPreviewPlayback.IsRewind(previous, clamped))
            {
                _rewindNoticeShown = true;
                AppendLog("⚠ 巻き戻しでは発火済みのトラックは再発火しません。最初から確認するには ⏮");
            }

            _preview.Seek(clamped);
            _seekBarContainer?.MarkDirtyRepaint();
        }

        // シークバー(U-7): AnimEditorWindow.DrawTimeline と同じ形(SeekBarGui 共通ヘルパー)で描く。
        // Presentation にはトラック編集用の詳細タイムライン(PresentationEditorWindow.Tracks.cs、ズーム/パン/
        // 複数レーン)が別にあるため、こちらは再生位置の確認・簡易シークに絞った単純な 1 本のバー。
        private void DrawSeekBar()
        {
            var rect = GUILayoutUtility.GetRect(100, SeekBarHeight, GUILayout.ExpandWidth(true));
            SeekBarGui.DrawBackground(rect);
            if (_target == null)
            {
                GUI.Label(rect, "対象アセットが未選択です", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var duration = Mathf.Max(0.01f, PresentationTiming.EffectiveDuration(_target));
            var totalUnits = Mathf.Max(1, Mathf.CeilToInt(duration));
            var bar = SeekBarGui.DrawBar(rect, totalUnits, f => $"{f}s");

            GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, rect.width - 12f, 14f),
                $"0s  —  {duration:0.##}s   クリック: シーク",
                EditorStyles.miniLabel);

            var normalized = _preview?.NormalizedTime ?? -1f;
            SeekBarGui.DrawPlayhead(bar, normalized);

            var evt = Event.current;
            if (SeekBarGui.TryHandleClickSeek(rect, bar, evt, out var t))
            {
                SeekToTime(t * duration);
                evt.Use();
            }
        }

        private void StopPreview()
        {
            _preview?.StopCurrent();
            _paused = false;
            _rewindNoticeShown = false;
            DisposeSubscriptions();
        }

        private void SubscribeToCurrent()
        {
            _subMarker = _preview.OnMarker.Subscribe(name => AppendLog($"Marker: {name}"));
            _subTrackFired = _preview.OnTrackFired.Subscribe(t => AppendLog(
                t.Trigger == TrackTrigger.OnSignal
                    ? $"Fired: {t.Kind}(onSignal:{t.SignalKey})"
                    : $"Fired: {t.Kind}(t={t.Time:0.##}s)"));
            _subCompleted = _preview.OnCompleted.Subscribe(_ => AppendLog("● 完了"));
            _subCancelled = _preview.OnCancelled.Subscribe(_ => AppendLog("■ Cancel"));
        }

        // ── Signal レーン(手動発火) ──

        private void RefreshSignalButtons()
        {
            if (_signalRow == null)
            {
                return;
            }

            _signalRow.Clear();
            if (_target?.Tracks == null)
            {
                return;
            }

            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var track in _target.Tracks)
            {
                if (track.Trigger != TrackTrigger.OnSignal || string.IsNullOrEmpty(track.SignalKey) || !seen.Add(track.SignalKey))
                {
                    continue;
                }

                var key = track.SignalKey;
                var button = new Button(() =>
                {
                    _preview?.Signal(key);
                    AppendLog($"手動発火: Signal(\"{key}\")");
                })
                { text = $"Signal: {key}" };
                button.style.marginRight = 2;
                _signalRow.Add(button);
            }

            if (_signalRow.childCount == 0)
            {
                _signalRow.Add(new Label("(OnSignal トラックがありません)") { style = { opacity = 0.6f } });
            }
        }
    }
}
