using System;
using System.Collections.Generic;
using System.Text;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Runtime.Model;
using DDrive.Runtime.Presentation;
using R3;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Presentation
{
    // 統合プレビュー(確認用シーンのモデルを ctx.Self にして、実 PresentationManager で同時再生)・Signal の手動送出・環境切替・ログ・単体試聴。
    // 実 Manager は ScenePresentationPreviewDriver が駆動する(ADR-4。ウィンドウはボタン / ログのみ)。再生・停止・リピート・速度は共通基盤の固定バー。
    internal sealed partial class PresentationEditor
    {
        private const int MaxLogLines = 16;

        private bool _paused;
        private bool _rewindNoticeShown;
        private bool _auditioning;
        private PresentationData _audition;
        private Button _pauseButton;
        private Label _statusLabel;
        private Label _logLabel;
        private VisualElement _signalRow;
        private string _signalSig = string.Empty;
        private readonly List<string> _log = new();

        private IDisposable _subMarker;
        private IDisposable _subTrackFired;
        private IDisposable _subCompleted;
        private IDisposable _subCancelled;

        // ── 欄(確認する) ──

        private UxRow CreateModelRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            var field = new ObjectField { objectType = typeof(ModelData), allowSceneObjects = false, tooltip = "ctx.Self として確認用シーンに配置するモデル。Animator が無くても VFX / SE / Shake / Haptic の基準点として使える" };
            field.SetValueWithoutNotify(_model);
            field.RegisterValueChangedCallback(evt => _model = evt.newValue as ModelData);
            box.Add(field);

            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            line.Add(PreviewPlacementButton.Create(
                "配置",
                "確認用シーンを開き、その原点にモデルを配置して ctx.Self にする(未保存、DontSave)",
                PlaceModel));
            line.Add(MakeButton("配置解除", null, () =>
            {
                _preview?.ReleaseModel();
                AppendLog("配置解除");
            }, "配置したモデルを片付ける"));
            foreach (var child in line.Children())
            {
                child.AddToClassList("ux-btn");
            }

            box.Add(line);
            return new PresentationAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        private UxRow CreatePlayControlRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            _pauseButton = MakeButton("⏸ 一時停止", null, TogglePause, "再生中の演出を一時停止 / 再開する");
            line.Add(_pauseButton);
            line.Add(MakeButton("⏮ 最初から", null, RestartFromStart, "一時停止中でも最初から再生し直す"));
            box.Add(line);
            _statusLabel = new Label("■ 停止中");
            _statusLabel.AddToClassList("ux-dim");
            _statusLabel.AddToClassList("ux-pstatus");
            box.Add(_statusLabel);
            UpdatePauseButtonLabel();
            return new PresentationAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        private UxRow CreateLogRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            _logLabel = new Label(string.Join("\n", _log));
            _logLabel.AddToClassList("ux-plog");
            box.Add(_logLabel);
            var clear = MakeButton("ログを消す", "ux-btn--small", () =>
            {
                _log.Clear();
                _logLabel.text = string.Empty;
            }, "ログを空にする");
            box.Add(clear);
            return new PresentationAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        private UxRow CreateEnvironmentRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            var light = new Slider("ライト強度", 0f, 3f) { showInputField = true, tooltip = "確認用シーンの Directional Light(見つかった最初の Light)の強度を切り替える" };
            light.RegisterValueChangedCallback(evt =>
            {
                var l = UnityEngine.Object.FindFirstObjectByType<Light>();
                if (l != null)
                {
                    l.intensity = evt.newValue;
                }
            });
            box.Add(light);
            var bg = new ColorField("背景色(Camera.main)");
            bg.RegisterValueChangedCallback(evt =>
            {
                if (Camera.main != null)
                {
                    Camera.main.backgroundColor = evt.newValue;
                }
            });
            box.Add(bg);
            var note = new Label("スロー再生は固定バーの「速度」(0.1x〜2x)で行います。");
            note.AddToClassList("ux-dim");
            note.AddToClassList("ux-note");
            box.Add(note);
            return new PresentationAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        private UxRow CreateSignalsRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            _signalRow = new VisualElement();
            _signalRow.AddToClassList("ux-toolsline");
            box.Add(_signalRow);
            _signalSig = string.Empty;
            RefreshSignalButtonsIfChanged();
            return new PresentationAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        // ── Signal レーン(手動送出) ──

        private string SignalSignature()
        {
            var tracks = Target?.Tracks;
            if (tracks == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (var i = 0; i < tracks.Length; i++)
            {
                var t = tracks[i];
                if (t.Trigger == TrackTrigger.OnSignal && !string.IsNullOrEmpty(t.SignalKey))
                {
                    sb.Append(t.SignalKey).Append('|');
                }

                if (t.Kind == TrackKind.Signal || t.Kind == TrackKind.Marker)
                {
                    sb.Append('#').Append((int)t.Kind).Append(t.SignalKey).Append('|');
                }
            }

            return sb.ToString();
        }

        private void RefreshSignalButtonsIfChanged()
        {
            if (_signalRow == null)
            {
                return;
            }

            var sig = SignalSignature();
            if (sig == _signalSig)
            {
                return;
            }

            _signalSig = sig;
            _signalRow.Clear();
            var tracks = Target?.Tracks;
            var seen = new HashSet<string>();
            var playing = _preview?.IsPlaying ?? false;
            for (var i = 0; tracks != null && i < tracks.Length; i++)
            {
                var track = tracks[i];
                if (track.Trigger != TrackTrigger.OnSignal || string.IsNullOrEmpty(track.SignalKey) || !seen.Add(track.SignalKey))
                {
                    continue;
                }

                var key = track.SignalKey;
                var button = new Button(() =>
                {
                    _preview?.Signal(key);
                    AppendLog($"手動送出: Signal(\"{key}\")");
                })
                { text = "Signal: " + key, tooltip = "再生中のみ有効です。固定バーの「▶ 再生」を押してから送ってください" };
                button.AddToClassList("ux-btn");
                button.SetEnabled(playing);
                _signalRow.Add(button);
            }

            if (_signalRow.childCount == 0)
            {
                var empty = new Label("(On Signal のトラックがありません。Trigger=OnSignal のトラックを追加するとここにボタンが出ます)");
                empty.AddToClassList("ux-dim");
                empty.AddToClassList("ux-note");
                _signalRow.Add(empty);
            }

            // データ→コードの通知(Signal / Marker トラック)の名前も一覧する。
            var names = new List<string>();
            for (var i = 0; tracks != null && i < tracks.Length; i++)
            {
                if (tracks[i].Kind == TrackKind.Signal || tracks[i].Kind == TrackKind.Marker)
                {
                    names.Add($"{tracks[i].Kind}:{(string.IsNullOrEmpty(tracks[i].SignalKey) ? "(名前なし)" : tracks[i].SignalKey)}@{tracks[i].Time:0.##}s");
                }
            }

            if (names.Count > 0)
            {
                var notify = new Label("データ→コードの通知: " + string.Join(" / ", names));
                notify.AddToClassList("ux-dim");
                notify.AddToClassList("ux-note");
                _signalRow.Add(notify);
            }
        }

        private void UpdateSignalButtonsEnabled(bool playing)
        {
            if (_signalRow == null)
            {
                return;
            }

            foreach (var child in _signalRow.Children())
            {
                if (child is Button button && button.enabledSelf != playing)
                {
                    button.SetEnabled(playing);
                }
            }
        }

        // ── 確認用シーンとモデル ──

        private void OpenPreviewScene(PreviewPlaceMode mode)
        {
            StopMain();
            if (!PreviewPlacement.PrepareScene(mode, VfxPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            // シーンは開けたので、以降はモデル配置だけ行う(モデル未選択なら PlaceModel が案内を出す)。
            PlaceModel(mode, true);
        }

        private void PlaceModel(PreviewPlaceMode mode) => PlaceModel(mode, false);

        private void PlaceModel(PreviewPlaceMode mode, bool sceneAlreadyPrepared)
        {
            if (_preview == null)
            {
                return;
            }

            if (_model == null)
            {
                AppendLog("⚠ モデル(ModelData)を選択してください(未設定でも World / Anchor 基準のトラックだけなら再生できます)");
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
                ? $"確認用モデル '{_model.DisplayName ?? _model.name}' を配置(Animator あり、Anim / Anim2D トラックも再生可)"
                : $"確認用モデル '{_model.DisplayName ?? _model.name}' を配置(Animator なし。VFX / SE / Shake / Haptic の基準点として使用)");
        }

        // ── 再生 ──

        private void StartFresh(string logPrefix)
        {
            if (Target == null)
            {
                AppendLog("⚠ 対象アセット(PresentationData)が未選択です");
                return;
            }

            DestroyAudition();
            StartFreshFor(Target, $"{logPrefix} '{Target.DisplayName ?? Target.name}' を再生");
        }

        private void StartFreshFor(PresentationData data, string logLine)
        {
            if (_preview == null || data == null)
            {
                return;
            }

            DisposeSubscriptions();
            _paused = false;
            _rewindNoticeShown = false;
            // ウィンドウを開いた後に作った / 割り当てた Data(トラックの ID)を解決できるよう、再生のたびに Registry を最新化する。
            EditorAnchorRegistry.Refresh(_preview.Registry);
            _preview.Play(data);
            if (!Mathf.Approximately(Speed, 1f))
            {
                _preview.SetSpeed(Speed);
            }

            SubscribeToCurrent();
            UpdatePauseButtonLabel();
            _timelineContainer?.MarkDirtyRepaint();
            AppendLog(logLine + (_preview.HasSelf ? string.Empty : "(モデル未配置。Self 基準のトラックは対象が見つからず警告のうえ no-op になります)"));
        }

        // 固定バーの ■ / 再生のやり直しの前 / 対象の切替。再生中だったときだけログに残す。
        private void StopCurrentPlayback()
        {
            if (_preview != null && _preview.IsPlaying)
            {
                AppendLog("■ 停止");
            }

            _preview?.StopCurrent();
            _paused = false;
            _rewindNoticeShown = false;
            _auditioning = false;
            UpdatePauseButtonLabel();
        }

        private void RestartFromStart()
        {
            if (Target == null || _preview == null)
            {
                return;
            }

            StopMain();
            PlayMain(false);
        }

        private void TogglePause()
        {
            if (_preview == null)
            {
                return;
            }

            if (!_preview.IsPlaying)
            {
                PlayMain(false);
                return;
            }

            _paused = !_paused;
            _preview.SetPaused(_paused);
            UpdatePauseButtonLabel();
            AppendLog(_paused ? "⏸ 一時停止" : "▶ 再開");
        }

        private void UpdatePauseButtonLabel()
        {
            if (_pauseButton != null)
            {
                _pauseButton.text = _paused ? "▶ 再開" : "⏸ 一時停止";
            }
        }

        // タイムラインの上段のクリック / ドラッグでシーク。実際の再生時間(EffectiveDuration)にクランプする。
        // 巻き戻し(過去への Seek)を検出したら、その再生の最初の 1 回だけログへ注意書きを出す。
        private void SeekToTime(float absoluteSeconds)
        {
            if (Target == null || _preview == null)
            {
                return;
            }

            var duration = PresentationTiming.EffectiveDuration(Target);
            var clamped = Mathf.Clamp(absoluteSeconds, 0f, Mathf.Max(0f, duration));
            var previous = duration > 0f ? Mathf.Clamp01(_preview.NormalizedTime) * duration : 0f;
            if (!_rewindNoticeShown && PresentationPreviewPlayback.IsRewind(previous, clamped))
            {
                _rewindNoticeShown = true;
                AppendLog("⚠ 巻き戻しでは発火済みのトラックは再発火しません。最初から確認するには ⏮");
            }

            _preview.Seek(clamped);
            _timelineContainer?.MarkDirtyRepaint();
        }

        private void UpdateStatus(bool playing)
        {
            if (_statusLabel == null || Target == null)
            {
                return;
            }

            var normalized = PresentationPreviewPlayback.ComputeSeekSliderValue(playing, _preview.NormalizedTime);
            var elapsedSec = normalized * Mathf.Max(0f, PresentationTiming.EffectiveDuration(Target));
            var text = playing
                ? (_paused ? $"⏸ 一時停止中  {normalized:P0} ({elapsedSec:0.00}s)" : $"● 再生中  {normalized:P0} ({elapsedSec:0.00}s)")
                : (Repeat ? "↻ リピート待機" : "■ 停止中");
            if (_auditioning && playing)
            {
                text = "♪ 単体試聴中  " + text.Substring(2);
            }

            if (_statusLabel.text != text)
            {
                _statusLabel.text = text;
            }
        }

        // ── 単体試聴(1 トラックだけを時刻 0 で再生する。一時的な PresentationData を作って実 Manager に渡す) ──

        private void AuditionTrack(int index)
        {
            var tracks = Target?.Tracks;
            if (tracks == null || index < 0 || index >= tracks.Length || _preview == null)
            {
                return;
            }

            StopMain();
            DestroyAudition();
            var track = tracks[index];
            track.Trigger = TrackTrigger.AtTime;
            track.Time = 0f;
            var tmp = ScriptableObject.CreateInstance<PresentationData>();
            tmp.hideFlags = HideFlags.HideAndDontSave;
            tmp.name = "PRES_Audition";
            tmp.Tracks = new[] { track };
            tmp.Interruptible = true;
            tmp.TotalDuration = Mathf.Max(0.5f, PresentationTimelineRange.SuggestedTotalDuration(tmp));
            _audition = tmp;
            _auditioning = true;
            StartFreshFor(tmp, $"♪ 単体試聴: [{index}] {track.Kind} {TrackAssetName(in track)}");
        }

        private void DestroyAudition()
        {
            if (_audition == null)
            {
                return;
            }

            if (_preview != null && _auditioning)
            {
                _preview.StopCurrent();
            }

            UnityEngine.Object.DestroyImmediate(_audition);
            _audition = null;
            _auditioning = false;
        }

        // ── 購読・ログ ──

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

        private void DisposeSubscriptions()
        {
            _subMarker?.Dispose();
            _subMarker = null;
            _subTrackFired?.Dispose();
            _subTrackFired = null;
            _subCompleted?.Dispose();
            _subCompleted = null;
            _subCancelled?.Dispose();
            _subCancelled = null;
        }

        private void AppendLog(string line)
        {
            _log.Add(line);
            while (_log.Count > MaxLogLines)
            {
                _log.RemoveAt(0);
            }

            if (_logLabel != null)
            {
                _logLabel.text = string.Join("\n", _log);
            }
        }

        private void OnDataSignalFromPresentation(string key) => AppendLog($"→ PlayContext.OnSignal(\"{key}\")(Kind=Signal トラック、データ→コード通知)");
    }
}
