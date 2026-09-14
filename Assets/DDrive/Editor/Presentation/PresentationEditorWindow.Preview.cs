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
        private bool _paused;
        private Slider _seekSlider;

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

            var modelRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 4 } };
            modelRow.Add(new Button(PlaceModel) { text = "配置", tooltip = "確認用シーンの原点にモデルを配置して ctx.Self にする(未保存、DontSave)" });
            modelRow.Add(new Button(() =>
            {
                _preview?.ReleaseModel();
                AppendLog("配置解除");
            })
            { text = "配置解除" });
            foldout.Add(modelRow);

            var playRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 4 } };
            playRow.Add(new Button(Play) { text = "▶ 再生" });
            playRow.Add(new Button(TogglePause) { text = "⏸ 一時停止" });
            playRow.Add(new Button(Stop) { text = "■ 停止" });
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

            _seekSlider = new Slider("シーク", 0f, 1f) { showInputField = true, tooltip = "デバッグ用。通過したトラックはまとめて発火する(巻き戻しでは既発火のトラックを再発火しない)" };
            _seekSlider.RegisterValueChangedCallback(evt =>
            {
                if (_target == null || _preview == null)
                {
                    return;
                }

                var duration = PresentationTiming.EffectiveDuration(_target);
                _preview.Seek(evt.newValue * duration);
            });
            foldout.Add(_seekSlider);

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

        private void PlaceModel()
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

            VfxPreviewSceneSetup.OpenOrCreate();
            var animator = _preview.SpawnModel(_model, Vector3.zero, Quaternion.identity);
            AppendLog(animator != null
                ? $"確認用モデル '{_model.DisplayName ?? _model.name}' を配置(Animator あり、Anim/Anim2D トラックも再生可)"
                : $"確認用モデル '{_model.DisplayName ?? _model.name}' を配置(Animator なし。VFX/SE/Shake/Haptic の基準点として使用)");
        }

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

            DisposeSubscriptions();
            _paused = false;
            _preview.Play(_target);
            SubscribeToCurrent();
            AppendLog($"▶ '{_target.DisplayName ?? _target.name}' を再生" + (_preview.HasSelf ? string.Empty : "(モデル未配置。Self 基準のトラックは対象が見つからず警告のうえ no-op になります)"));
        }

        private void Stop()
        {
            _preview?.StopCurrent();
            _paused = false;
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
            AppendLog(_paused ? "⏸ 一時停止" : "▶ 再開");
        }

        private void StopPreview()
        {
            _preview?.StopCurrent();
            _paused = false;
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
