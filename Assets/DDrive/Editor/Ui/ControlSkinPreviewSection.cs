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
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace DDrive.Editor.Ui
{
    // ButtonSkin / SliderSkin エディタ共通の設定欄(2026-09-13 追加、2026-09-14 改修)。
    // 各状態の Enter Tween / Enter Preset 欄のすぐ上に演出の再生ボタンを、各 SE 欄の同じ行に試聴ボタンを置く
    // (当初は Inspector とプレビュー一覧が上下に離れていて操作しづらかった)。それ以外の項目は通常の
    // PropertyField で元の順に並べる。
    // Editor では UiFx が未 Bind のため ForceStateForPreview は色・拡大率・画像しか適用しない。状態に入ったときの
    // 演出はここで実行時(UiInteractable.PlayStateTween)と同じ規則(EnterTween 優先 → EnterPreset、プリセット付属
    // SE も鳴らす)で実 UiTweenManager に再生させ、SE は PreviewService の実 AudioManager で鳴らす(ADR-4)。
    public sealed class ControlSkinPreviewSection : VisualElement
    {
        public readonly struct SeField
        {
            public readonly string PropertyName;
            public readonly Func<ControlSkinData, AssetId<SeMarker>> Get;

            public SeField(string propertyName, Func<ControlSkinData, AssetId<SeMarker>> get)
            {
                PropertyName = propertyName;
                Get = get;
            }

            public string Label => ObjectNames.NicifyVariableName(PropertyName);
        }

        private sealed class StateRow
        {
            public ControlState State;
            public Label Summary;
            public Button Edit;
            public Button Pause;
            public Button Stop;
            public Label Status;
        }

        private sealed class SeRow
        {
            public SeField Field;
            public Button Play;
            public Button Stop;
        }

        private static readonly Dictionary<string, ControlState> StateByProperty = new()
        {
            { nameof(ControlSkinData.Normal), ControlState.Normal },
            { nameof(ControlSkinData.Hover), ControlState.Hover },
            { nameof(ControlSkinData.Pressed), ControlState.Pressed },
            { nameof(ControlSkinData.Selected), ControlState.Selected },
            { nameof(ControlSkinData.Disabled), ControlState.Disabled },
            { nameof(ControlSkinData.Locked), ControlState.Locked },
        };

        // 演出の欄は再生ボタンの直下に常に出し、色・拡大率・画像は折りたたむ(開閉は状態ごとに保持)。
        private static readonly HashSet<string> TweenFieldNames = new() { nameof(StateVisual.EnterTween), nameof(StateVisual.EnterPreset) };
        private static readonly Dictionary<ControlState, bool> LookExpanded = new();

        private readonly Func<UiInteractable> _ensurePreview;
        private readonly SeField[] _seFields;
        private readonly TweenTrack[] _presetScratch = new TweenTrack[UiTweenManager.MaxTracksPerTween];
        private readonly VisualElement _body = new();
        private readonly List<StateRow> _stateWidgets = new();
        private readonly List<SeRow> _seWidgets = new();
        private readonly Label _status = new() { style = { opacity = 0.8f, marginTop = 2, marginBottom = 4, whiteSpace = WhiteSpace.Normal } };

        private ControlSkinData _skin;
        private UiTweenManager _tweens;
        private PreviewService _audio;
        private Handle<UiTweenMarker> _tweenHandle = Handle<UiTweenMarker>.Invalid;
        private ControlState _tweenState;
        private Handle<SeMarker> _seHandle = Handle<SeMarker>.Invalid;
        private string _sePropertyName;
        private double _lastTime;

        // ensurePreview: 確認用シーンのプレビュー部品を返す(未配置なら配置してから返す)。
        public ControlSkinPreviewSection(Func<UiInteractable> ensurePreview, params SeField[] seFields)
        {
            _ensurePreview = ensurePreview;
            _seFields = seFields ?? Array.Empty<SeField>();

            Add(new HelpBox("各状態の「▶ 再生」でプレビューの部品をその状態にし、状態に入ったときの演出(すぐ下の Enter Tween / Enter Preset)を再生します。未配置なら自動で「確認用シーンに配置」します。SE 欄の右の「▶」で試聴できます。", HelpBoxMessageType.Info));
            Add(_status);
            Add(_body);

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
            _status.text = string.Empty;

            _body.Unbind();
            _body.Clear();
            _stateWidgets.Clear();
            _seWidgets.Clear();

            if (skin == null)
            {
                _body.Add(new Label("Skin を選択してください") { style = { opacity = 0.7f } });
                return;
            }

            var seByName = new Dictionary<string, SeField>();
            foreach (var field in _seFields)
            {
                seByName[field.PropertyName] = field;
            }

            var so = new SerializedObject(skin);
            var it = so.GetIterator();
            var enterChildren = true;
            var statesHeaderAdded = false;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (it.propertyPath == "m_Script")
                {
                    continue;
                }

                var prop = it.Copy();
                if (StateByProperty.TryGetValue(prop.name, out var state))
                {
                    if (!statesHeaderAdded)
                    {
                        _body.Add(Header("状態別ビジュアル"));
                        statesHeaderAdded = true;
                    }

                    _body.Add(BuildStateBlock(prop, state));
                }
                else if (seByName.TryGetValue(prop.name, out var seField))
                {
                    _body.Add(BuildSeRow(prop, seField));
                }
                else
                {
                    _body.Add(new PropertyField(prop));
                }
            }

            _body.Bind(so);

            // 欄を編集したら見出しの要約・押せるボタンを追従させる(欄自体は作り直さない = 入力中のフォーカスを奪わない)。
            var tracker = new VisualElement();
            _body.Add(tracker);
            tracker.TrackSerializedObjectValue(so, _ => RefreshSummaries());

            RefreshSummaries();
            RefreshWidgets();
        }

        private VisualElement BuildStateBlock(SerializedProperty prop, ControlState state)
        {
            var block = new VisualElement
            {
                style =
                {
                    marginTop = 6, paddingLeft = 6, paddingTop = 2, paddingBottom = 4,
                    borderLeftWidth = 3, borderLeftColor = new Color(0.35f, 0.6f, 0.95f, 0.8f),
                },
            };

            var w = new StateRow { State = state };
            var header = Row();
            header.Add(new Label(state.ToString()) { style = { unityFontStyleAndWeight = FontStyle.Bold, width = 70 } });
            w.Summary = new Label { style = { flexGrow = 1f, opacity = 0.8f } };
            header.Add(w.Summary);
            header.Add(new Button(() => PlayState(state))
            {
                text = "▶ 再生",
                tooltip = "プレビューの部品をこの状態にして、状態に入ったときの演出を再生する",
            });
            w.Pause = new Button(TogglePause) { text = "⏸ 一時停止", tooltip = "その場で一時停止 / 再開" };
            header.Add(w.Pause);
            w.Stop = new Button(StopTween) { text = "■ 停止", tooltip = "途中で止める(最終状態には進めない)" };
            header.Add(w.Stop);
            w.Status = new Label { style = { marginLeft = 4, opacity = 0.8f, minWidth = 64 } };
            header.Add(w.Status);
            block.Add(header);

            var look = new Foldout { text = "見た目(Tint / Scale / 画像)", value = LookExpanded.TryGetValue(state, out var open) && open };
            look.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == look)
                {
                    LookExpanded[state] = evt.newValue;
                }
            });

            var child = prop.Copy();
            var end = prop.GetEndProperty();
            if (child.NextVisible(true))
            {
                do
                {
                    if (SerializedProperty.EqualContents(child, end))
                    {
                        break;
                    }

                    var field = new PropertyField(child.Copy());
                    if (child.name == nameof(StateVisual.EnterTween))
                    {
                        // 開く対象の欄の横に置く(見出し行に置くと横に長くなり、何を開くのかも分かりづらい)。
                        var tweenRow = Row();
                        field.style.flexGrow = 1f;
                        tweenRow.Add(field);
                        w.Edit = new Button(() => OpenTweenEditor(state))
                        {
                            text = "✎ Tween Editor",
                            tooltip = "この UiTweenData を UI Tween Editor で開く(プレビューの部品を対象にする)",
                        };
                        tweenRow.Add(w.Edit);
                        block.Add(tweenRow);
                    }
                    else if (TweenFieldNames.Contains(child.name))
                    {
                        block.Add(field);
                    }
                    else
                    {
                        look.Add(field);
                    }
                }
                while (child.NextVisible(false));
            }

            block.Add(look);
            _stateWidgets.Add(w);
            return block;
        }

        private VisualElement BuildSeRow(SerializedProperty prop, SeField field)
        {
            var row = Row();
            row.style.alignItems = Align.FlexEnd; // [Header] 付きの欄でもボタンを値の行に揃える
            row.Add(new PropertyField(prop) { style = { flexGrow = 1f } });

            var w = new SeRow { Field = field };
            w.Play = new Button(() => PlaySe(field.Get(_skin), field.PropertyName)) { text = "▶", tooltip = "試聴" };
            row.Add(w.Play);
            w.Stop = new Button(StopSe) { text = "■", tooltip = "停止" };
            row.Add(w.Stop);

            _seWidgets.Add(w);
            return row;
        }

        private void RefreshSummaries()
        {
            if (_skin == null)
            {
                return;
            }

            foreach (var w in _stateWidgets)
            {
                var v = _skin.Get(w.State);
                var tween = v.EnterTween.IsValid ? FindData<UiTweenData>(v.EnterTween.Value) : null;
                var text = Describe(v, tween);
                if (w.Summary.text != text)
                {
                    w.Summary.text = text;
                }

                w.Edit?.SetEnabled(tween != null);
            }

            foreach (var w in _seWidgets)
            {
                var id = w.Field.Get(_skin);
                w.Play.SetEnabled(id.IsValid && FindData<SeData>(id.Value) != null);
            }
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
                    PlaySe(preset.Se, null);
                }
            }

            _tweenState = state;
            _lastTime = EditorApplication.timeSinceStartup;
            _status.text = _tweens.IsPlaying(_tweenHandle) ? $"{state} の演出を再生中(Game ビューで確認)" : $"{state} の見た目を適用しました(演出なし)";
            InternalEditorUtility.RepaintAllViews();
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
            // 終了済み Handle に Stop すると InstanceStore が「Invalid handle access」を警告するため、再生中だけ止める。
            if (_tweens != null && _tweens.IsPlaying(_tweenHandle))
            {
                _tweens.Stop(_tweenHandle);
            }

            _tweenHandle = Handle<UiTweenMarker>.Invalid;
        }

        // propertyName=null はプリセット付属 SE(行に対応するボタンが無い)。
        private void PlaySe(AssetId<SeMarker> id, string propertyName)
        {
            var label = propertyName != null ? ObjectNames.NicifyVariableName(propertyName) : "プリセットの SE";
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
            _sePropertyName = propertyName;

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
            _sePropertyName = null;
        }

        private void OpenTweenEditor(ControlState state)
        {
            if (_skin == null)
            {
                return;
            }

            var id = _skin.Get(state).EnterTween;
            var tween = id.IsValid ? FindData<UiTweenData>(id.Value) : null;
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
            if (dt <= 0f)
            {
                return;
            }

            if (_tweens != null && _tweens.ActiveCount > 0)
            {
                _tweens.Tick(Mathf.Min(dt, 0.1f));

                // Edit Mode の Game ビューは自動では再描画されないため、演出中は毎フレーム描き直す
                // (これが無いと Game ビューには最後の姿しか映らず「再生されない」ように見える)。
                InternalEditorUtility.RepaintAllViews();
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
                s.Stop.SetEnabled(sePlaying && s.Field.PropertyName == _sePropertyName);
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

            return v.EnterPreset.Preset != UiPreset.None ? $"Preset: {v.EnterPreset.Preset}" : "演出なし";
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
