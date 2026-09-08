using System.Collections.Generic;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Foundation.Handle;
using DDrive.Runtime.Model;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Model
{
    // [05_model_animation.md] A-4 — Model 専用エディタ(2-6)。
    // ターンテーブル回転 / 複数モデル並列表示 / 背景・ライト切替 / Slot 自動収集 /
    // Material スロット差し替え(ID 保存。実適用は Phase 3 の MaterialData 実装後)。
    public sealed class ModelEditorWindow : EditorWindow
    {
        private const int MaxParallelSlots = 4;
        private const float ViewportHeight = 240f;
        private const float ParallelSpacingMeters = 2f;

        private sealed class SlotState
        {
            public ModelData Data;
            public Handle<ModelMarker> Handle;
            public ObjectField Field;
            public Button ToggleButton;
        }

        private ModelData _target;
        private PreviewService _preview;
        private Handle<ModelMarker> _mainHandle;
        private SerializedObject _serializedTarget;

        private readonly OrbitCameraController _orbit = new();
        private readonly List<SlotState> _slots = new();

        private ObjectField _targetField;
        private IMGUIContainer _viewportContainer;
        private VisualElement _slotsContainer;
        private VisualElement _animContainer;

        private bool _turntableEnabled;
        private float _turntableSpeedDegPerSec = 30f;
        private double _lastTurntableTime;

        [MenuItem(DDriveMenu.Editors + "Model")]
        public static void OpenFromMenu() => Open(Selection.activeObject as ModelData);

        public static void Open(ModelData target)
        {
            var window = GetWindow<ModelEditorWindow>("Model Editor");
            window.minSize = new Vector2(520, 480);
            if (target != null)
            {
                window.SetTarget(target);
            }
        }

        private void OnEnable()
        {
            _preview = new PreviewService();
            _preview.Initialize();
            _lastTurntableTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            _preview?.Dispose();
            _preview = null;
        }

        private void OnEditorUpdate()
        {
            if (!_turntableEnabled || !_preview.ModelsManager.IsValid(_mainHandle))
            {
                _lastTurntableTime = EditorApplication.timeSinceStartup;
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastTurntableTime), 0f, 0.25f);
            _lastTurntableTime = now;

            var go = _preview.ModelsManager.GetGameObject(_mainHandle);
            if (go != null)
            {
                go.transform.Rotate(Vector3.up, _turntableSpeedDegPerSec * dt, Space.World);
            }

            Repaint();
        }

        private void CreateGUI()
        {
            // ウィンドウが小さい/セクションが増えても内容が見切れないよう、ルートをスクロール可能にする
            // ([09_editor_tools.md] §7 拡縮前提のUI規約)。
            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(scrollView);

            var root = scrollView;
            root.style.paddingLeft = 6;
            root.style.paddingRight = 6;
            root.style.paddingTop = 6;

            _targetField = new ObjectField("対象アセット") { objectType = typeof(ModelData) };
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as ModelData));
            root.Add(_targetField);

            _viewportContainer = new IMGUIContainer(DrawViewport);
            _viewportContainer.style.height = ViewportHeight;
            _viewportContainer.style.marginBottom = 4;
            root.Add(_viewportContainer);

            var playRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6 } };
            playRow.Add(new Button(PlayMain) { text = "▶ 配置" });
            playRow.Add(new Button(StopMain) { text = "■ 撤去" });
            root.Add(playRow);

            BuildTurntableSection(root);
            BuildEnvironmentSection(root);
            BuildMultiSlotSection(root);
            BuildMaterialSection(root);

            _animContainer = new VisualElement();
            root.Add(_animContainer);

            if (_target == null && Selection.activeObject is ModelData selected)
            {
                SetTarget(selected);
            }
            else
            {
                RefreshTargetUi();
            }
        }

        private void SetTarget(ModelData data)
        {
            StopMain();
            _target = data;
            _targetField.SetValueWithoutNotify(data);
            RefreshTargetUi();
        }

        private void RefreshTargetUi()
        {
            _serializedTarget = _target != null ? new SerializedObject(_target) : null;
            RebuildMaterialUi();
            RebuildAnimUi();
        }

        // ── Viewport(実 ModelsManager 駆動 + オービットカメラ + ターンテーブル) ──
        private void DrawViewport()
        {
            var rect = GUILayoutUtility.GetRect(100, ViewportHeight, GUILayout.ExpandWidth(true));

            if (_preview == null || !_preview.IsInitialized)
            {
                EditorGUI.HelpBox(rect, "プレビュー未初期化です", MessageType.Info);
                return;
            }

            _orbit.HandleInput(rect);
            _orbit.Apply(_preview.PreviewCamera, new Vector3(0f, 1f, 0f));

            var texture = _preview.Render((int)rect.width, (int)rect.height);
            if (texture != null)
            {
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
            }

            GUI.Label(new Rect(rect.x + 4f, rect.y + rect.height - 18f, rect.width - 8f, 16f),
                "ドラッグ: 視点回転 / ホイール: ズーム", EditorStyles.miniLabel);
        }

        private void BuildTurntableSection(VisualElement root)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };

            var toggle = new Toggle("ターンテーブル自動回転") { value = _turntableEnabled };
            toggle.RegisterValueChangedCallback(evt => _turntableEnabled = evt.newValue);
            row.Add(toggle);

            var speedSlider = new Slider("速度(度/秒)", -180f, 180f) { value = _turntableSpeedDegPerSec, style = { flexGrow = 1f } };
            speedSlider.RegisterValueChangedCallback(evt => _turntableSpeedDegPerSec = evt.newValue);
            row.Add(speedSlider);

            root.Add(row);
        }

        private void BuildEnvironmentSection(VisualElement root)
        {
            var foldout = new Foldout { text = "環境切替", value = false };

            var bgField = new ColorField("背景色") { value = new Color(0.16f, 0.16f, 0.16f) };
            bgField.RegisterValueChangedCallback(evt => _preview.SetBackgroundColor(evt.newValue));
            foldout.Add(bgField);

            var lightSlider = new Slider("ライト強度", 0f, 3f) { value = 1f };
            lightSlider.RegisterValueChangedCallback(evt => _preview.SetLightIntensity(evt.newValue));
            foldout.Add(lightSlider);

            root.Add(foldout);
        }

        // ── 複数モデル並列表示 ──
        private void BuildMultiSlotSection(VisualElement root)
        {
            var foldout = new Foldout { text = $"複数モデル並列表示(最大 {MaxParallelSlots})", value = false };

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
            if (_preview.ModelsManager.IsValid(slot.Handle))
            {
                _preview.DespawnModel(slot.Handle);
                slot.Handle = Handle<ModelMarker>.Invalid;
                slot.ToggleButton.text = "配置";
                return;
            }

            slot.Data = slot.Field.value as ModelData;
            if (slot.Data == null)
            {
                return;
            }

            var index = _slots.IndexOf(slot) + 1; // メイン(index 0 相当)の隣から並べる
            var pos = new Vector3(index * ParallelSpacingMeters, 0f, 0f);
            slot.Handle = _preview.SpawnModel(slot.Data, pos, Quaternion.identity);
            slot.ToggleButton.text = "撤去";
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
            _slotsContainer.Clear();

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

            if (_preview.ModelsManager.IsValid(_mainHandle))
            {
                _slotsContainer.Add(new Button(ApplySlotsToPreview) { text = "プレビューに反映(現状 ID 保存のみ。実適用は Phase 3)" });
            }
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
                    collected.Add(new MaterialSlot
                    {
                        RendererPath = path,
                        SlotIndex = slotIndex,
                        Material = preserved.Material,
                    });
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
            if (_target == null || _target.Slots == null || !_preview.ModelsManager.IsValid(_mainHandle))
            {
                return;
            }

            for (var i = 0; i < _target.Slots.Length; i++)
            {
                _preview.ModelsManager.SetMaterial(_mainHandle, i, _target.Slots[i].Material);
            }
        }

        // ── DefaultAnimation(情報表示。再生確認は AnimManager 実装後の Phase 3) ──
        private void RebuildAnimUi()
        {
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

            var foldout = new Foldout { text = "Default Animation", value = false };
            var field = new PropertyField(animProp);
            field.Bind(_serializedTarget);
            foldout.Add(field);
            foldout.Add(new Label("AnimManager は Phase 3 で実装されるため、ここでの再生確認は未対応です。")
            {
                style = { opacity = 0.75f, whiteSpace = WhiteSpace.Normal },
            });

            _animContainer.Add(foldout);
        }

        // ── 配置/撤去(メイン対象) ──
        private void PlayMain()
        {
            if (_target == null)
            {
                return;
            }

            StopMain();
            _mainHandle = _preview.SpawnModel(_target, Vector3.zero, Quaternion.identity);
        }

        private void StopMain()
        {
            if (_preview != null && _preview.ModelsManager != null && _preview.ModelsManager.IsValid(_mainHandle))
            {
                _preview.DespawnModel(_mainHandle);
            }

            _mainHandle = Handle<ModelMarker>.Invalid;
        }
    }
}
