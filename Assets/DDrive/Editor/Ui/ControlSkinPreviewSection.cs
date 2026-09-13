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
using Image = UnityEngine.UI.Image;
using Object = UnityEngine.Object;

namespace DDrive.Editor.Ui
{
    // ButtonSkin / SliderSkin エディタ共通の設定欄(2026-09-13 追加、2026-09-14 改修)。
    // - 各状態の Enter Tween / Enter Preset 欄のすぐ上に演出の再生ボタン、各 SE 欄の同じ行に試聴ボタンを置く
    // - 「状態遷移」: Normal→Hover→Pressed… のような遷移を自動で順に再生し、画像の差し替え・演出・SE を通しで見る
    // - 「当たり判定」: 確認用シーンのプレビューに赤い半透明の枠を重ね(Game / Scene ビュー)、SceneView の四辺の
    //   ハンドルで HitAreaExpand をドラッグ調整できる
    // それ以外の項目は通常の PropertyField で元の順に並べる。
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
        }

        public readonly struct TransitionStep
        {
            public readonly ControlState State;
            public readonly string SePropertyName;

            public TransitionStep(ControlState state, string sePropertyName = null)
            {
                State = state;
                SePropertyName = sePropertyName;
            }
        }

        public readonly struct TransitionSequence
        {
            public readonly string Name;
            public readonly TransitionStep[] Steps;

            public TransitionSequence(string name, params TransitionStep[] steps)
            {
                Name = name;
                Steps = steps ?? Array.Empty<TransitionStep>();
            }
        }

        // 本体以外の当たり判定(SliderSkin のつまみ等)。「当たり判定を表示」で本体と一緒に枠とハンドルを出す。
        public readonly struct HitAreaTarget
        {
            public readonly Func<RectTransform> Target;
            public readonly Func<ControlSkinData, Vector4> Get;
            public readonly Action<ControlSkinData, Vector4> AddDelta;

            public HitAreaTarget(Func<RectTransform> target, Func<ControlSkinData, Vector4> get, Action<ControlSkinData, Vector4> addDelta)
            {
                Target = target;
                Get = get;
                AddDelta = addDelta;
            }
        }

        public sealed class Options
        {
            public HitAreaTarget[] ExtraHitAreas = Array.Empty<HitAreaTarget>();

            // 確認用シーンのプレビュー部品(未配置なら配置してから返す)。
            public Func<UiInteractable> EnsurePreview;

            // 現在のプレビュー部品(配置しない。当たり判定の枠・ハンドル表示用)。
            public Func<UiInteractable> CurrentPreview;

            public SeField[] SeFields = Array.Empty<SeField>();
            public TransitionSequence[] Sequences = Array.Empty<TransitionSequence>();

            // プレビュー部品のスプライトアニメをこの欄が毎フレーム進めるか(部品側で Advance を回すエディタは false)。
            public bool TickPreviewVisuals = true;
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

        private const string HitOverlayName = "[D-Drive] HitArea";

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

        private readonly Options _options;
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

        // 状態遷移の自動再生。
        private PopupField<string> _seqPopup;
        private Button _seqPause;
        private Button _seqStop;
        private Label _seqStatus;
        private int _seqIndex = -1;
        private int _seqStep;
        private float _seqTimer;
        private bool _seqPaused;
        private ControlState _seqPrevState;
        private float _seqInterval = 0.7f;
        private bool _seqLoop;
        private bool _seqWithSe = true;

        // 当たり判定の表示。
        private bool _showHitArea;

        public ControlSkinPreviewSection(Options options)
        {
            _options = options ?? new Options();

            Add(new HelpBox("各状態の「▶ 再生」でプレビューの部品をその状態にし、状態に入ったときの演出(すぐ下の Enter Tween / Enter Preset)を再生します。「状態遷移」は複数の状態を順に自動で切り替えます。未配置なら自動で「確認用シーンに配置」します。SE 欄の右の「▶」で試聴できます。動きは Game ビューで見てください。", HelpBoxMessageType.Info));
            Add(_status);
            Add(_body);

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                _lastTime = EditorApplication.timeSinceStartup;
                EditorApplication.update += OnEditorUpdate;
                SceneView.duringSceneGui += OnSceneGui;
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                EditorApplication.update -= OnEditorUpdate;
                SceneView.duringSceneGui -= OnSceneGui;
                Shutdown();
            });
        }

        public void SetSkin(ControlSkinData skin)
        {
            StopSequence();
            StopSe();
            _skin = skin;
            _status.text = string.Empty;

            _body.Unbind();
            _body.Clear();
            _stateWidgets.Clear();
            _seWidgets.Clear();
            _seqPopup = null;

            if (skin == null)
            {
                _body.Add(new Label("Skin を選択してください") { style = { opacity = 0.7f } });
                return;
            }

            var seByName = new Dictionary<string, SeField>();
            foreach (var field in _options.SeFields)
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
                        if (_options.Sequences.Length > 0)
                        {
                            _body.Add(BuildTransitionBlock());
                        }

                        statesHeaderAdded = true;
                    }

                    _body.Add(BuildStateBlock(prop, state));
                }
                else if (seByName.TryGetValue(prop.name, out var seField))
                {
                    _body.Add(BuildSeRow(prop, seField));
                }
                else if (prop.name == nameof(ControlSkinData.HitAreaExpand))
                {
                    _body.Add(new PropertyField(prop));
                    _body.Add(BuildHitAreaRow());
                }
                else
                {
                    _body.Add(new PropertyField(prop));
                }
            }

            _body.Bind(so);

            // 欄を編集したら見出しの要約・押せるボタン・当たり判定の枠を追従させる
            // (欄自体は作り直さない = 入力中のフォーカスを奪わない)。
            var tracker = new VisualElement();
            _body.Add(tracker);
            tracker.TrackSerializedObjectValue(so, _ =>
            {
                RefreshSummaries();
                ReapplyPreviewVisuals();
                UpdateHitOverlay();
                SceneView.RepaintAll();
            });

            RefreshSummaries();
            RefreshWidgets();
            UpdateHitOverlay();
        }

        // ── 状態ごとの箱 ──

        private VisualElement BuildStateBlock(SerializedProperty prop, ControlState state)
        {
            var block = Block(new Color(0.35f, 0.6f, 0.95f, 0.8f));

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
            w.Stop = new Button(StopSequence) { text = "■ 停止", tooltip = "途中で止める(最終状態には進めない)" };
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

            look.Add(BuildAnimImportRow(prop));
            block.Add(look);
            _stateWidgets.Add(w);
            return block;
        }

        // ── 状態遷移の自動再生 ──

        private VisualElement BuildTransitionBlock()
        {
            var block = Block(new Color(0.95f, 0.7f, 0.3f, 0.8f));

            var names = new List<string>();
            foreach (var seq in _options.Sequences)
            {
                names.Add(seq.Name);
            }

            var row1 = Row();
            row1.Add(new Label("状態遷移") { style = { unityFontStyleAndWeight = FontStyle.Bold, width = 70 } });
            _seqPopup = new PopupField<string>(names, 0) { style = { flexGrow = 1f } };
            _seqPopup.RegisterValueChangedCallback(_ => UpdateSequenceHint());
            row1.Add(_seqPopup);
            row1.Add(new Button(StartSequence) { text = "▶ 遷移を再生", tooltip = "選んだ遷移を順に自動で再生する(画像の差し替え・演出・SE を通しで確認)" });
            _seqPause = new Button(ToggleSequencePause) { text = "⏸ 一時停止" };
            row1.Add(_seqPause);
            _seqStop = new Button(StopSequence) { text = "■ 停止" };
            row1.Add(_seqStop);
            block.Add(row1);

            var row2 = Row();
            var interval = new Slider("1 状態の長さ(秒)", 0.2f, 2f) { value = _seqInterval, showInputField = true, style = { flexGrow = 1f } };
            interval.RegisterValueChangedCallback(evt => _seqInterval = evt.newValue);
            row2.Add(interval);
            var loop = new Toggle("ループ") { value = _seqLoop, style = { marginLeft = 8 } };
            loop.RegisterValueChangedCallback(evt => _seqLoop = evt.newValue);
            row2.Add(loop);
            var withSe = new Toggle("SE も鳴らす") { value = _seqWithSe, style = { marginLeft = 8 } };
            withSe.RegisterValueChangedCallback(evt => _seqWithSe = evt.newValue);
            row2.Add(withSe);
            block.Add(row2);

            _seqStatus = new Label { style = { opacity = 0.85f, whiteSpace = WhiteSpace.Normal } };
            block.Add(_seqStatus);
            UpdateSequenceHint();
            return block;
        }

        private void UpdateSequenceHint()
        {
            if (_seqStatus == null || _seqIndex >= 0 || _seqPopup == null)
            {
                return;
            }

            var seq = _options.Sequences[Mathf.Clamp(_seqPopup.index, 0, _options.Sequences.Length - 1)];
            var parts = new List<string>();
            foreach (var step in seq.Steps)
            {
                parts.Add(string.IsNullOrEmpty(step.SePropertyName) ? step.State.ToString() : $"{step.State}({ObjectNames.NicifyVariableName(step.SePropertyName)})");
            }

            _seqStatus.text = string.Join(" → ", parts);
        }

        private void StartSequence()
        {
            if (_skin == null || _seqPopup == null || _options.Sequences.Length == 0)
            {
                return;
            }

            StopSequence();
            _seqIndex = Mathf.Clamp(_seqPopup.index, 0, _options.Sequences.Length - 1);
            _seqStep = -1;
            _seqPaused = false;
            AdvanceSequence();
        }

        private void AdvanceSequence()
        {
            var seq = _options.Sequences[_seqIndex];
            _seqStep++;
            if (_seqStep >= seq.Steps.Length)
            {
                if (!_seqLoop)
                {
                    _seqIndex = -1;
                    _seqStatus.text = $"{seq.Name}: 完了";
                    return;
                }

                _seqStep = 0;
            }

            var step = seq.Steps[_seqStep];
            // 同じ状態が続く段(例: Disabled のまま押して拒否音)は演出をやり直さず、SE だけ鳴らす。
            if (_seqStep == 0 || step.State != _seqPrevState)
            {
                PlayStateCore(step.State);
            }

            _seqPrevState = step.State;

            var seText = string.Empty;
            if (_seqWithSe && !string.IsNullOrEmpty(step.SePropertyName))
            {
                foreach (var field in _options.SeFields)
                {
                    if (field.PropertyName == step.SePropertyName && field.Get(_skin).IsValid)
                    {
                        PlaySe(field.Get(_skin), field.PropertyName);
                        seText = $"  ♪ {ObjectNames.NicifyVariableName(field.PropertyName)}";
                        break;
                    }
                }
            }

            _seqTimer = 0f;
            _seqStatus.text = $"● {seq.Name}: {_seqStep + 1}/{seq.Steps.Length} {step.State}{seText}";
        }

        private void ToggleSequencePause()
        {
            if (_seqIndex < 0)
            {
                return;
            }

            _seqPaused = !_seqPaused;
            if (_tweens != null && _tweens.IsPlaying(_tweenHandle))
            {
                _tweens.SetPaused(_tweenHandle, _seqPaused);
            }
        }

        private void StopSequence()
        {
            var wasRunning = _seqIndex >= 0;
            _seqIndex = -1;
            _seqPaused = false;
            StopTween();
            if (wasRunning)
            {
                StopSe();
                if (_seqStatus != null)
                {
                    _seqStatus.text = "停止";
                }
            }
        }

        // ── SE 欄 ──

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

        // ── 当たり判定 ──

        private VisualElement BuildHitAreaRow()
        {
            var row = Row();
            var toggle = new Toggle("当たり判定を表示")
            {
                value = _showHitArea,
                tooltip = "確認用シーンのプレビューに押せる範囲を赤い半透明で重ねる(Game / Scene ビュー)。SceneView では四辺の四角をドラッグして Hit Area Expand を調整できる",
            };
            toggle.RegisterValueChangedCallback(evt =>
            {
                _showHitArea = evt.newValue;
                if (_showHitArea)
                {
                    _options.EnsurePreview?.Invoke();
                }

                UpdateHitOverlay();
                SceneView.RepaintAll();
                InternalEditorUtility.RepaintAllViews();
            });
            row.Add(toggle);
            row.Add(new Label("透明部分の判定(Alpha Hit Threshold)は実行時のみ効きます") { style = { marginLeft = 8, opacity = 0.7f } });
            return row;
        }

        private static readonly Color MainHitColor = new(1f, 0.25f, 0.25f, 0.35f);
        private static readonly Color ExtraHitColor = new(0.25f, 0.6f, 1f, 0.4f);

        // 本体(プレビュー部品 = Target Graphic)の当たり判定 + 追加分(つまみ等)。
        private List<(RectTransform rt, Vector4 expand, Action<Vector4> addDelta, Color color)> CollectHitAreas()
        {
            var list = new List<(RectTransform, Vector4, Action<Vector4>, Color)>();
            var control = _options.CurrentPreview?.Invoke();
            if (control == null || _skin == null)
            {
                return list;
            }

            var skin = _skin;
            if (control.transform is RectTransform main)
            {
                // 派生 Skin の上乗せ分(SliderSkin.ExtraHitPadding)はそのままに、差分だけ HitAreaExpand へ反映する。
                list.Add((main, skin.EffectiveHitAreaExpand, d => skin.HitAreaExpand += d, MainHitColor));
            }

            foreach (var extra in _options.ExtraHitAreas)
            {
                var rt = extra.Target?.Invoke();
                if (rt != null)
                {
                    list.Add((rt, extra.Get(skin), d => extra.AddDelta(skin, d), ExtraHitColor));
                }
            }

            return list;
        }

        private void UpdateHitOverlay()
        {
            var control = _options.CurrentPreview?.Invoke();
            if (control == null)
            {
                return;
            }

            if (!_showHitArea || _skin == null)
            {
                foreach (var t in control.GetComponentsInChildren<Transform>(true))
                {
                    if (t != null && t.name == HitOverlayName)
                    {
                        Object.DestroyImmediate(t.gameObject);
                    }
                }

                return;
            }

            foreach (var (rt, expand, _, color) in CollectHitAreas())
            {
                ApplyOverlay(rt, expand, color);
            }
        }

        private static void ApplyOverlay(RectTransform parent, Vector4 e, Color color)
        {
            var existing = parent.Find(HitOverlayName);
            RectTransform overlay;
            if (existing == null)
            {
                var go = new GameObject(HitOverlayName, typeof(RectTransform), typeof(Image)) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(parent, false);
                var image = go.GetComponent<Image>();
                image.color = color;
                image.raycastTarget = false;
                overlay = (RectTransform)go.transform;
                overlay.anchorMin = Vector2.zero;
                overlay.anchorMax = Vector2.one;
                overlay.pivot = new Vector2(0.5f, 0.5f);
            }
            else
            {
                overlay = (RectTransform)existing;
            }

            overlay.offsetMin = new Vector2(-e.x, -e.y);
            overlay.offsetMax = new Vector2(e.z, e.w);
        }

        private void OnSceneGui(SceneView view)
        {
            if (!_showHitArea || _skin == null)
            {
                return;
            }

            var prevColor = Handles.color;
            var changedAny = false;
            foreach (var (rt, e, addDelta, color) in CollectHitAreas())
            {
                var r = rt.rect;
                var min = new Vector2(r.xMin - e.x, r.yMin - e.y);
                var max = new Vector2(r.xMax + e.z, r.yMax + e.w);
                var mid = (min + max) * 0.5f;

                Handles.color = new Color(color.r, color.g, color.b, 1f);
                Handles.DrawPolyLine(
                    rt.TransformPoint(new Vector3(min.x, min.y)), rt.TransformPoint(new Vector3(min.x, max.y)),
                    rt.TransformPoint(new Vector3(max.x, max.y)), rt.TransformPoint(new Vector3(max.x, min.y)),
                    rt.TransformPoint(new Vector3(min.x, min.y)));

                var changed = false;
                var next = e;
                next.x = DragEdge(rt, new Vector3(min.x, mid.y), Vector3.left, e.x, ref changed);
                next.y = DragEdge(rt, new Vector3(mid.x, min.y), Vector3.down, e.y, ref changed);
                next.z = DragEdge(rt, new Vector3(max.x, mid.y), Vector3.right, e.z, ref changed);
                next.w = DragEdge(rt, new Vector3(mid.x, max.y), Vector3.up, e.w, ref changed);

                if (changed)
                {
                    Undo.RecordObject(_skin, "当たり判定の調整");
                    addDelta(next - e);
                    EditorUtility.SetDirty(_skin);
                    changedAny = true;
                }
            }

            Handles.color = prevColor;
            if (changedAny)
            {
                UpdateHitOverlay();
                InternalEditorUtility.RepaintAllViews();
            }
        }

        // エディタ側の操作プレビュー(スライダーを動かしたとき等)から SE 欄を名前で鳴らす。
        public void PlaySeField(string propertyName)
        {
            if (_skin == null)
            {
                return;
            }

            foreach (var field in _options.SeFields)
            {
                if (field.PropertyName == propertyName)
                {
                    var id = field.Get(_skin);
                    if (id.IsValid)
                    {
                        PlaySe(id, propertyName, stopPrevious: false);
                    }

                    return;
                }
            }
        }

        private static float DragEdge(RectTransform rt, Vector3 localPos, Vector3 localDir, float value, ref bool changed)
        {
            var world = rt.TransformPoint(localPos);
            var worldDir = rt.TransformDirection(localDir).normalized;
            var size = HandleUtility.GetHandleSize(world) * 0.08f;

            EditorGUI.BeginChangeCheck();
            var moved = Handles.Slider(world, worldDir, size, Handles.CubeHandleCap, 0f);
            if (!EditorGUI.EndChangeCheck())
            {
                return value;
            }

            var along = Vector3.Dot(rt.InverseTransformVector(moved - world), localDir);
            changed = true;
            return Mathf.Round(value + along);
        }

        // ── 共通の再生処理 ──

        private void RefreshSummaries()
        {
            if (_skin == null)
            {
                return;
            }

            EnsureScrollMaterial();

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

        // 設定欄の変更を配置済みのプレビューへすぐ当て直す(2026-09-14。以前は ▶ を押すか置き直すまで変わらなかった)。
        // 演出・遷移の再生中は触らない(途中の拡大率等を上書きしてしまうため。次の再生で反映される)。
        private void ReapplyPreviewVisuals()
        {
            var control = _options.CurrentPreview?.Invoke();
            if (control == null || _skin == null || _seqIndex >= 0 || (_tweens != null && _tweens.IsPlaying(_tweenHandle)))
            {
                return;
            }

            control.ForceStateForPreview(control.State);
            InternalEditorUtility.RepaintAllViews();
        }

        public const string DefaultScrollMaterialPath = "Assets/DDrive/Runtime/Ui/Shaders/DDrive_UI_Scroll.mat";

        // Scroll Speed を使う状態があるのに Scroll Material が空なら、既定のものを入れる(デザイナーが探さなくて済むように)。
        private void EnsureScrollMaterial()
        {
            if (_skin.ScrollMaterial != null)
            {
                return;
            }

            var needed = false;
            foreach (var state in StateByProperty.Values)
            {
                if (_skin.Get(state).ScrollSpeed != Vector2.zero)
                {
                    needed = true;
                    break;
                }
            }

            if (!needed)
            {
                return;
            }

            var material = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(DefaultScrollMaterialPath);
            if (material == null)
            {
                _status.text = "既定のスクロール用マテリアルが見つかりません: " + DefaultScrollMaterialPath;
                return;
            }

            Undo.RecordObject(_skin, "スクロール用マテリアルを設定");
            _skin.ScrollMaterial = material;
            EditorUtility.SetDirty(_skin);
            _status.text = "Scroll Speed を使う状態があるため、既定のスクロール用マテリアルを設定しました";
        }

        // 既存の Anim2D / Anim データ(スプライトの切り替えを持つ Clip)からコマを読み込む。
        private VisualElement BuildAnimImportRow(SerializedProperty stateProp)
        {
            var row = Row();
            var source = new ObjectField("Anim2D から読み込む")
            {
                objectType = typeof(DDrive.Runtime.Anim.AnimData),
                tooltip = "Anim2D エディタで作ったデータ(スプライトを切り替える Clip)を選び、右のボタンでコマ・コマ数/秒・ループを取り込む",
                style = { flexGrow = 1f },
            };
            row.Add(source);
            var so = stateProp.serializedObject;
            var path = stateProp.propertyPath;
            row.Add(new Button(() => ImportFrames(so, path, source.value as DDrive.Runtime.Anim.AnimData)) { text = "コマを読み込む" });
            return row;
        }

        private void ImportFrames(SerializedObject so, string statePath, DDrive.Runtime.Anim.AnimData data)
        {
            var clip = data != null ? data.Clip : null;
            if (clip == null)
            {
                _status.text = "Clip を持つ Anim2D データを指定してください";
                return;
            }

            var sprites = new List<Sprite>();
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (binding.propertyName != "m_Sprite")
                {
                    continue;
                }

                foreach (var key in AnimationUtility.GetObjectReferenceCurve(clip, binding))
                {
                    if (key.value is Sprite sprite)
                    {
                        sprites.Add(sprite);
                    }
                }

                break;
            }

            if (sprites.Count == 0)
            {
                _status.text = $"'{clip.name}' にスプライトのコマがありません";
                return;
            }

            so.Update();
            var state = so.FindProperty(statePath);
            var frames = state.FindPropertyRelative(nameof(StateVisual.AnimFrames));
            frames.arraySize = sprites.Count;
            for (var i = 0; i < sprites.Count; i++)
            {
                frames.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
            }

            state.FindPropertyRelative(nameof(StateVisual.AnimFps)).floatValue = clip.frameRate;
            state.FindPropertyRelative(nameof(StateVisual.AnimLoop)).boolValue = clip.isLooping;
            so.ApplyModifiedProperties();
            _status.text = $"'{clip.name}' から {sprites.Count} コマを読み込みました({clip.frameRate:0.#} コマ/秒)";
        }

        // 状態の「▶ 再生」。遷移の自動再生中なら止めてから 1 状態だけ再生する。
        private void PlayState(ControlState state)
        {
            StopSequence();
            PlayStateCore(state);
        }

        private void PlayStateCore(ControlState state)
        {
            if (_skin == null)
            {
                return;
            }

            var control = _options.EnsurePreview?.Invoke();
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
            UpdateHitOverlay();
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
        // stopPrevious=false はスライダー操作など実行時に重なって鳴る SE 用(掴む音が端の音に切られない等)。
        private void PlaySe(AssetId<SeMarker> id, string propertyName, bool stopPrevious = true)
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
            if (stopPrevious)
            {
                StopSe();
            }

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

            var control = _options.EnsurePreview?.Invoke();
            var rt = control != null ? control.transform as RectTransform : null;
            var root = control != null ? control.transform.root.gameObject : null;
            UiTweenEditorWindow.Open(tween, root, rt, control != null ? control.name : null);
        }

        private void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Min((float)(now - _lastTime), 0.1f);
            _lastTime = now;
            if (dt <= 0f)
            {
                return;
            }

            var animating = false;
            if (_tweens != null && _tweens.ActiveCount > 0)
            {
                _tweens.Tick(dt);
                animating = true;
            }

            // 状態のスプライトアニメ / スクロールもプレビューで動かす(Edit Mode では部品の Update が回らないため)。
            var control = _options.CurrentPreview?.Invoke();
            if (control != null)
            {
                if (_options.TickPreviewVisuals)
                {
                    control.TickVisuals(dt);
                }

                if (control.HasVisualAnimation)
                {
                    animating = true;
                }
            }

            if (_seqIndex >= 0 && !_seqPaused)
            {
                _seqTimer += dt;
                if (_seqTimer >= _seqInterval)
                {
                    AdvanceSequence();
                }

                animating = true;
            }

            // Edit Mode の Game ビューは自動では再描画されないため、動いている間は毎フレーム描き直す
            // (これが無いと Game ビューには最後の姿しか映らず「再生されない」ように見える)。
            if (animating)
            {
                InternalEditorUtility.RepaintAllViews();
            }

            if (_showHitArea)
            {
                UpdateHitOverlay();
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
                w.Stop.SetEnabled(mine || _seqIndex >= 0);
                SetText(w.Pause, mine && paused ? "▶ 再開" : "⏸ 一時停止");
                var status = !mine ? string.Empty : paused ? "⏸ 一時停止" : "● 再生中";
                if (w.Status.text != status)
                {
                    w.Status.text = status;
                }
            }

            if (_seqPause != null)
            {
                var running = _seqIndex >= 0;
                _seqPause.SetEnabled(running);
                _seqStop.SetEnabled(running);
                SetText(_seqPause, running && _seqPaused ? "▶ 再開" : "⏸ 一時停止");
            }

            var sePlaying = _audio != null && _audio.IsInitialized && _audio.AudioManager.IsPlaying(_seHandle);
            foreach (var s in _seWidgets)
            {
                s.Stop.SetEnabled(sePlaying && s.Field.PropertyName == _sePropertyName);
            }
        }

        private void Shutdown()
        {
            StopSequence();
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

        private static VisualElement Block(Color accent) => new()
        {
            style =
            {
                marginTop = 6, paddingLeft = 6, paddingTop = 2, paddingBottom = 4,
                borderLeftWidth = 3, borderLeftColor = accent,
            },
        };

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
