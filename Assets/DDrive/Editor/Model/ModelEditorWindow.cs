using System.Collections.Generic;
using DDrive.Editor.Anim;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Foundation.Handle;
using DDrive.Runtime.Model;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DDrive.Editor.Model
{
    // [05_model_animation.md] A-4 — Model 専用エディタ(2-6)。
    // 2026-09-10: AnimEditor と同じく SceneView 方式に統一(ウィンドウ内ビューポートと背景/ライト切替は廃止。
    // ライト・ポスプロは確認用シーンのものをそのまま使う)。
    //  - 「確認用シーンを開く」→ 対象モデルをシーンに配置(SceneAnimPreviewDriver、DontSave)して SceneView で確認
    //  - 「Prefab を開く」→ ModelData.Prefab をプレハブモードで開く(Renderer / Material をその場で編集)
    //  - ターンテーブル(配置したモデルを回す) / 複数モデル並列表示 / Slot 自動収集 / Material スロット ID 差し替え /
    //    DefaultAnimation の再生確認(実 AnimManager。Anim も EditorAnchorRegistry に登録済み)
    [DDrive.Editor.Inspector.DataEditor(typeof(ModelData), "Model Editor で開く")]
    public sealed class ModelEditorWindow : EditorWindow
    {
        private const int MaxParallelSlots = 4;
        private const float ParallelSpacingMeters = 2f;

        private sealed class SlotState
        {
            public ModelData Data;
            public Handle<ModelMarker> Handle;
            public ObjectField Field;
            public Button ToggleButton;
        }

        [SerializeField] private ModelData _target;
        [SerializeField] private bool _lockTarget;
        [SerializeField] private bool _turntableEnabled;
        [SerializeField] private float _turntableSpeedDegPerSec = 30f;

        private SceneAnimPreviewDriver _scene;
        private SerializedObject _serializedTarget;
        private readonly List<SlotState> _slots = new();
        private double _lastTurntableTime;

        private ObjectField _targetField;
        private Label _statusLabel;
        private VisualElement _slotsContainer;
        private VisualElement _animContainer;
        private Button _applySlotsButton;

        [MenuItem(DDriveMenu.Editors + "Model")]
        public static void OpenFromMenu() => Open(Selection.activeObject as ModelData);

        public static void Open(ModelData target)
        {
            var window = GetWindow<ModelEditorWindow>("Model Editor");
            window.minSize = new Vector2(520, 420);
            if (target != null)
            {
                window.SetTarget(target);
            }
        }

        // ── ライフサイクル ──

        private void OnEnable()
        {
            _scene = new SceneAnimPreviewDriver();
            _lastTurntableTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
        }

        private void OnDisable()
        {
            PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
            PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            EditorApplication.update -= OnEditorUpdate;
            _scene?.Dispose();
            _scene = null;
        }

        private void OnSelectionChange()
        {
            if (!_lockTarget && Selection.activeObject is ModelData selected && selected != _target)
            {
                SetTarget(selected);
            }
        }

        private void OnActiveSceneChanged(Scene previous, Scene current) => OnStageChanged();

        private void OnPrefabStageChanged(PrefabStage stage) => OnStageChanged();

        // ドライバはシーン切替で配置物を自動撤去済み。UI の状態だけ合わせる。
        private void OnStageChanged()
        {
            foreach (var slot in _slots)
            {
                slot.Handle = Handle<ModelMarker>.Invalid;
                if (slot.ToggleButton != null)
                {
                    slot.ToggleButton.text = "配置";
                }
            }

            RefreshStatus();
            RebuildMaterialUi();
        }

        private void OnEditorUpdate()
        {
            var root = _scene != null && _scene.OwnsCurrent ? _scene.CurrentRoot : null;
            if (!_turntableEnabled || root == null)
            {
                _lastTurntableTime = EditorApplication.timeSinceStartup;
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastTurntableTime), 0f, 0.25f);
            _lastTurntableTime = now;
            root.transform.Rotate(Vector3.up, _turntableSpeedDegPerSec * dt, Space.World);
            SceneView.RepaintAll();
        }

        // ── UI ──

        private void CreateGUI()
        {
            BuildToolbar(rootVisualElement);

            // ウィンドウが小さい/セクションが増えても内容が見切れないよう、ルートをスクロール可能にする([09] §7)。
            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(scrollView);
            var root = scrollView;
            root.style.paddingLeft = 6;
            root.style.paddingRight = 6;
            root.style.paddingTop = 6;

            _targetField = new ObjectField("対象アセット") { objectType = typeof(ModelData) };
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as ModelData));
            root.Add(_targetField);

            var playRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 6 } };
            playRow.Add(new Button(PlaceMain) { text = "▶ シーンに配置", tooltip = "開いているシーン / プレハブモードの原点に対象を配置する(保存されない)" });
            playRow.Add(new Button(RemoveMain) { text = "■ 撤去" });
            _statusLabel = new Label { style = { marginLeft = 8, opacity = 0.8f, whiteSpace = WhiteSpace.Normal, flexShrink = 1f } };
            playRow.Add(_statusLabel);
            root.Add(playRow);

            BuildTurntableSection(root);
            BuildMultiSlotSection(root);
            BuildMaterialSection(root);

            _animContainer = new VisualElement();
            root.Add(_animContainer);

            if (_target == null && !_lockTarget && Selection.activeObject is ModelData selected)
            {
                SetTarget(selected);
            }
            else
            {
                RefreshTargetUi();
            }

            RefreshStatus();
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
                tooltip = "ライト/カメラ/Volume/床を備えた確認用シーンを開き(無ければ生成)、対象を原点に配置する",
            });
            toolbar.Add(new ToolbarButton(OpenPrefab) { text = "Prefab を開く", tooltip = "ModelData.Prefab をプレハブモードで開く(Renderer / Material をその場で編集)" });
            toolbar.Add(new ToolbarButton(() => { if (_target != null) EditorGUIUtility.PingObject(_target); }) { text = "Project で表示" });
            root.Add(toolbar);
        }

        private void BuildTurntableSection(VisualElement root)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var toggle = new Toggle("ターンテーブル自動回転") { value = _turntableEnabled, tooltip = "シーンに配置したモデルを Y 軸で回す(プレハブモードの実体は回さない)" };
            toggle.RegisterValueChangedCallback(evt => _turntableEnabled = evt.newValue);
            row.Add(toggle);
            var speedSlider = new Slider("速度(度/秒)", -180f, 180f) { value = _turntableSpeedDegPerSec, style = { flexGrow = 1f } };
            speedSlider.RegisterValueChangedCallback(evt => _turntableSpeedDegPerSec = evt.newValue);
            row.Add(speedSlider);
            root.Add(row);
        }

        // ── 複数モデル並列表示 ──

        private void BuildMultiSlotSection(VisualElement root)
        {
            var foldout = new Foldout { text = $"複数モデル並列表示(最大 {MaxParallelSlots}、対象の隣に 2m 間隔で配置)", value = false };
            for (var i = 0; i < MaxParallelSlots; i++)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                var slot = new SlotState();
                slot.Field = new ObjectField { objectType = typeof(ModelData), style = { flexGrow = 1f } };
                row.Add(slot.Field);
                slot.ToggleButton = new Button(() => ToggleSlot(slot)) { text = "配置" };
                slot.ToggleButton.style.width = 48;
                row.Add(slot.ToggleButton);
                foldout.Add(row);
                _slots.Add(slot);
            }

            root.Add(foldout);
        }

        private void ToggleSlot(SlotState slot)
        {
            if (_scene == null)
            {
                return;
            }

            if (_scene.Models != null && _scene.Models.IsValid(slot.Handle))
            {
                _scene.DespawnExtraModel(slot.Handle);
                slot.Handle = Handle<ModelMarker>.Invalid;
                slot.ToggleButton.text = "配置";
                return;
            }

            slot.Data = slot.Field.value as ModelData;
            if (slot.Data == null)
            {
                return;
            }

            var index = _slots.IndexOf(slot) + 1; // 対象(原点)の隣から並べる
            slot.Handle = _scene.SpawnExtraModel(slot.Data, new Vector3(index * ParallelSpacingMeters, 0f, 0f), Quaternion.identity);
            slot.ToggleButton.text = _scene.Models != null && _scene.Models.IsValid(slot.Handle) ? "撤去" : "配置";
        }

        // ── Material スロット(自動収集 + ID 差し替え。AssetIdDrawer が自動適用される) ──

        private void BuildMaterialSection(VisualElement root)
        {
            var foldout = new Foldout { text = "Material スロット", value = true };
            foldout.Add(new Button(CollectSlots) { text = "Slot 自動収集(Prefab の Renderer を走査)" });
            _slotsContainer = new VisualElement();
            foldout.Add(_slotsContainer);
            root.Add(foldout);
        }

        private void RebuildMaterialUi()
        {
            if (_slotsContainer == null)
            {
                return;
            }

            _slotsContainer.Clear();
            _applySlotsButton = null;
            if (_serializedTarget == null)
            {
                return;
            }

            var slotsProp = _serializedTarget.FindProperty("Slots");
            if (slotsProp == null)
            {
                return;
            }

            var field = new PropertyField(slotsProp, "Slots(RendererPath / SlotIndex / Material)");
            field.Bind(_serializedTarget);
            _slotsContainer.Add(field);

            _applySlotsButton = new Button(ApplySlotsToPreview) { text = "配置中のモデルに反映", tooltip = "Slots の Material ID を、配置中のモデルの Renderer に実 MaterialManager 経由で適用する(Spawn 時と同じ経路)" };
            _applySlotsButton.SetEnabled(_scene != null && _scene.OwnsCurrent);
            _slotsContainer.Add(_applySlotsButton);
        }

        private void CollectSlots()
        {
            if (_target == null || _target.Prefab == null)
            {
                return;
            }

            var renderers = _target.Prefab.GetComponentsInChildren<Renderer>(true);
            var existing = _target.Slots ?? System.Array.Empty<MaterialSlot>();
            var collected = new List<MaterialSlot>();
            foreach (var renderer in renderers)
            {
                var path = GetRelativePath(_target.Prefab.transform, renderer.transform);
                var materialCount = renderer.sharedMaterials.Length;
                for (var slotIndex = 0; slotIndex < materialCount; slotIndex++)
                {
                    var preserved = FindExisting(existing, path, slotIndex);
                    collected.Add(new MaterialSlot { RendererPath = path, SlotIndex = slotIndex, Material = preserved.Material });
                }
            }

            Undo.RecordObject(_target, "Collect Model Slots");
            _target.Slots = collected.ToArray();
            EditorUtility.SetDirty(_target);
            RefreshTargetUi();
        }

        private static MaterialSlot FindExisting(MaterialSlot[] slots, string path, int slotIndex)
        {
            foreach (var slot in slots)
            {
                if (slot.RendererPath == path && slot.SlotIndex == slotIndex)
                {
                    return slot;
                }
            }

            return default;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root)
            {
                return string.Empty;
            }

            var segments = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                segments.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", segments);
        }

        private void ApplySlotsToPreview()
        {
            if (_target == null || _target.Slots == null || _scene == null || !_scene.OwnsCurrent)
            {
                return;
            }

            for (var i = 0; i < _target.Slots.Length; i++)
            {
                _scene.Models.SetMaterial(_scene.SpawnedModelHandle, i, _target.Slots[i].Material);
            }
        }

        // ── DefaultAnimation(実 AnimManager で再生確認) ──

        private void RebuildAnimUi()
        {
            if (_animContainer == null)
            {
                return;
            }

            _animContainer.Clear();
            if (_serializedTarget == null)
            {
                return;
            }

            var animProp = _serializedTarget.FindProperty("DefaultAnimation");
            if (animProp == null)
            {
                return;
            }

            var foldout = new Foldout { text = "Default Animation", value = true };
            var field = new PropertyField(animProp);
            field.Bind(_serializedTarget);
            foldout.Add(field);
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            row.Add(new Button(PlayDefaultAnimation) { text = "▶ 配置して DefaultAnimation を再生" });
            row.Add(new Button(() => _scene?.Stop()) { text = "■ 停止" });
            foldout.Add(row);
            foldout.Add(new Label("配置時に AnimManager が DefaultAnimation を自動再生します(ランタイムの Models.Spawn と同じ経路)。細かい確認は Anim Editor で。") { style = { opacity = 0.7f, whiteSpace = WhiteSpace.Normal } });
            _animContainer.Add(foldout);
        }

        private void PlayDefaultAnimation()
        {
            if (_target == null || _scene == null)
            {
                return;
            }

            if (!_target.DefaultAnimation.IsValid)
            {
                Debug.LogWarning("[DDrive] DefaultAnimation が未設定です。");
                return;
            }

            PlaceMain(); // 配置と同時に DefaultAnimation が再生される
            if (_scene.OwnsCurrent && _scene.Current != null && _scene.Manager.ActiveCount == 0)
            {
                _scene.Manager.Play(_target.DefaultAnimation, _scene.Current);
            }
        }

        // ── 対象 / 配置 ──

        public void SetTarget(ModelData data)
        {
            RemoveMain();
            _target = data;
            _targetField?.SetValueWithoutNotify(data);
            RefreshTargetUi();
        }

        private void RefreshTargetUi()
        {
            _serializedTarget = _target != null ? new SerializedObject(_target) : null;
            RebuildMaterialUi();
            RebuildAnimUi();
            RefreshStatus();
        }

        private void OpenPreviewScene()
        {
            RemoveMain();
            VfxPreviewSceneSetup.OpenOrCreate();
            PlaceMain();
        }

        private void OpenPrefab()
        {
            if (_target != null && _target.Prefab != null)
            {
                RemoveMain();
                AssetDatabase.OpenAsset(_target.Prefab);
            }
            else
            {
                Debug.LogWarning("[DDrive] 対象 ModelData に Prefab がありません。");
            }
        }

        private void PlaceMain()
        {
            if (_target == null || _scene == null)
            {
                return;
            }

            RemoveMain();
            if (_target.Prefab == null)
            {
                Debug.LogWarning("[DDrive] 対象 ModelData に Prefab がありません。");
                RefreshStatus();
                return;
            }

            _scene.SpawnModel(_target, Vector3.zero, Quaternion.identity, requireAnimator: false);
            RefreshStatus();
            RebuildMaterialUi();
            Selection.activeGameObject = _scene.CurrentRoot;
        }

        private void RemoveMain()
        {
            if (_scene != null && _scene.OwnsCurrent)
            {
                _scene.ReleaseTarget();
            }

            foreach (var slot in _slots)
            {
                slot.Handle = Handle<ModelMarker>.Invalid;
                if (slot.ToggleButton != null)
                {
                    slot.ToggleButton.text = "配置";
                }
            }

            RefreshStatus();
            RebuildMaterialUi();
        }

        private void RefreshStatus()
        {
            if (_statusLabel == null)
            {
                return;
            }

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            var where = stage != null
                ? $"プレハブモード '{System.IO.Path.GetFileNameWithoutExtension(stage.assetPath)}'"
                : $"シーン '{SceneManager.GetActiveScene().name}'";
            if (_scene != null && _scene.OwnsCurrent && _scene.CurrentRoot != null)
            {
                _statusLabel.text = $"● {where} に配置中: {_scene.CurrentRoot.name}(保存されません)";
            }
            else if (stage != null && _target != null && _target.Prefab != null && stage.assetPath == AssetDatabase.GetAssetPath(_target.Prefab))
            {
                _statusLabel.text = $"● {where} を編集中(対象の Prefab そのもの)。Ctrl+S で保存";
            }
            else
            {
                _statusLabel.text = _target == null ? "対象アセットを選んでください" : $"未配置。「確認用シーンを開く」か「▶ シーンに配置」で {where} に置きます";
            }
        }
    }
}
