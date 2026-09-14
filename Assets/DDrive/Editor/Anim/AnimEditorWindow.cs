using System.Collections.Generic;
using System.Text;
using DDrive.Editor.Anim2D;
using DDrive.Editor.Common;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Anim2D;
using DDrive.Runtime.Model;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anim
{
    // [05_model_animation.md] B-4 — AnimEditor(3-3/3-4)。
    // 開いているシーン / プレハブモードのモデルを実 AnimManager でその場で動かし(SceneAnimPreviewDriver)、
    // SceneView で確認しながらタイムライン(シーク・イベントマーカーのドラッグ)、ブレンド確認(A → B を CrossFade)、
    // BlendShape / IK の情報、イベントに設定した SE・VFX の同時プレビュー、Validation を 1 ウィンドウで行う。
    // ウィンドウ内ビューポートは持たない(2026-09-09: 小さく分かりづらいため廃止。VFX Editor と同じ SceneView 方式に統一)。
    //
    // 対象の Animator は自動で決まる: 「確認用シーンを開く」→ 確認用モデルをシーンに配置して対象にする、
    // 「モデル Prefab を開く」/ プレハブモードに入る → その Prefab の Animator を対象にする。手動で差し替えも可。
    [DataEditor(typeof(AnimData), "Anim Editor で開く")]
    public sealed partial class AnimEditorWindow : EditorWindow
    {
        private const float TimelineHeight = 62f; // 2026-09-11: フレーム番号の行(bar.yMax + 22)を追加したため 44 → 62
        private const int MaxEventLog = 12;

        [SerializeField] private AnimData _target;
        [SerializeField] private bool _lockTarget;
        [SerializeField] private ModelData _model;
        [SerializeField] private float _speed = 1f;
        [SerializeField] private bool _loopPreview;
        [SerializeField] private Animator _sceneTarget;

        // 2026-09-10(3-13): 対象が Anim2DData で確認用モデル(ModelData)が未設定のときのフォールバック。
        // Anim2DEditorWindow.Edit と同じ Anim2DPreviewObject(SpriteRenderer + Animator、DontSave)を使う。
        private GameObject _anim2DPreview;

        private SceneAnimPreviewDriver _scene;
        private SerializedObject _serializedTarget;
        private Handle<AnimMarker> _animHandle = Handle<AnimMarker>.Invalid;
        private int _draggingEvent = -1;
        private int _dragUndoGroup;
        private readonly List<string> _eventLog = new();

        private ObjectField _targetField;
        private ObjectField _modelField;
        private ObjectField _sceneTargetField;
        private Label _sceneHelpLabel;
        private Label _statusLabel;
        private Label _eventLogLabel;
        private Label _modelSummaryLabel;
        private Foldout _modelDetailFoldout;
        private Label _modelDetailLabel;
        private VisualElement _fieldsContainer;
        private Foldout _validationFoldout;
        private IMGUIContainer _timelineContainer;
        private Button _playButton;

        [MenuItem(DDriveMenu.Editors + "Animation (3D)")]
        public static void Open() => Open(Selection.activeObject as AnimData);

        public static void Open(AnimData target)
        {
            var window = GetWindow<AnimEditorWindow>("Anim Editor");
            window.minSize = new Vector2(560, 480);
            if (target != null)
            {
                window.SetTarget(target);
            }
        }

        // ── ライフサイクル ──

        private void OnEnable()
        {
            _scene = new SceneAnimPreviewDriver { Speed = _speed };
            _scene.Manager.Events.OnEventFired += OnEventFired;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnEditorUpdate;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageOpened;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;
        }

        private void OnDisable()
        {
            // Anim2D のプレビュー物(DontSave)はここでは壊さない。Anim2D Editor と共有しており、
            // ウィンドウを閉じた / ドメインリロードしただけで相手の対象や絵が消えてしまうため(2026-09-11 レビュー対応)。
            // 破棄はモデル変更 / 対象切替などの明示操作だけ。残った物は次の FindOrCreate が拾い直す。
            _anim2DPreview = null;
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;
            PrefabStage.prefabStageOpened -= OnPrefabStageOpened;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (_scene != null)
            {
                _scene.Manager.Events.OnEventFired -= OnEventFired;
                _scene.Dispose();
                _scene = null;
            }
        }

        private void OnSelectionChange()
        {
            if (!_lockTarget && Selection.activeObject is AnimData selected && selected != _target)
            {
                SetTarget(selected);
            }
        }

        private void OnUndoRedo()
        {
            _serializedTarget?.Update();
            RefreshEventList();
            RefreshValidation();
            _timelineContainer?.MarkDirtyRepaint();
        }

        // シーン切替: ドライバは自分でリセット済み。対象を作り直す(確認用モデルがあれば次の ▶ で配置)。
        private void OnActiveSceneChanged(Scene previous, Scene current)
        {
            _animHandle = Handle<AnimMarker>.Invalid;
            _seqIndex = -1;
            SetSceneTarget(null);
        }

        // プレハブモードに入ったら、その Prefab の Animator を自動で対象にする(モデル Prefab の中で動かす導線)。
        private void OnPrefabStageOpened(PrefabStage stage)
        {
            _animHandle = Handle<AnimMarker>.Invalid;
            _seqIndex = -1;
            var animator = stage != null && stage.prefabContentsRoot != null ? stage.prefabContentsRoot.GetComponentInChildren<Animator>(true) : null;
            SetSceneTarget(animator);
            if (animator != null)
            {
                AppendLog($"対象: プレハブモード '{animator.name}'");
            }
        }

        private void OnPrefabStageClosing(PrefabStage stage)
        {
            _animHandle = Handle<AnimMarker>.Invalid;
            _seqIndex = -1;
            SetSceneTarget(null);
        }

        private void OnEditorUpdate()
        {
            if (_scene == null || _statusLabel == null)
            {
                return;
            }

            var anim = _scene.Manager;
            var playing = anim.IsPlaying(_animHandle);
            if (!playing)
            {
                // 終了済み Handle を毎フレーム問い合わせて無効 Handle 警告を出さないよう Invalid に戻す。
                _animHandle = Handle<AnimMarker>.Invalid;
            }

            // ブレンド確認: 遷移シーケンスの進行(AnimEditorWindow.Blend.cs)。
            TickSequence(anim, ref playing);

            if (!playing && _loopPreview && _target != null && _scene.Current != null && _seqIndex < 0)
            {
                Play();
                playing = true;
            }

            if (playing || _scene.HasActive)
            {
                _timelineContainer?.MarkDirtyRepaint();
                Repaint();
            }

            var t = anim.GetNormalizedTime(_animHandle);
            var status = playing
                ? (anim.IsPaused(_animHandle)
                    ? $"⏸ 一時停止  {t:P0}  (Frame {Mathf.RoundToInt(t * _target.LengthSec * _target.FrameRate)})"
                    : $"● 再生中  {t:P0}  (周回 {anim.GetLoopCount(_animHandle)})")
                : _loopPreview ? "↻ ループ試聴待機" : "■ 停止中";
            if (_statusLabel.text != status)
            {
                _statusLabel.text = status;
            }
        }

        private void OnEventFired(DDrive.Foundation.Manager.InstanceContext ctx, AssetEvent evt)
        {
            var when = evt.Trigger switch
            {
                EventTrigger.Frame => $"Frame {evt.Time:0}",
                EventTrigger.Time => $"{evt.Time:0.##}s",
                _ => evt.Trigger.ToString(),
            };
            AppendLog($"{when}: {evt.Action}{(evt.Target.IsAssigned ? $" {evt.Target.Type} 0x{evt.Target.Id:X}" : string.Empty)}");
        }

        private void AppendLog(string line)
        {
            _eventLog.Add(line);
            while (_eventLog.Count > MaxEventLog)
            {
                _eventLog.RemoveAt(0);
            }

            if (_eventLogLabel != null)
            {
                _eventLogLabel.text = string.Join("\n", _eventLog);
            }
        }

        // ── UI ──

        private void CreateGUI()
        {
            BuildToolbar(rootVisualElement);
            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(scrollView);
            var root = scrollView;
            root.style.paddingLeft = 6;
            root.style.paddingRight = 6;
            root.style.paddingTop = 6;

            _targetField = new ObjectField("対象アセット") { objectType = typeof(AnimData) };
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as AnimData));
            root.Add(_targetField);

            _modelField = new ObjectField("確認用モデル(ModelData)") { objectType = typeof(ModelData), tooltip = "Animator を持つモデル。「確認用シーンを開く」でシーンに配置され、対象になる" };
            _modelField.SetValueWithoutNotify(_model);
            _modelField.RegisterValueChangedCallback(evt =>
            {
                _model = evt.newValue as ModelData;
                OnModelChanged();
            });
            root.Add(_modelField);

            BuildSceneTargetSection(root);

            _modelSummaryLabel = new Label { style = { opacity = 0.75f, marginLeft = 4, whiteSpace = WhiteSpace.Normal } };
            root.Add(_modelSummaryLabel);
            _modelDetailFoldout = new Foldout { text = "モデル情報の詳細(BlendShape 一覧など)", value = false };
            _modelDetailLabel = new Label { style = { opacity = 0.75f, whiteSpace = WhiteSpace.Normal } };
            _modelDetailFoldout.Add(_modelDetailLabel);
            root.Add(_modelDetailFoldout);

            BuildSourceSection(root);
            BuildPlaySection(root);

            _timelineContainer = new IMGUIContainer(DrawTimeline);
            _timelineContainer.style.height = TimelineHeight + 18f;
            root.Add(_timelineContainer);

            RefreshAssetChoices();
            BuildEventListSection(root);
            BuildBlendSection(root);

            var fieldsFoldout = new Foldout { text = "設定(Clip / StateMachine / IK / BlendShape / イベント)", value = true };
            _fieldsContainer = new VisualElement();
            fieldsFoldout.Add(_fieldsContainer);
            root.Add(fieldsFoldout);

            var logFoldout = new Foldout { text = "イベントログ(発火した順)", value = true };
            _eventLogLabel = new Label { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.8f } };
            logFoldout.Add(_eventLogLabel);
            root.Add(logFoldout);

            _validationFoldout = new Foldout { text = "検証", value = true };
            root.Add(_validationFoldout);

            if (_target == null && !_lockTarget && Selection.activeObject is AnimData selected)
            {
                SetTarget(selected);
            }
            else
            {
                RefreshTargetUi();
            }

            // 既にプレハブモードなら、その Animator を対象にする。
            if (_sceneTarget == null)
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.prefabContentsRoot != null)
                {
                    _sceneTarget = stage.prefabContentsRoot.GetComponentInChildren<Animator>(true);
                }
            }

            _sceneTargetField.SetValueWithoutNotify(_sceneTarget);
            RefreshSceneHelp();
            RefreshModelInfo();
        }

        private void BuildToolbar(VisualElement root)
        {
            var toolbar = new Toolbar();
            var lockToggle = new ToolbarToggle { text = "🔒 対象を固定", value = _lockTarget };
            lockToggle.RegisterValueChangedCallback(evt => _lockTarget = evt.newValue);
            toolbar.Add(lockToggle);
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(new ToolbarButton(OpenPreviewScene)
            {
                text = "確認用シーンを開く",
                tooltip = "ライト/カメラ/Volume/床を備えた確認用シーンを開き(無ければ生成)、確認用モデルを配置して対象にする",
            });
            toolbar.Add(new ToolbarButton(OpenModelPrefab) { text = "モデル Prefab を開く", tooltip = "確認用モデルの Prefab をプレハブモードで開き、その Animator を対象にする" });
            toolbar.Add(new ToolbarButton(() => { if (_target != null) EditorGUIUtility.PingObject(_target); }) { text = "Project で表示" });
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(DDrive.Editor.Inspector.NewAssetToolbarButton.CreateToolbarButton(typeof(AnimEditorWindow)));
            root.Add(toolbar);
        }

        private void BuildSceneTargetSection(VisualElement root)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            _sceneTargetField = new ObjectField("シーン上の Animator(対象)") { objectType = typeof(Animator), allowSceneObjects = true, style = { flexGrow = 1f }, tooltip = "動かす相手。確認用シーン / プレハブモードを開くと自動で入る。Hierarchy の別のモデルを入れてもよい" };
            _sceneTargetField.SetValueWithoutNotify(_sceneTarget);
            _sceneTargetField.RegisterValueChangedCallback(evt => SetSceneTarget(evt.newValue as Animator));
            row.Add(_sceneTargetField);
            row.Add(new Button(() =>
            {
                var suggested = SceneAnimPreviewDriver.SuggestTarget();
                if (suggested != null)
                {
                    SetSceneTarget(suggested);
                }
                else
                {
                    AppendLog("⚠ Hierarchy で Animator を持つオブジェクトを選ぶか、「確認用シーンを開く」「モデル Prefab を開く」を押してください");
                }
            }) { text = "選択から取得", tooltip = "Hierarchy の選択(または開いているプレハブモードのルート)の Animator を対象にする" });
            root.Add(row);
            _sceneHelpLabel = new Label { style = { opacity = 0.7f, marginLeft = 4, whiteSpace = WhiteSpace.Normal } };
            root.Add(_sceneHelpLabel);
        }

        private void BuildPlaySection(VisualElement root)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 4 } };
            _playButton = new Button(Play) { text = "▶ 再生" };
            row.Add(_playButton);
            row.Add(new Button(TogglePause) { text = "⏸ 一時停止", tooltip = "その瞬間で止める(タイムラインをクリックしても止まる)。もう一度押すか ▶ で続き" });
            row.Add(new Button(Stop) { text = "■ 停止", tooltip = "再生を止める(ポーズはそのまま残す)" });
            row.Add(new Button(() => _scene?.RestorePoseNow()) { text = "↺ ポーズを戻す", tooltip = "シーン上の対象を再生前のポーズに戻す" });
            var loopToggle = new Toggle("ループ試聴") { value = _loopPreview, tooltip = "終わったら自動でもう一度(データは変更しない)" };
            loopToggle.RegisterValueChangedCallback(evt => _loopPreview = evt.newValue);
            row.Add(loopToggle);
            _statusLabel = new Label("■ 停止中") { style = { marginLeft = 12, opacity = 0.8f } };
            row.Add(_statusLabel);
            root.Add(row);

            var speed = new Slider("速度", 0.1f, 2f) { value = _speed, showInputField = true };
            speed.RegisterValueChangedCallback(evt =>
            {
                _speed = evt.newValue;
                if (_scene != null)
                {
                    _scene.Speed = _speed;
                }
            });
            root.Add(speed);
        }

        private void RebuildFields()
        {
            if (_fieldsContainer == null)
            {
                return;
            }

            _fieldsContainer.Clear();
            _fieldsContainer.Unbind();
            if (_serializedTarget == null)
            {
                _fieldsContainer.Add(new Label("対象アセットを選択してください") { style = { opacity = 0.6f } });
                return;
            }

            foreach (var name in new[] { "Clip", "StateName", "Layer", "Loop", "DefaultCrossFade", "Mask", "Ik", "BlendShapes", "Events" })
            {
                var prop = _serializedTarget.FindProperty(name);
                if (prop != null)
                {
                    _fieldsContainer.Add(new PropertyField(prop));
                }
            }

            _fieldsContainer.Add(new Label("Events の Trigger=Frame は Clip のフレーム番号、Time は秒。Action=PlayAsset で Target に SE/VFX を入れると、シーン上の再生中に同時に鳴る/出る。") { style = { opacity = 0.6f, whiteSpace = WhiteSpace.Normal } });
            _fieldsContainer.Bind(_serializedTarget);
            _fieldsContainer.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                RefreshValidation();
                RefreshEventList();
                _timelineContainer?.MarkDirtyRepaint();
            });
        }

        // ── 対象(シーン上の Animator) ──

        private void OpenPreviewScene()
        {
            Stop();
            VfxPreviewSceneSetup.OpenOrCreate();
            // シーンが開いた(または既に開いていた)ので、確認用モデルを配置して対象にする。
            var animator = EnsureSceneTarget();
            if (animator == null)
            {
                AppendLog(_model == null ? "⚠ 確認用モデルを入れると、確認用シーンに配置して動かせます" : "⚠ 確認用モデルの Prefab に Animator がありません");
            }
        }

        private void OpenModelPrefab()
        {
            if (_model != null && _model.Prefab != null)
            {
                Stop();
                AssetDatabase.OpenAsset(_model.Prefab); // prefabStageOpened で対象が自動で入る
            }
            else
            {
                AppendLog("⚠ 確認用モデル(Prefab 付き)が未設定です");
            }
        }

        private void OnModelChanged()
        {
            Stop();
            if (_target is Anim2DData && _model != null)
            {
                // 2D の対象では確認用モデル(3D)は使わない。入れられても None に戻す(2026-09-11)
                _model = null;
                _modelField?.SetValueWithoutNotify(null);
                AppendLog("⚠ Anim2DData では確認用モデル(3D)は使いません(None に戻しました)。プレビュー物は「確認用シーンを開く」で配置されます");
                RefreshSceneHelp();
                RefreshModelInfo();
                return;
            }

            DestroyAnim2DPreview(); // ModelData を設定/変更したら Anim2D フォールバックの配置物は手放す
            // 自前で配置していた分は手放す(次の ▶ / 確認用シーンを開くで新しいモデルを配置)。
            if (_scene != null && _scene.OwnsCurrent)
            {
                _scene.ReleaseTarget();
                _sceneTarget = null;
                _sceneTargetField?.SetValueWithoutNotify(null);
            }

            RefreshSceneHelp();
            RefreshModelInfo();
            RefreshValidation();
        }

        private void SetSceneTarget(Animator animator)
        {
            Stop();
            // 手動で別の Animator が指定された(Anim2D フォールバック物ではない)ら、配置物は手放す。
            if (_anim2DPreview != null && (animator == null || animator != _anim2DPreview.GetComponent<Animator>()))
            {
                DestroyAnim2DPreview();
            }

            if (_scene != null && animator != _scene.Current)
            {
                _scene.ReleaseTarget();
            }

            _sceneTarget = animator;
            _sceneTargetField?.SetValueWithoutNotify(animator);
            RefreshSceneHelp();
            RefreshModelInfo();
            RefreshValidation();
        }

        // 対象が Anim2DData で ModelData が未設定のときのフォールバック(2026-09-10、3-13):
        // [D-Drive] Anim2D Preview(SpriteRenderer + Animator、DontSave)を配置して対象にする。
        private Animator EnsureAnim2DPreviewTarget(Anim2DData anim2D)
        {
            if (_scene == null)
            {
                return null;
            }

            // シーンに既にあるプレビュー物(Anim2D Editor が置いたもの等)は再利用し、二重配置しない(2026-09-11)。
            var hadPreview = _anim2DPreview != null;
            _anim2DPreview = Anim2DPreviewObject.FindOrCreate(anim2D);
            if (!hadPreview)
            {
                AppendLog("確認用 Anim2D プレビュー物(SpriteRenderer + Animator)を配置");
            }

            var animator = _anim2DPreview.GetComponent<Animator>();
            _scene.SetTarget(animator);
            _sceneTarget = animator;
            _sceneTargetField?.SetValueWithoutNotify(animator);
            RefreshSceneHelp();
            RefreshModelInfo();
            RefreshValidation();
            return animator;
        }

        private void DestroyAnim2DPreview()
        {
            if (_anim2DPreview == null)
            {
                return;
            }

            Anim2DPreviewObject.Destroy(_anim2DPreview);
            _anim2DPreview = null;
        }

        // 再生対象を確定する: 指定 Animator > 配置済みの自前モデル > 確認用モデルを配置。配置したものは欄にも反映する。
        private Animator EnsureSceneTarget()
        {
            if (_scene == null)
            {
                return null;
            }

            if (_sceneTarget != null)
            {
                if (_scene.Current != _sceneTarget)
                {
                    _scene.SetTarget(_sceneTarget);
                }

                return _scene.Current;
            }

            if (_scene.Current != null && _scene.OwnsCurrent)
            {
                return _scene.Current;
            }

            // Anim2DData は Prefab を持たないので確認用モデル(3D)より先に 2D プレビュー物を使う(2026-09-11)。
            if (_target is Anim2DData anim2DFirst)
            {
                return EnsureAnim2DPreviewTarget(anim2DFirst);
            }

            if (_model != null)
            {
                var animator = _scene.SpawnModel(_model, Vector3.zero, Quaternion.identity);
                if (animator != null)
                {
                    _sceneTarget = animator;
                    _sceneTargetField?.SetValueWithoutNotify(animator);
                    AppendLog($"確認用モデル '{_model.DisplayName ?? _model.name}' を配置");
                }

                RefreshSceneHelp();
                RefreshModelInfo();
                RefreshValidation();
                return animator;
            }

            // ModelData(3D)が未設定でも、対象が Anim2DData なら SpriteRenderer + Animator のプレビュー物で代用する。
            if (_target is Anim2DData anim2D)
            {
                return EnsureAnim2DPreviewTarget(anim2D);
            }

            return null;
        }

        private void RefreshSceneHelp()
        {
            if (_sceneHelpLabel == null)
            {
                return;
            }

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            var where = stage != null
                ? $"プレハブモード '{System.IO.Path.GetFileNameWithoutExtension(stage.assetPath)}'"
                : $"シーン '{SceneManager.GetActiveScene().name}'";
            if (_sceneTarget != null)
            {
                var owned = _scene != null && _scene.OwnsCurrent && _scene.Current == _sceneTarget;
                _sceneHelpLabel.text = owned
                    ? $"対象: {_sceneTarget.name}({where} に配置した確認用モデル、保存されません)。SceneView で確認できます。"
                    : $"対象: {_sceneTarget.name}({where})。停止すると再生前のポーズに戻し、シーン / Prefab には保存されません。";
            }
            else if (_model != null)
            {
                _sceneHelpLabel.text = $"対象なし。「確認用シーンを開く」で確認用モデル '{_model.DisplayName ?? _model.name}' を配置するか、「モデル Prefab を開く」でプレハブモードのモデルを対象にします(▶ でも {where} の原点に配置します)。";
            }
            else if (_target is Anim2DData)
            {
                _sceneHelpLabel.text = $"対象なし。「確認用シーンを開く」で Anim2D プレビュー物({where} に配置、保存されません)を出すか、Hierarchy でモデルを選んで「選択から取得」を押してください。";
            }
            else
            {
                _sceneHelpLabel.text = "確認用モデルを入れて「確認用シーンを開く」か、Hierarchy でモデルを選んで「選択から取得」を押してください。";
            }
        }

        // ── 対象アセット ──

        public void SetTarget(AnimData data)
        {
            Stop();
            var changed = _target != data;
            _target = data;
            _targetField?.SetValueWithoutNotify(data);

            if (changed)
            {
                // 対象の切り替えで前の対象の配置物・確認用モデルを引き継がない(2026-09-11: 人による確認で判明)。
                if (data is Anim2DData)
                {
                    // 2D は Prefab を持たないので確認用モデル(3D)は使わない。前の 3D 対象で配置したモデルも手放す
                    if (_model != null)
                    {
                        _model = null;
                        _modelField?.SetValueWithoutNotify(null);
                    }

                    // 手で入れた 3D の Animator も含めて対象を必ず外す(残すと EnsureSceneTarget がそれを返し、
                    // 3D のリグで 2D の Clip を再生してしまう。2026-09-11 レビュー対応)
                    _scene?.ReleaseTarget();
                    _sceneTarget = null;
                    _sceneTargetField?.SetValueWithoutNotify(null);
                }
                else if (_anim2DPreview != null)
                {
                    // 3D に切り替えたら 2D の配置物は手放す
                    var previewAnimator = _anim2DPreview.GetComponent<Animator>();
                    if (_sceneTarget == previewAnimator)
                    {
                        _scene?.ReleaseTarget();
                        _sceneTarget = null;
                        _sceneTargetField?.SetValueWithoutNotify(null);
                    }

                    DestroyAnim2DPreview();
                }

                RefreshSceneHelp();
                RefreshModelInfo();
            }

            // シーンに既にプレビュー物があれば(Anim2D Editor から引き渡された物、または前の対象の物)、対象の変更有無に
            // 関係なく新しい Data の先頭スプライトに差し替えて対象にする(前の絵を残さず、スプライトも消さない。2026-09-11)。
            if (_target is Anim2DData current && (_anim2DPreview != null || Anim2DPreviewObject.FindExisting() != null))
            {
                EnsureAnim2DPreviewTarget(current);
            }

            RefreshTargetUi();
        }

        private void RefreshTargetUi()
        {
            if (_targetField == null)
            {
                return;
            }

            _serializedTarget = _target != null ? new SerializedObject(_target) : null;
            _targetField.SetValueWithoutNotify(_target);
            RebuildFields();
            RefreshEventList();
            RefreshValidation();
            _timelineContainer?.MarkDirtyRepaint();
        }

        // モデル情報: 1 行の要約 + 折りたたみの詳細(BlendShape 名の一覧は長いので畳んでおく)。
        private void RefreshModelInfo()
        {
            if (_modelSummaryLabel == null)
            {
                return;
            }

            var animator = _scene?.Current != null ? _scene.Current : _sceneTarget;
            RefreshBlendUi(animator);
            RefreshSourceStates(animator);
            if (animator == null)
            {
                _modelSummaryLabel.text = _model == null
                    ? "モデル情報: 対象が決まるとここに Controller と BlendShape の要約が出ます。"
                    : $"モデル情報: 確認用モデル '{_model.DisplayName ?? _model.name}'(未配置)";
                _modelDetailLabel.text = string.Empty;
                _modelDetailFoldout.style.display = DisplayStyle.None;
                return;
            }

            var shapes = new List<string>();
            foreach (var smr in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null)
                {
                    continue;
                }

                for (var i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                {
                    shapes.Add(smr.sharedMesh.GetBlendShapeName(i));
                }
            }

            var controller = animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "なし(Clip を直接サンプリング)";
            var proxy = animator.GetComponent<AnimatorProxy>();
            var ik = proxy != null && (proxy.LeftHandTarget != null || proxy.RightHandTarget != null || proxy.LeftFootTarget != null || proxy.RightFootTarget != null) ? " / IK ターゲットあり" : string.Empty;
            _modelSummaryLabel.text = $"Controller: {controller} / BlendShape {shapes.Count} 個{ik}";

            var sb = new StringBuilder();
            sb.Append("Animator: ").Append(animator.name).Append('\n');
            sb.Append("Controller: ").Append(controller).Append('\n');
            sb.Append(shapes.Count > 0 ? "BlendShape: " + string.Join(", ", shapes) : "BlendShape: なし");
            _modelDetailLabel.text = sb.ToString();
            _modelDetailFoldout.style.display = DisplayStyle.Flex;
        }

        // ── 再生 ──

        private void Play()
        {
            if (_target == null || _scene == null)
            {
                return;
            }

            // 一時停止中の ▶ は続きから(最初からにしない)。
            if (_scene.IsPaused && _scene.Manager.IsPlaying(_animHandle))
            {
                _scene.SetPaused(_animHandle, false);
                AppendLog("▶ 再開");
                return;
            }

            var animator = EnsureSceneTarget();
            if (animator == null)
            {
                AppendLog("⚠ 対象がありません。「確認用シーンを開く」「モデル Prefab を開く」か、シーン上の Animator を指定してください");
                return;
            }

            _seqIndex = -1;
            _scene.StopSpawned(); // 最初に戻るので前回の SE / VFX は切る
            _animHandle = _scene.Play(_target, animator);
            AppendLog($"▶ '{_target.DisplayName ?? _target.name}' on {animator.name}");
            RefreshValidation();
        }

        private void Stop()
        {
            _seqIndex = -1;
            if (_scene != null)
            {
                if (_scene.Manager.IsPlaying(_animHandle))
                {
                    _scene.Manager.Stop(_animHandle);
                }

                _scene.Stop(); // ポーズはその瞬間のまま(戻すのは「↺ ポーズを戻す」)
            }

            _animHandle = Handle<AnimMarker>.Invalid;
        }

        // 一時停止 / 再開。停止中なら ▶ と同じ。
        private void TogglePause()
        {
            if (_scene == null)
            {
                return;
            }

            if (!_scene.Manager.IsPlaying(_animHandle))
            {
                Play();
                return;
            }

            var paused = _scene.IsPaused;
            _scene.SetPaused(_animHandle, !paused);
            AppendLog(paused ? "▶ 再開" : "⏸ 一時停止");
        }

        // ── タイムライン(シーク + イベントマーカーのドラッグ) ──

        private void DrawTimeline()
        {
            var rect = GUILayoutUtility.GetRect(100, TimelineHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f));
            if (_target == null)
            {
                GUI.Label(rect, "対象アセットが未選択です", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var length = _target.LengthSec;
            var frameRate = _target.FrameRate;
            var bar = new Rect(rect.x + 8f, rect.y + 18f, rect.width - 16f, 8f);
            EditorGUI.DrawRect(bar, new Color(0.3f, 0.3f, 0.3f));

            // 目盛り: フレームごと(細)+ ラベル付き(太)。ラベル間隔は幅に応じて間引く(OH_CASE2026_ITAMI の SE タイムライン相当、2026-09-11)。
            // 2026-09-14(5-4): PresentationEditor と共用するため TimelineRulerGui へ切り出し(見た目は不変)。
            var totalFrames = Mathf.Max(1, Mathf.RoundToInt(length * frameRate));
            TimelineRulerGui.DrawTicks(bar, totalFrames, f => f.ToString());

            GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, rect.width - 12f, 14f),
                $"0s  —  {length:0.##}s ({length * frameRate:0} フレーム @ {frameRate:0}fps)   クリック: シーク / マーカーをドラッグ: イベント時刻の変更",
                EditorStyles.miniLabel);

            // SE 波形(マーカー位置から SE の長さぶん)
            DrawSeWaveforms(bar, length);

            // イベントマーカー
            var events = _target.Events;
            var evt = Event.current;
            if (events != null)
            {
                for (var i = 0; i < events.Length; i++)
                {
                    var e = events[i];
                    float sec;
                    switch (e.Trigger)
                    {
                        case EventTrigger.Frame: sec = e.Time / frameRate; break;
                        case EventTrigger.Time: sec = e.Time; break;
                        default: continue;
                    }

                    var x = bar.x + bar.width * Mathf.Clamp01(sec / length);
                    var marker = new Rect(x - 5f, bar.y - 6f, 10f, bar.height + 12f);
                    var over = sec > length + 1e-3f;
                    EditorGUI.DrawRect(marker, over ? new Color(1f, 0.3f, 0.3f) : (e.Action == EventAction.PlayAsset ? new Color(1f, 0.8f, 0.3f) : new Color(0.6f, 0.8f, 1f)));
                    // 時刻 + 対象名(SE / VFX)をマーカー直下に出す
                    var asset = e.Action == EventAction.PlayAsset ? FindAsset(e.Target) : null;
                    var timeText = e.Trigger == EventTrigger.Frame ? $"F{e.Time:0}" : $"{e.Time:0.##}s";
                    var nameText = asset != null ? (asset.DisplayName ?? asset.name) : null;
                    GUI.Label(new Rect(x - 40f, bar.y + 10f, 80f, 14f), nameText != null ? $"{timeText} {nameText}" : timeText, EditorStyles.centeredGreyMiniLabel);

                    if (evt.type == EventType.MouseDown && marker.Contains(evt.mousePosition))
                    {
                        _draggingEvent = i;
                        _dragUndoGroup = Undo.GetCurrentGroup();
                        evt.Use();
                    }
                }
            }

            // 再生ヘッド
            var anim = _scene?.Manager;
            var normalized = anim != null ? anim.GetNormalizedTime(_animHandle) : -1f;
            if (normalized >= 0f)
            {
                var px = bar.x + bar.width * normalized;
                EditorGUI.DrawRect(new Rect(px - 1f, bar.y - 8f, 2f, bar.height + 16f), Color.white);
            }

            switch (evt.type)
            {
                case EventType.MouseDrag when _draggingEvent >= 0 && events != null && _draggingEvent < events.Length:
                {
                    // MouseDown だけの RecordObject は変更前に記録が終わって Undo が効かないため、ドラッグごとに記録し MouseUp で 1 つにまとめる。
                    Undo.RecordObject(_target, "Move Anim Event");
                    var sec = Mathf.Clamp((evt.mousePosition.x - bar.x) / bar.width, 0f, 1f) * length;
                    var e = events[_draggingEvent];
                    e.Time = e.Trigger == EventTrigger.Frame ? Mathf.Round(sec * frameRate) : Mathf.Round(sec * 100f) / 100f;
                    events[_draggingEvent] = e;
                    EditorUtility.SetDirty(_target);
                    _serializedTarget?.Update();
                    evt.Use();
                    break;
                }
                case EventType.MouseUp when _draggingEvent >= 0:
                    _draggingEvent = -1;
                    Undo.CollapseUndoOperations(_dragUndoGroup);
                    RefreshValidation();
                    evt.Use();
                    break;
                case EventType.MouseDown when rect.Contains(evt.mousePosition):
                {
                    // 空いている場所のクリックはシーク(再生中でなければ再生を開始してその位置へ)。
                    var t = Mathf.Clamp01((evt.mousePosition.x - bar.x) / bar.width);
                    var animator = EnsureSceneTarget();
                    if (anim != null && animator != null)
                    {
                        if (!anim.IsPlaying(_animHandle))
                        {
                            _animHandle = _scene.Play(_target, animator, 0f);
                        }

                        // クリックした瞬間で止める(ポーズはその位置に更新される)。続きは ▶ か ⏸。
                        _scene.SetPaused(_animHandle, true);
                        _scene.Seek(_animHandle, t);
                    }

                    evt.Use();
                    break;
                }
            }
        }

        // ── Validation(静的 + 対象モデルに対する実行時検査) ──

        private void RefreshValidation()
        {
            if (_validationFoldout == null)
            {
                return;
            }

            _validationFoldout.Clear();
            if (_target == null)
            {
                return;
            }

            var any = false;
            foreach (var result in new AnimDataValidator().Validate(_target, new ValidationContext(new List<AssetDataBase> { _target })))
            {
                any = true;
                _validationFoldout.Add(new HelpBox(result.Message, result.Severity == ValidationSeverity.Error ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning));
            }

            var animator = _scene?.Current != null ? _scene.Current : _sceneTarget;
            if (animator != null)
            {
                var state = _target.ResolvedStateName;
                if (animator.runtimeAnimatorController != null && !string.IsNullOrEmpty(state) &&
                    (_target.Layer >= animator.layerCount || !animator.HasState(_target.Layer, Animator.StringToHash(state))))
                {
                    any = true;
                    _validationFoldout.Add(new HelpBox($"StateName '{state}'(Layer {_target.Layer}) が対象モデルの Controller にありません", HelpBoxMessageType.Error));
                }

                if (_target.BlendShapes != null)
                {
                    foreach (var track in _target.BlendShapes)
                    {
                        if (!string.IsNullOrEmpty(track.ShapeName) && !HasBlendShape(animator, track.ShapeName))
                        {
                            any = true;
                            _validationFoldout.Add(new HelpBox($"BlendShape '{track.ShapeName}' が対象モデルにありません", HelpBoxMessageType.Warning));
                        }
                    }
                }
            }

            if (!any)
            {
                _validationFoldout.Add(new Label("Validation に問題はありません。") { style = { opacity = 0.6f } });
            }
        }

        // シーン上の借用モデルにコンポーネントを足さずに BlendShape 名を確認する。
        private static bool HasBlendShape(Animator animator, string shapeName)
        {
            foreach (var smr in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh != null && smr.sharedMesh.GetBlendShapeIndex(shapeName) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
