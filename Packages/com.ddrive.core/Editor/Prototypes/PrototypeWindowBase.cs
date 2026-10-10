using System.Collections.Generic;
using DDrive.Editor.Preview;
using DDrive.Editor.Validation;
using DDrive.Editor.Vfx;
using DDrive.Foundation.Handle;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.EditorPrototypes
{
    // 3 サンプルの共通土台: 対象選択(Project 選択に追従 + 固定)、▶ プレビュー(SceneVfxPreviewDriver)、検証、既定値比較。
    // サンプルごとの違いは「欄の見せ方」だけにするため、それ以外はここに集める。
    internal abstract class PrototypeWindowBase : EditorWindow
    {
        private const float RepeatGapSec = 0.35f;

        [SerializeField] protected VfxData _target;
        [SerializeField] private bool _lock;
        [SerializeField] private bool _repeat;
        [SerializeField] private float _speed = 1f;

        private SceneVfxPreviewDriver _driver;
        private Handle<VfxMarker> _handle;
        private bool _wantPlaying;
        private double _repeatWaitStart = -1;
        private VfxData _defaultInstance;
        private ScrollView _body;
        private Label _status;
        private Button _playButton;

        protected SerializedObject So { get; private set; }
        protected SerializedObject DefaultSo { get; private set; }
        protected DataValidationSection Validation { get; private set; }
        protected VfxData Target => _target;
        protected VfxFieldGuide Guide => VfxFieldGuide.Instance;

        // 対象を切り替えてから再生ボタンを一度でも押したか(A の「次にやること」が使う)。
        protected bool Played { get; private set; }

        // 各サンプルの狙い(1 行)。ウィンドウ最上部に出す。
        protected const string TrialNotice = "試験版（v1.7.0-preview.1）: 方向を決めるためのサンプル。確認後に削除予定";
        protected abstract string Aim { get; }
        protected abstract void BuildBody(VisualElement body);
        protected virtual void OnDataEdited() { }
        protected virtual void OnTick() { }
        protected virtual void OnTargetChanged() { }

        // true の窓(D)は、ツールバー・狙い・対象欄・未選択の案内を自前で作る。BuildBody は対象が無くても呼ばれる。
        protected virtual bool CustomChrome => false;
        protected virtual void ConfigureRoot(VisualElement root) { }

        protected bool Locked
        {
            get => _lock;
            set => _lock = value;
        }

        protected static T OpenWindow<T>(string title) where T : PrototypeWindowBase
        {
            var w = GetWindow<T>(title);
            w.minSize = new Vector2(500, 380); // [09] §7.1: 横幅 500px を minSize で回避しない
            if (w._target == null && Selection.activeObject is VfxData sel)
            {
                w.SetTarget(sel);
            }

            return w;
        }

        // ── ライフサイクル ──

        private void OnEnable()
        {
            _driver = new SceneVfxPreviewDriver { Speed = _speed };
            _handle = Handle<VfxMarker>.Invalid;
            _defaultInstance = CreateInstance<VfxData>();
            _defaultInstance.hideFlags = HideFlags.HideAndDontSave;
            DefaultSo = new SerializedObject(_defaultInstance);
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;
            _wantPlaying = false;
            _driver?.Dispose();
            _driver = null;
            DefaultSo = null;
            if (_defaultInstance != null)
            {
                DestroyImmediate(_defaultInstance);
            }
        }

        private void OnSceneChanged(UnityEngine.SceneManagement.Scene a, UnityEngine.SceneManagement.Scene b)
        {
            _handle = Handle<VfxMarker>.Invalid;
            _wantPlaying = false;
        }

        private void OnSelectionChange()
        {
            if (!_lock && Selection.activeObject is VfxData sel && sel != _target)
            {
                SetTarget(sel);
            }
        }

        private void OnUndoRedo()
        {
            EnsureSo();
            RefreshStates();
            Validation?.Refresh();
            _driver?.ReapplyAnchorToAll();
            OnDataEdited();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;

            if (CustomChrome)
            {
                _body = new ScrollView(ScrollViewMode.Vertical);
                root.Add(_body);
                _body.RegisterCallback<SerializedPropertyChangeEvent>(OnPropertyChanged);
                Validation = new DataValidationSection();
                root.schedule.Execute(Tick).Every(150);
                if (_target == null && Selection.activeObject is VfxData selD)
                {
                    _target = selD;
                }

                EnsureSo();
                ConfigureRoot(root);
                RebuildBody();
                return;
            }

            var toolbar = new Toolbar();
            var lockToggle = new ToolbarToggle { text = "🔒 対象を固定", value = _lock, tooltip = "ON: Project ウィンドウの選択に追従しない" };
            lockToggle.RegisterValueChangedCallback(e => _lock = e.newValue);
            toolbar.Add(lockToggle);
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(PreviewPlacementButton.CreateToolbarButton(
                "確認用シーンを開く", "ライト / カメラ / 床を備えた確認用シーンを開き、対象をそこで再生する", OpenPreviewScene));
            toolbar.Add(new ToolbarButton(() =>
            {
                if (_target != null)
                {
                    EditorGUIUtility.PingObject(_target);
                }
            }) { text = "Project で表示" });
            root.Add(toolbar);

            var aim = new HelpBox(TrialNotice + "\n" + Aim, HelpBoxMessageType.Info);
            aim.style.marginLeft = aim.style.marginRight = 6;
            aim.style.marginTop = 4;
            aim.name = "aim";
            root.Add(aim);

            _body = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f, paddingLeft = 6, paddingRight = 6, paddingTop = 6 } };
            root.Add(_body);
            _body.RegisterCallback<SerializedPropertyChangeEvent>(OnPropertyChanged);

            Validation = new DataValidationSection();
            root.schedule.Execute(Tick).Every(150);

            if (_target == null && Selection.activeObject is VfxData sel)
            {
                _target = sel;
            }

            EnsureSo();
            RebuildBody();
        }

        // ── 対象 ──

        protected void SetTarget(VfxData data)
        {
            StopMain();
            _target = data;
            Played = false;
            EnsureSo();
            OnTargetChanged();
            RebuildBody();
        }

        private void EnsureSo()
        {
            if (_target == null)
            {
                So = null;
                return;
            }

            if (So == null || So.targetObject == null || So.targetObject != _target)
            {
                So = new SerializedObject(_target);
            }
            else
            {
                So.Update();
            }
        }

        protected void RebuildBody()
        {
            if (_body == null)
            {
                return;
            }

            _body.Clear();
            _status = null;
            _playButton = null;

            if (CustomChrome)
            {
                BuildBody(_body);
                return;
            }

            var title = new ObjectField("対象アセット") { objectType = typeof(VfxData) };
            title.SetValueWithoutNotify(_target);
            title.RegisterValueChangedCallback(e => SetTarget(e.newValue as VfxData));
            _body.Add(title);

            if (_target == null || So == null)
            {
                _body.Add(new Label("Project ウィンドウで VfxData を選択してください(選択に追従します)。") { style = { opacity = 0.6f, whiteSpace = WhiteSpace.Normal, marginTop = 6 } });
                _body.Add(SampleVfx.CreateButton(SetTarget));
                Validation.Bind(null);
                return;
            }

            BuildBody(_body);
            Validation.Bind(_target);
            RefreshStates();
        }

        // 編集のたびに: 既定値との差の目印・検証・(必要なら)再生のやり直し。
        private void OnPropertyChanged(SerializedPropertyChangeEvent evt)
        {
            var path = evt.changedProperty?.propertyPath ?? string.Empty;
            if (path == "Prefab")
            {
                RestartIfPlaying();
            }
            else if (path.StartsWith("Anchor"))
            {
                _driver?.ReapplyAnchorToAll();
            }

            RefreshStates();
            Validation?.Refresh();
            OnDataEdited();
        }

        protected void RefreshStates()
        {
            if (_body == null)
            {
                return;
            }

            var fields = new List<GuidedField>();
            _body.Query<GuidedField>().ToList(fields);
            for (var i = 0; i < fields.Count; i++)
            {
                fields[i].UpdateState();
            }
        }

        // 外部コード(雛形・パッド)が Data を書いた後に呼ぶ。
        protected void NotifyDataWritten()
        {
            EditorUtility.SetDirty(_target);
            So?.Update();
            RefreshStates();
            Validation?.Refresh();
            RestartIfPlaying();
            OnDataEdited();
        }

        protected GuidedField MakeField(string field, bool showReset, bool tierBadge = false)
        {
            var entry = Guide.Find(field);
            return FieldGuideUi.MakeField(So, DefaultSo, entry, showReset, tierBadge, () =>
            {
                RefreshStates();
                Validation?.Refresh();
                OnDataEdited();
            });
        }

        // ── 再生 ──

        protected bool IsPlaying => _driver != null && _driver.IsPlaying(_handle);

        protected VisualElement BuildPlayRow()
        {
            var wrap = new VisualElement { style = { marginTop = 4, marginBottom = 4 } };
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center } };
            _playButton = new Button(() => PlayMain(true)) { text = "▶ 再生" };
            PreviewPlacementButton.ApplyNarrowWindowStyle(_playButton);
            row.Add(_playButton);
            var stop = new Button(StopMain) { text = "■ 停止" };
            PreviewPlacementButton.ApplyNarrowWindowStyle(stop);
            row.Add(stop);
            var repeat = new Toggle("リピート") { value = _repeat, tooltip = "終わったら自動で再生し直す(調整中に何度も押さなくてよい)" };
            repeat.style.marginLeft = 8;
            repeat.RegisterValueChangedCallback(e =>
            {
                _repeat = e.newValue;
                _repeatWaitStart = -1;
            });
            row.Add(repeat);
            _status = new Label("■ 停止中") { style = { marginLeft = 10, opacity = 0.8f } };
            row.Add(_status);
            wrap.Add(row);

            var speed = new Slider("速度", 0.1f, 2f) { value = _speed, showInputField = true };
            speed.RegisterValueChangedCallback(e =>
            {
                _speed = e.newValue;
                if (_driver != null)
                {
                    _driver.Speed = _speed;
                }
            });
            wrap.Add(speed);
            return wrap;
        }

        protected void PlayMain(bool focus)
        {
            if (_target == null || _driver == null)
            {
                return;
            }

            StopMainInternal();
            _wantPlaying = true;
            Played = true;
            _repeatWaitStart = -1;
            _handle = _driver.Play(_target, null);
            if (focus)
            {
                PreviewPlacement.Focus(_driver.Manager.GetGameObject(_handle));
            }
        }

        protected void StopMain()
        {
            _wantPlaying = false;
            _repeatWaitStart = -1;
            StopMainInternal();
        }

        private void StopMainInternal()
        {
            if (_driver != null && _driver.IsPlaying(_handle))
            {
                _driver.Stop(_handle);
            }

            _handle = Handle<VfxMarker>.Invalid;
        }

        private void RestartIfPlaying()
        {
            if (_wantPlaying && _driver != null && _driver.IsPlaying(_handle))
            {
                _driver.Kill(_handle);
                PlayMain(false);
            }
        }

        protected void ReapplyAnchor() => _driver?.ReapplyAnchorToAll();

        protected void OpenPreviewScene(PreviewPlaceMode mode)
        {
            StopMain();
            if (!PreviewPlacement.PrepareScene(mode, VfxPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            if (PreviewPlacement.IsPersistent(mode))
            {
                if (_target != null && _target.Prefab != null)
                {
                    PreviewPlacement.PlacePrefabPersistent(_target.Prefab, Vector3.zero, Quaternion.identity);
                }

                return;
            }

            PlayMain(true);
        }

        private void Tick()
        {
            if (_driver == null)
            {
                return;
            }

            var playing = _driver.IsPlaying(_handle);
            if (!playing)
            {
                _handle = Handle<VfxMarker>.Invalid;
            }

            if (_wantPlaying && _repeat && !playing && _target != null)
            {
                var now = EditorApplication.timeSinceStartup;
                if (_repeatWaitStart < 0)
                {
                    _repeatWaitStart = now;
                }
                else if (now - _repeatWaitStart >= RepeatGapSec)
                {
                    _repeatWaitStart = -1;
                    PlayMain(false);
                    playing = true;
                }
            }

            if (_status != null)
            {
                var text = playing ? "● 再生中" : _wantPlaying && _repeat ? "↻ リピート待機" : "■ 停止中";
                if (_status.text != text)
                {
                    _status.text = text;
                }
            }

            if (_playButton != null)
            {
                var label = playing ? "▶ 再生(やり直し)" : "▶ 再生";
                if (_playButton.text != label)
                {
                    _playButton.text = label;
                }
            }

            OnTick();
        }
    }
}
