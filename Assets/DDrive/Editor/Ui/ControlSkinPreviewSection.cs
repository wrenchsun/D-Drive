using System;
using System.Collections.Generic;
using DDrive.Editor.Preview;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace DDrive.Editor.Ui
{
    // ButtonSkin / SliderSkin エディタ共通の「状態演出と SE の確認」セクション(2026-09-13)。
    // Editor では UiFx が未 Bind のため、UiInteractable.ForceStateForPreview は色・拡大率・差し替え画像だけを
    // 適用し、状態に入ったときの Tween は流れない。ここで実行時(UiInteractable.PlayStateTween)と同じ規則
    // (EnterTween 優先 → EnterPreset)で実 UiTweenManager に再生させ、SE は PreviewService の実 AudioManager
    // で鳴らす(ADR-4)。
    public sealed class ControlSkinPreviewSection : VisualElement
    {
        public readonly struct SeField
        {
            public readonly string Label;
            public readonly Func<ControlSkinData, AssetId<SeMarker>> Get;

            public SeField(string label, Func<ControlSkinData, AssetId<SeMarker>> get)
            {
                Label = label;
                Get = get;
            }
        }

        private sealed class StateRow
        {
            public ControlState State;
            public Button Pause;
            public Button Stop;
            public Label Status;
        }

        private sealed class SeRow
        {
            public string Label;
            public Button Stop;
        }

        private static readonly ControlState[] States =
        {
            ControlState.Normal, ControlState.Hover, ControlState.Pressed,
            ControlState.Selected, ControlState.Disabled, ControlState.Locked,
        };

        private readonly Func<UiInteractable> _ensurePreview;
        private readonly SeField[] _seFields;
        private readonly TweenTrack[] _presetScratch = new TweenTrack[UiTweenManager.MaxTracksPerTween];
        private readonly VisualElement _stateRows = new();
        private readonly VisualElement _seRows = new();
        private readonly List<StateRow> _stateWidgets = new();
        private readonly List<SeRow> _seWidgets = new();
        private readonly Label _status = new() { style = { opacity = 0.8f, marginTop = 2, marginBottom = 6 } };

        private VisualElement _tracker;
        private ControlSkinData _skin;
        private UiTweenManager _tweens;
        private PreviewService _audio;
        private Handle<UiTweenMarker> _tweenHandle = Handle<UiTweenMarker>.Invalid;
        private ControlState _tweenState;
        private Handle<SeMarker> _seHandle = Handle<SeMarker>.Invalid;
        private string _seLabel;
        private double _lastTime;

        // ensurePreview: 確認用シーンのプレビュー部品を返す(未配置なら配置してから返す)。
        public ControlSkinPreviewSection(Func<UiInteractable> ensurePreview, params SeField[] seFields)
        {
            _ensurePreview = ensurePreview;
            _seFields = seFields ?? Array.Empty<SeField>();

            Add(Header("状態演出の確認(Enter Tween / Enter Preset)"));
            Add(new HelpBox("「▶ 再生」でプレビューの部品をその状態にして、状態に入ったときの演出を実際に再生します(未配置なら自動で配置)。「✎ Tween Editor」は Enter Tween を指定している状態だけ押せます。", HelpBoxMessageType.Info));
            Add(_stateRows);
            Add(Header("SE の試聴"));
            Add(_seRows);
            Add(_status);

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                _lastTime = EditorApplication.timeSinceStartup;
                EditorApplication.update += OnEditorUpdate;
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                EditorApplication.update -= OnEditorUpdate;
                Shutdown();
            });
        }

        public void SetSkin(ControlSkinData skin)
        {
            StopTween();
            StopSe();
            _skin = skin;

            // Inspector 側で Enter Tween / Preset / SE を変えたら行を作り直す(表示名・押せる状態を追従させる)。
            _tracker?.RemoveFromHierarchy();
            _tracker = null;
            if (skin != null)
            {
                _tracker = new VisualElement();
                Add(_tracker);
                _tracker.TrackSerializedObjectValue(new SerializedObject(skin), _ => Rebuild());
            }

            Rebuild();
        }

        private void Rebuild()
        {
            _stateRows.Clear();
            _stateWidgets.Clear();
            _seRows.Clear();
            _seWidgets.Clear();

            if (_skin == null)
            {
                _stateRows.Add(new Label("Skin を選択してください") { style = { opacity = 0.7f } });
                return;
            }

            foreach (var state in States)
            {
                _stateRows.Add(BuildStateRow(state));
            }

            foreach (var field in _seFields)
            {
                _seRows.Add(BuildSeRow(field));
            }

            RefreshWidgets();
        }

        private VisualElement BuildStateRow(ControlState state)
        {
            var v = _skin.Get(state);
            var tween = v.EnterTween.IsValid ? FindData<UiTweenData>(v.EnterTween.Value) : null;

            var row = Row();
            row.Add(new Label(state.ToString()) { style = { width = 70 } });
            row.Add(new Label(Describe(v, tween)) { style = { flexGrow = 1f, opacity = 0.85f } });

            var edit = new Button(() => OpenTweenEditor(tween))
            {
                text = "✎ Tween Editor",
                tooltip = "UI Tween Editor で開く(確認用シーンのプレビュー部品を自動でプレビュー対象にする)",
            };
            edit.SetEnabled(tween != null);
            row.Add(edit);

            var widgets = new StateRow { State = state };
            row.Add(new Button(() => PlayState(state))
            {
                text = "▶ 再生",
                tooltip = "プレビュー部品をこの状態にして、状態に入ったときの演出を再生する",
            });
            widgets.Pause = new Button(TogglePause) { text = "⏸ 一時停止", tooltip = "その場で一時停止 / 再開" };
            row.Add(widgets.Pause);
            widgets.Stop = new Button(StopTween) { text = "■ 停止", tooltip = "途中で止める(最終状態には進めない)" };
            row.Add(widgets.Stop);
            widgets.Status = new Label { style = { marginLeft = 4, opacity = 0.8f, minWidth = 70 } };
            row.Add(widgets.Status);

            _stateWidgets.Add(widgets);
            return row;
        }

        private VisualElement BuildSeRow(SeField field)
        {
            var id = field.Get(_skin);
            var data = id.IsValid ? FindData<SeData>(id.Value) : null;

            var row = Row();
            row.Add(new Label(field.Label) { style = { width = 110 } });
            var name = !id.IsValid ? "(未設定)" : data != null ? NameOf(data) : $"(0x{id.Value:X} が見つかりません)";
            row.Add(new Label(name) { style = { flexGrow = 1f, opacity = 0.85f } });

            var play = new Button(() => PlaySe(field.Get(_skin), field.Label)) { text = "▶ 試聴" };
            play.SetEnabled(data != null);
            row.Add(play);

            var widgets = new SeRow { Label = field.Label };
            widgets.Stop = new Button(StopSe) { text = "■ 停止" };
            row.Add(widgets.Stop);

            _seWidgets.Add(widgets);
            return row;
        }

        private void PlayState(ControlState state)
        {
            if (_skin == null)
            {
                return;
            }

            var control = _ensurePreview?.Invoke();
            if (control == null || !(control.transform is RectTransform rt))
            {
                _status.text = "確認用シーンにプレビューを配置できませんでした";
                return;
            }

            StopTween();
            control.ForceStateForPreview(state);

            var v = _skin.Get(state);
            _tweens ??= new UiTweenManager(EditorAnchorRegistry.Build());
            if (v.EnterTween.IsValid)
            {
                var data = FindData<UiTweenData>(v.EnterTween.Value);
                if (data == null)
                {
                    _status.text = $"{state}: Enter Tween の UiTweenData が見つかりません";
                    return;
                }

                _tweenHandle = _tweens.PlayData(data, rt);
            }
            else if (v.EnterPreset.Preset != UiPreset.None)
            {
                var preset = v.EnterPreset;
                var count = UiPresetFactory.Build(in preset, rt, _presetScratch);
                if (count > 0)
                {
                    _tweenHandle = _tweens.PlayTracks(_presetScratch, count, rt);
                }

                // UiFx.Play(preset) と同じく、プリセット自身に付いた SE も鳴らす。
                if (preset.Se.IsValid)
                {
                    PlaySe(preset.Se, $"{state} のプリセット SE");
                }
            }

            _tweenState = state;
            _status.text = _tweens.IsPlaying(_tweenHandle) ? $"{state} の演出を再生中" : $"{state} の見た目を適用しました(演出なし)";
            SceneView.RepaintAll();
        }

        private void TogglePause()
        {
            if (_tweens != null && _tweens.IsPlaying(_tweenHandle))
            {
                _tweens.SetPaused(_tweenHandle, !_tweens.IsPaused(_tweenHandle));
            }
        }

        private void StopTween()
        {
            _tweens?.Stop(_tweenHandle);
            _tweenHandle = Handle<UiTweenMarker>.Invalid;
        }

        private void PlaySe(AssetId<SeMarker> id, string label)
        {
            var data = id.IsValid ? FindData<SeData>(id.Value) : null;
            if (data == null)
            {
                _status.text = $"{label}: SeData が見つかりません";
                return;
            }

            _audio ??= new PreviewService();
            _audio.Initialize();
            StopSe();
            _seHandle = _audio.PlaySe(data);
            _seLabel = label;

            // Clip が 1 つも無い SeData 等は AudioManager が何も鳴らさない。黙って失敗しないよう理由を出す。
            _status.text = _audio.AudioManager.IsPlaying(_seHandle)
                ? $"{label}: {NameOf(data)} を試聴中"
                : $"{label}: {NameOf(data)} を再生できませんでした(Clip 未設定など。SE の設定を確認してください)";
        }

        private void StopSe()
        {
            if (_audio != null && _audio.IsInitialized)
            {
                _audio.StopAll();
            }

            _seHandle = Handle<SeMarker>.Invalid;
            _seLabel = null;
        }

        private void OpenTweenEditor(UiTweenData tween)
        {
            if (tween == null)
            {
                return;
            }

            var control = _ensurePreview?.Invoke();
            var rt = control != null ? control.transform as RectTransform : null;
            var root = control != null ? control.transform.root.gameObject : null;
            UiTweenEditorWindow.Open(tween, root, rt, control != null ? control.name : null);
        }

        private void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = (float)(now - _lastTime);
            _lastTime = now;
            if (dt <= 0f || dt > 1f)
            {
                return;
            }

            if (_tweens != null && _tweens.ActiveCount > 0)
            {
                _tweens.Tick(dt);
                SceneView.RepaintAll();
            }

            RefreshWidgets();
        }

        private void RefreshWidgets()
        {
            var playing = _tweens != null && _tweens.IsPlaying(_tweenHandle);
            var paused = playing && _tweens.IsPaused(_tweenHandle);
            foreach (var w in _stateWidgets)
            {
                var mine = playing && w.State == _tweenState;
                w.Pause.SetEnabled(mine);
                w.Stop.SetEnabled(mine);
                SetText(w.Pause, mine && paused ? "▶ 再開" : "⏸ 一時停止");
                var status = !mine ? string.Empty : paused ? "⏸ 一時停止" : "● 再生中";
                if (w.Status.text != status)
                {
                    w.Status.text = status;
                }
            }

            var sePlaying = _audio != null && _audio.IsInitialized && _audio.AudioManager.IsPlaying(_seHandle);
            foreach (var s in _seWidgets)
            {
                s.Stop.SetEnabled(sePlaying && s.Label == _seLabel);
            }
        }

        private void Shutdown()
        {
            StopTween();
            _tweens = null;
            _audio?.Dispose();
            _audio = null;
        }

        private static void SetText(Button button, string text)
        {
            if (button.text != text)
            {
                button.text = text;
            }
        }

        private static string Describe(in StateVisual v, UiTweenData tween)
        {
            if (v.EnterTween.IsValid)
            {
                return tween != null ? $"Tween: {NameOf(tween)}" : $"Tween: (0x{v.EnterTween.Value:X} が見つかりません)";
            }

            return v.EnterPreset.Preset != UiPreset.None ? $"Preset: {v.EnterPreset.Preset}" : "演出なし(色・拡大率・画像のみ)";
        }

        private static string NameOf(AssetDataBase data) => string.IsNullOrEmpty(data.DisplayName) ? data.name : data.DisplayName;

        private static VisualElement Row() => new() { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 2 } };

        private static Label Header(string text) => new(text) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } };

        private static T FindData<T>(ulong id) where T : AssetDataBase
        {
            if (id == 0)
            {
                return null;
            }

            foreach (var guid in AssetSearch.FindAssets("t:" + typeof(T).Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.Id == id)
                {
                    return asset;
                }
            }

            return null;
        }
    }
}
