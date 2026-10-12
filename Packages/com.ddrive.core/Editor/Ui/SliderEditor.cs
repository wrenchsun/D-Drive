using System.Collections.Generic;
using DDrive.Editor.Inspector;
using DDrive.Editor.Manual;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ui
{
    // Slider Editor(UiSlider の動き方を見て整える。docs/1010 UX-13。旧 SliderEditorWindow は Archive メニューへ退避)。
    // 対象は Data ではなく UiSlider(シーン / Prefab 上のコンポーネント)なので、UxEditorWindow<TData> ではなく、
    // 共通テーマ(UxTheme)と部品(UxCard / DDriveUx.uss)を当てた独立ウィンドウにしている(UiPresetGallery と同じ作り)。
    // カード: 対象 / 応答曲線 / 値と刻み(ノッチ可視化つき)/ 実操作 / 追従比較 / Skin プレビュー / 雛形 / 検証。
    // ウィンドウ内に「動く」ものは描かない(owner instruction 2026-09-10): 応答曲線・ノッチ帯は静的な図で、実際に動く確認(追従・ドラッグ・状態切替)は
    // 「確認用シーンに配置」した DontSave の実 UiSlider を EditorApplication.update から Advance(dt) で駆動して、Scene / Game ビューで見せる(ADR-4)。
    [DataEditor(typeof(SliderSkinData), "Slider Editor で開く", Order = 5)]
    internal sealed partial class SliderEditor : EditorWindow
    {
        private const string Page = "ui-skin";
        private const string PreviewRootName = "[D-Drive] Slider Editor Preview 2";
        private const int ResponseSamples = 64;
        private const int DragSimSteps = 20;

        [SerializeField] private UiSlider _target;

        // Slider Skin の「Slider Editor で開く」から開いたときのスキン(サンプルは Skin Id を持たないため、
        // 「全状態を並べる」と SE はこれを使う)。ドメインリロードで失われないよう保存し、OnEnable でサンプルに当て直す。
        [SerializeField] private SliderSkinData _explicitSkin;

        private readonly List<string> _eventLog = new List<string>();
        private readonly SliderSePreview _sePreview = new SliderSePreview();
        private readonly List<UiSlider> _skinPreviewSliders = new List<UiSlider>();
        private readonly Vector3[] _corners = new Vector3[4];

        private VisualElement _root;
        private ObjectField _targetField;
        private Label _hint;
        private Label _statusLabel;
        private VisualElement _fieldsHost;
        private VisualElement _notchFieldsHost;
        private VisualElement _compareHost;
        private VisualElement _validationHost;
        private Label _eventLogLabel;
        private SliderResponseGraph _graph;
        private SliderNotchBar _notchBar;
        private SkinPresetsRow _presets;
        private Toggle _fineToggle;
        private bool _fineAdjust;
        private bool _lastPro;
        private bool _geometryHooked;
        private IVisualElementScheduledItem _tick;
        private int _tickCount;
        private string _validationKey = string.Empty;

        // イベントを購読している UiSlider(_target が差し替わった・破棄された後でも確実に解除するため別に持つ)。
        private UiSlider _subscribedTarget;

        // 追従比較(d)・Skin プレビュー(e)の DontSave 実体。
        private UiSlider _compareSlider;

        // ドラッグ模擬(c)。EditorApplication.update で 1 ステップずつ進める。
        private bool _dragSimActive;
        private int _dragSimStep;
        private bool _dragSimIncludesCompare;

        private PreviewService _audio;
        private double _lastEditorTime;

        // ── 入口 ──

        [MenuItem(DDriveMenu.Editors + "Slider")]
        public static void OpenFromMenu() => Open(Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<UiSlider>() : null);

        public static void Open(UiSlider target)
        {
            var w = GetWindow<SliderEditor>("Slider Editor");
            w.minSize = new Vector2(500, 480);
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // [DataEditor] 経由: SliderSkinData の Inspector から「Slider Editor で開く」で開いたときは、
        // そのスキンを当てたサンプルスライダーを確認用シーンに配置してから開く。
        public static void Open(SliderSkinData skin)
        {
            var w = GetWindow<SliderEditor>("Slider Editor");
            w.minSize = new Vector2(500, 480);
            // 暗黙の配置なのでシーンは切り替えない(保存ダイアログを出さない)。
            w.PlaceSampleInScene(skin, PreviewPlaceMode.CurrentScene);
        }

        // ── ライフサイクル ──

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.update += OnEditorUpdate;
            _lastEditorTime = EditorApplication.timeSinceStartup;

            // ドメインリロード後は _target([SerializeField])だけが戻り、イベントの購読とサンプルに当てた Skin
            // (UiInteractable 側の参照はシリアライズされない)が失われて、SE とイベントログが黙って止まる。
            // 購読し直し、サンプル(プレビュー)なら Skin も当て直す(実物には触らない)。
            if (_target != null)
            {
                if (_explicitSkin != null && IsPreviewObject(_target))
                {
                    _target.SetVisual(_explicitSkin);
                }

                SubscribeEvents();
            }
        }

        private void OnFocus() => SceneGuiOwner.Claim(this);

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            EditorApplication.update -= OnEditorUpdate;
            UnsubscribeEvents();
            SceneGuiOwner.Release(this);
            _audio?.Dispose();
            _audio = null;
        }

        private void OnDestroy() => RemoveFromScene();

        private void CreateGUI()
        {
            _root = rootVisualElement;
            UxTheme.Apply(_root);
            _lastPro = EditorGUIUtility.isProSkin;

            var body = new ScrollView(ScrollViewMode.Vertical);
            body.AddToClassList("ux-scroll");
            _root.Add(body);
            var page = new VisualElement();
            page.AddToClassList("ux-page");
            body.Add(page);

            page.Add(BuildHero());
            page.Add(BuildResponseCard());
            page.Add(BuildNotchCard());
            page.Add(BuildOperateCard());
            page.Add(BuildCompareCard());
            page.Add(BuildSkinPreviewCard());
            page.Add(BuildPresetCard());
            page.Add(BuildValidationCard());

            _tick?.Pause();
            _tick = _root.schedule.Execute(Tick).Every(150);

            if (_target == null && Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<UiSlider>() is UiSlider selected)
            {
                _target = selected;
                SubscribeEvents();
            }

            _targetField?.SetValueWithoutNotify(_target);
            RefreshAll();
        }

        private void Tick()
        {
            if (EditorGUIUtility.isProSkin != _lastPro)
            {
                // Light / Dark が切り替わった: クラスを付け替え、Painter の色を読み直すため作り直す。
                _root.Clear();
                CreateGUI();
                return;
            }

            _graph?.Sync();
            _notchBar?.Sync();
            _tickCount++;
            if (_tickCount % 4 == 0)
            {
                RebuildValidation();
            }
        }

        // ── 組み立て ──

        private VisualElement BuildHero()
        {
            var hero = new VisualElement();
            hero.AddToClassList("ux-hero");

            var top = new VisualElement();
            top.AddToClassList("ux-hero__top");
            var info = new VisualElement();
            info.AddToClassList("ux-hero__info");
            var title = new Label("Slider Editor");
            title.AddToClassList("ux-hero__title");
            info.Add(title);
            _hint = new Label();
            _hint.AddToClassList("ux-dim");
            _hint.AddToClassList("ux-hero__id");
            info.Add(_hint);
            top.Add(info);

            var help = new Button(() => ManualLauncher.OpenPage(Page, "slider")) { text = "?", tooltip = "マニュアルを開く" };
            help.AddToClassList("ux-btn");
            help.AddToClassList("ux-btn--icon");
            top.Add(help);
            hero.Add(top);

            var actions = new VisualElement();
            actions.AddToClassList("ux-hero__actions");
            actions.Add(Btn("選択から取得", () => SetTarget(Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<UiSlider>() : null),
                "Hierarchy で選んでいる GameObject の UiSlider を対象にする"));
            var place = PreviewPlacementButton.Create(
                "サンプルを配置",
                "UI 確認用シーン(CanvasPreviewScene)を開き、サンプルの UiSlider を置いて対象にする",
                mode => PlaceSampleInScene(null, mode));
            place.AddToClassList("ux-btn");
            actions.Add(place);
            actions.Add(Btn("撤去", RemoveFromScene, "「サンプルを配置」で置いたスライダー(と比較用・全状態の並べ表示)を片付ける"));
            actions.Add(Btn("＋ 新規 Skin", NewSkin, "命名規則どおりの新しい Slider Skin を作り、その Skin を当てたサンプルを確認用シーンに置く"));
            actions.Add(Btn("Slider Skin Editor へ", OpenSkinEditor, "このスライダーの Skin(Skin Id)を Slider Skin Editor で開く"));
            actions.Add(Btn("確認用シーン", () => CanvasPreviewSceneSetup.TryOpenOrCreate(), "UI 確認用シーン(CanvasPreviewScene)を開く(無ければ生成)"));
            hero.Add(actions);
            return hero;
        }

        private static Button Btn(string text, System.Action click, string tooltip)
        {
            var b = new Button(click) { text = text, tooltip = tooltip };
            b.AddToClassList("ux-btn");
            return b;
        }

        private UxCard Card(string title, string desc, string anchor) => new UxCard(title, desc, UxCard.Help(Page, anchor));

        private static Label Note(string text)
        {
            var l = new Label(text);
            l.AddToClassList("ux-dim");
            l.AddToClassList("ux-note");
            return l;
        }

        private VisualElement BuildResponseCard()
        {
            var card = Card("応答曲線", "つまみの位置(横)→ 値(縦)の対応。黄色の点が現在の値。曲線は Response で変える", "slider-response");

            _targetField = new ObjectField("対象 UiSlider") { objectType = typeof(UiSlider), allowSceneObjects = true };
            _targetField.AddToClassList("ux-ut-field");
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as UiSlider));
            card.Body.Add(_targetField);

            _statusLabel = Note(string.Empty);
            card.Body.Add(_statusLabel);

            _graph = new SliderResponseGraph(() => _target, ResponseSamples);
            card.Body.Add(_graph);

            _fieldsHost = new VisualElement();
            card.Body.Add(_fieldsHost);
            return card;
        }

        private VisualElement BuildNotchCard()
        {
            var card = Card("値の範囲・刻み・ノッチ", "範囲(Min / Max)と刻み(Step)、目盛り(Notches)と吸着の幅。帯は目盛りの位置と吸着の効く範囲", "slider-notch");
            _notchFieldsHost = new VisualElement();
            card.Body.Add(_notchFieldsHost);
            _notchBar = new SliderNotchBar(() => _target);
            card.Body.Add(_notchBar);
            card.Body.Add(Note("SceneView 上でも同じ帯を TrackRect の上に重ねて表示します(このウィンドウがフォーカス中のみ)。"));
            return card;
        }

        private VisualElement BuildOperateCard()
        {
            var card = Card("実操作", "パッド操作・ドラッグを再現して、値と見た目・SE・イベントを確かめる(確認用シーンに置いたサンプルだけ)", "slider-operate");
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            line.Add(Btn("◀", () => MovePad(MoveDirection.Left), "キーボード / パッドの左 1 回分"));
            line.Add(Btn("▶", () => MovePad(MoveDirection.Right), "キーボード / パッドの右 1 回分"));
            _fineToggle = new Toggle("微調整") { value = _fineAdjust, tooltip = "パッド操作の移動量を Fine Step Multiplier 倍にする" };
            _fineToggle.AddToClassList("ux-sk-fine");
            _fineToggle.RegisterValueChangedCallback(evt => _fineAdjust = evt.newValue);
            line.Add(_fineToggle);
            card.Body.Add(line);

            var drag = new VisualElement();
            drag.AddToClassList("ux-toolsline");
            drag.Add(Btn("ドラッグ模擬: 0 → 1", () => StartDragSimulate(false), "マウスで端から端まで掴んで動かす操作を再現(掴む・目盛り・端・離すの SE も鳴る)"));
            card.Body.Add(drag);

            _eventLogLabel = Note(string.Empty);
            card.Body.Add(_eventLogLabel);
            return card;
        }

        private VisualElement BuildCompareCard()
        {
            var card = Card("追従比較", "2 体目のスライダーを置いて、Follow Motion(表示が値に追いつく動き)を見比べる", "slider-compare");
            _compareHost = new VisualElement();
            card.Body.Add(_compareHost);
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            line.Add(Btn("2 体目を配置して FollowMotion を比較", SpawnCompareSlider, "OutBack で 0.4 秒の追従をつけた 2 体目を横に置く"));
            line.Add(Btn("同時に 0→1 を模擬", () => StartDragSimulate(true), "対象と 2 体目を同時に 0 → 1 へドラッグして、追従の差を見る"));
            card.Body.Add(line);
            RebuildCompareSection();
            return card;
        }

        private VisualElement BuildSkinPreviewCard()
        {
            var card = Card("Skin プレビュー", "Normal / Hover / Pressed / Selected / Disabled / Locked の 6 状態を並べて見る", "slider-skinpreview");
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            line.Add(Btn("全状態を並べる", SpawnSkinStatesPreview, "Normal / Hover / Pressed / Selected / Disabled / Locked を横に並べて、確認用シーンに置く"));
            card.Body.Add(line);
            return card;
        }

        private VisualElement BuildPresetCard()
        {
            var card = Card("雛形", "音量・感度・HP バー・スタミナ・キャラメイクの 5 種を、対象のスライダーに一式当てる", "slider-presets");
            _presets = new SkinPresetsRow(ApplyPreset, false);
            card.Body.Add(_presets);
            return card;
        }

        private VisualElement BuildValidationCard()
        {
            var card = Card("検証", "UiSlider の設定の検査結果(範囲・刻み・目盛り・パーツの参照など)", "slider-validation");
            _validationHost = new VisualElement();
            card.Body.Add(_validationHost);
            return card;
        }

        // ── ターゲット ──

        private void SetTarget(UiSlider target)
        {
            UnsubscribeEvents();

            // 別のスライダーに変わったときだけ明示 Skin を捨てる(同じサンプルを「選択から取得」し直しただけで Skin を失わない)。
            if (target != _target)
            {
                _explicitSkin = null;
            }

            _target = target;
            _targetField?.SetValueWithoutNotify(_target);
            SubscribeEvents();
            RefreshAll();
        }

        private void RefreshAll()
        {
            _validationKey = null;
            RebuildValidation();
            RebuildFields();
            _graph?.Sync(true);
            _notchBar?.Sync(true);
            SceneView.RepaintAll();
            if (_statusLabel != null)
            {
                _statusLabel.text = _target == null ? "UiSlider を選択してください(ヒーローの「選択から取得」か「サンプルを配置」)" : string.Empty;
            }

            if (_hint != null)
            {
                _hint.text = _target == null ? "対象の UiSlider がありません" : $"{_target.name}(範囲 {_target.Min:0.##}〜{_target.Max:0.##}・Notches {_target.Notches})";
            }
        }

        // 対象の UiSlider の欄(Response と、値・刻み・ノッチ)を作り直す。
        private void RebuildFields()
        {
            if (_fieldsHost == null || _notchFieldsHost == null)
            {
                return;
            }

            _fieldsHost.Clear();
            _notchFieldsHost.Clear();
            if (_target == null)
            {
                return;
            }

            var so = new SerializedObject(_target);
            AddField(_fieldsHost, so, nameof(UiSlider.Response), "応答(Response)");
            _fieldsHost.Bind(so);
            AddField(_notchFieldsHost, so, nameof(UiSlider.Min), "最小(Min)");
            AddField(_notchFieldsHost, so, nameof(UiSlider.Max), "最大(Max)");
            AddField(_notchFieldsHost, so, nameof(UiSlider.Step), "刻み(Step。0 = 連続)");
            AddField(_notchFieldsHost, so, nameof(UiSlider.WholeNumbers), "整数だけ(Whole Numbers)");
            AddField(_notchFieldsHost, so, nameof(UiSlider.Notches), "目盛りの数(Notches)");
            AddField(_notchFieldsHost, so, nameof(UiSlider.SnapThreshold), "吸着の幅(Snap Threshold)");
            _notchFieldsHost.Bind(so);
        }

        private void AddField(VisualElement host, SerializedObject so, string name, string label)
        {
            var prop = so.FindProperty(name);
            if (prop == null)
            {
                return;
            }

            var field = new PropertyField(prop, label);
            field.AddToClassList("ux-ut-field");
            field.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                _graph?.Sync(true);
                _notchBar?.Sync(true);
                SceneView.RepaintAll();
                RebuildValidation();
            });
            host.Add(field);
        }

        // 新しい Slider Skin を作って、そのサンプルを開く(対象はアセットではなくシーン上の UiSlider なので、「作成」の結果はその Skin を当てたサンプル)。
        private static void NewSkin() =>
            DDrive.Editor.AssetBrowser.NewAssetDialog.Open(new[] { typeof(SliderSkinData) }, created =>
            {
                if (created is SliderSkinData skin)
                {
                    Open(skin);
                }
            });

        private void OpenSkinEditor()
        {
            var skin = GetTargetSkin();
            if (skin != null)
            {
                SliderSkinEditor.Open(skin);
            }
            else
            {
                Say("このスライダーには Skin が設定されていません(Skin Id が空)");
            }
        }

        // ── イベントの購読と SE ──

        private void SubscribeEvents()
        {
            UnsubscribeEvents(); // 二重購読しない
            if (_target == null)
            {
                return;
            }

            _subscribedTarget = _target;
            _target.OnValueChanged += OnTargetValueChanged;
            _target.OnCommit += OnTargetCommit;
            _target.OnNotchPassed += OnTargetNotch;
            _target.OnLimitReached += OnTargetLimit;
            _target.OnDragBegin += OnTargetDragBegin;
            _target.OnDragEnd += OnTargetDragEnd;
            _target.OnDenied += OnTargetDenied;
        }

        private void UnsubscribeEvents()
        {
            // 破棄済み(Unity の null)でも C# のイベントは外せるので、参照そのもので判定する。
            if (ReferenceEquals(_subscribedTarget, null))
            {
                return;
            }

            _subscribedTarget.OnValueChanged -= OnTargetValueChanged;
            _subscribedTarget.OnCommit -= OnTargetCommit;
            _subscribedTarget.OnNotchPassed -= OnTargetNotch;
            _subscribedTarget.OnLimitReached -= OnTargetLimit;
            _subscribedTarget.OnDragBegin -= OnTargetDragBegin;
            _subscribedTarget.OnDragEnd -= OnTargetDragEnd;
            _subscribedTarget.OnDenied -= OnTargetDenied;
            _subscribedTarget = null;
        }

        private void OnTargetValueChanged(float v) => AppendLog($"Changed: {v:0.###}");

        private void OnTargetCommit(float v) => AppendLog($"Commit: {v:0.###}");

        private void OnTargetNotch(int index)
        {
            AppendLog($"NotchPassed: {index}");
            PlaySkinSe(SliderSeEvent.Notch);
        }

        private void OnTargetLimit(bool isMax)
        {
            AppendLog($"LimitReached: {(isMax ? "Max" : "Min")}");
            PlaySkinSe(SliderSeEvent.Limit);
        }

        private void OnTargetDragBegin() => PlaySkinSe(SliderSeEvent.Grab);

        private void OnTargetDragEnd() => PlaySkinSe(SliderSeEvent.Release);

        private void OnTargetDenied()
        {
            AppendLog("Denied(Disabled / Locked)");
            PlaySkinSe(SliderSeEvent.Denied);
        }

        // Editor では Audio が未 Bind で UiSlider 自身の SE は鳴らないため、同じタイミングのイベントでプレビュー用の実 AudioManager から鳴らす。
        private void PlaySkinSe(SliderSeEvent e)
        {
            var data = _sePreview.Resolve(GetTargetSkin(), e);
            if (data == null)
            {
                return;
            }

            _audio ??= new PreviewService();
            _audio.Initialize();
            _audio.PlaySe(data);
        }

        private void AppendLog(string line)
        {
            _eventLog.Add(line);
            if (_eventLog.Count > 12)
            {
                _eventLog.RemoveAt(0);
            }

            if (_eventLogLabel != null)
            {
                _eventLogLabel.text = string.Join("\n", _eventLog);
            }
        }

        private void Say(string text)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = text;
            }
        }

        private SliderSkinData GetTargetSkin()
        {
            // ResolvedSkin は protected のため、SkinId 経由の解決結果は直接読めない。
            // 「明示的に SetVisual した Skin」を優先し、無ければ AssetDatabase から Id で探す(実行時専用の UiSkins.Resolver には頼らない)。
            if (_target == null)
            {
                return null;
            }

            if (!_target.SkinId.IsValid)
            {
                return _explicitSkin;
            }

            // 全件走査をやめ、Id ごとにキャッシュする DataIdLookup で引く。
            return DataIdLookup.Find<SliderSkinData>(_target.SkinId.Value);
        }

        // ── 検証 ──

        private void RebuildValidation()
        {
            if (_validationHost == null)
            {
                return;
            }

            var results = new List<ValidationResult>();
            if (_target != null)
            {
                UiSliderValidation.Validate(_target, _target.name, results);
            }

            var key = _target == null ? "none" : UxRow.IssueKey(results) + (_target.GetInstanceID());
            if (key == _validationKey)
            {
                return; // 内容が同じなら作り直さない
            }

            _validationKey = key;
            _validationHost.Clear();
            if (_target == null)
            {
                _validationHost.Add(Note("対象のスライダーがありません"));
                return;
            }

            if (results.Count == 0)
            {
                _validationHost.Add(Note("問題なし"));
                return;
            }

            foreach (var result in results)
            {
                _validationHost.Add(UxRow.MakeIssue(result, null));
            }
        }

        // ── 雛形 ──

        private void ApplyPreset(SliderPresets.SliderPreset preset)
        {
            if (_target == null)
            {
                Say("先に対象 UiSlider を用意してください(「選択から取得」か「サンプルを配置」)");
                return;
            }

            SliderPresets.Apply(_target, preset);
            RefreshAll();
            AppendLog($"プリセット '{preset}' を適用しました");
        }

        // ── テスト・確認用の読み取り口 ──

        internal UiSlider Target => _target;

        internal SliderResponseGraph Graph => _graph;

        internal SliderNotchBar NotchBar => _notchBar;

        internal SkinPresetsRow Presets => _presets;

        internal string StatusText => _statusLabel != null ? _statusLabel.text : string.Empty;

        internal string EventLogText => _eventLogLabel != null ? _eventLogLabel.text : string.Empty;

        internal int ValidationCount => _validationHost != null ? _validationHost.childCount : 0;

        internal int SkinPreviewCount => _skinPreviewSliders.Count;

        internal UiSlider CompareSlider => _compareSlider;

        internal void SetTargetForTest(UiSlider target) => SetTarget(target);

        internal void ApplyPresetForTest(SliderPresets.SliderPreset preset) => ApplyPreset(preset);

        internal bool DragSimActive => _dragSimActive;
    }
}
