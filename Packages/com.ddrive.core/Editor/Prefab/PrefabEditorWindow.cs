using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Prefab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using DDrive.Editor.Validation;

namespace DDrive.Editor.PrefabTool
{
    // [07_canvas_prefab.md] Part B の最小エディタ(4-4)。編集は SerializedObject バインド(Undo 対応)、
    // プレビューは実 PrefabsManager で Spawn した実体を開いているシーン / プレハブステージの DontSave
    // ルートに置いて SceneView で確認する(ADR-4: Editor 専用の再生経路を作らない。MaterialEditorWindow と同じ設計)。
    [DDrive.Editor.Inspector.DataEditor(typeof(PrefabData), "Prefab Editor で開く")]
    public sealed class PrefabEditorWindow : EditorWindow
    {
        public const string PreviewRootName = "[D-Drive] Prefab Preview";

        private PrefabData _target;
        private bool _lockTarget;
        private PrefabsManager _manager;
        private AssetRegistry _registry;
        private PoolService _pool;

        private GameObject _previewRoot;
        private Handle<PrefabMarker> _previewHandle = Handle<PrefabMarker>.Invalid;

        private ScrollView _root;
        private ObjectField _targetField;
        private DataValidationSection _validationSection; // 2026-09-17 U-13([09] §11)
        private VisualElement _inspectorContainer;
        private Label _statusLabel;

        [MenuItem(DDriveMenu.Editors + "Prefab")]
        public static void OpenFromMenu() => Open(Selection.activeObject as PrefabData);

        public static void Open(PrefabData target)
        {
            var window = GetWindow<PrefabEditorWindow>("Prefab Editor");
            window.minSize = new Vector2(380, 300);
            if (target != null)
            {
                window.SetTarget(target);
            }
        }

        private void OnEnable()
        {
            _registry = EditorAnchorRegistry.Build();
            _pool = new PoolService();
            _manager = new PrefabsManager(_pool, _registry);
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
        }

        private void OnDisable()
        {
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            RemovePreview();
            _manager = null;
            _pool = null;
            _registry = null;
        }

        private void OnSelectionChange()
        {
            if (!_lockTarget && Selection.activeObject is PrefabData data && data != _target)
            {
                SetTarget(data);
            }
        }

        private void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene previous, UnityEngine.SceneManagement.Scene current)
        {
            // シーンが切り替わると DontSave の配置物は Unity 側で既に失われているので、参照だけ捨てる。
            _previewRoot = null;
            _previewHandle = Handle<PrefabMarker>.Invalid;
        }

        public void CreateGUI()
        {
            _root = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            rootVisualElement.Add(_root);

            var toolbar = new Toolbar();
            _targetField = new ObjectField("対象") { objectType = typeof(PrefabData), allowSceneObjects = false, style = { flexGrow = 1 } };
            _targetField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is PrefabData data)
                {
                    SetTarget(data);
                }
                else if (evt.newValue != null)
                {
                    _targetField.SetValueWithoutNotify(_target);
                }
            });
            toolbar.Add(_targetField);
            var lockToggle = new ToolbarToggle { text = "🔒", tooltip = "選択に追従しない" };
            lockToggle.RegisterValueChangedCallback(evt => _lockTarget = evt.newValue);
            toolbar.Add(lockToggle);
            toolbar.Add(DDrive.Editor.Inspector.NewAssetToolbarButton.CreateToolbarButton(typeof(PrefabEditorWindow)));
            _root.Add(toolbar);

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 4, marginBottom = 4 } }; // [09] §7.1
            buttons.Add(PreviewPlacementButton.Create(
                "確認用シーンに配置",
                "確認用シーン(PreviewScene)を開き、実 PrefabsManager で Spawn して SceneView をそこへ向ける",
                PlacePreview));
            buttons.Add(new Button(RemovePreview) { text = "撤去" });
            _root.Add(buttons);

            _statusLabel = new Label { style = { marginLeft = 4, marginBottom = 4 } };
            _root.Add(_statusLabel);

            _inspectorContainer = new VisualElement();
            _root.Add(_inspectorContainer);

            // 2026-09-17(U-13): 「検証」を全エディタで揃える([09] §11)。
            _validationSection = new DataValidationSection();
            _root.Add(_validationSection);

            if (_target != null)
            {
                SetTarget(_target);
            }
            else if (Selection.activeObject is PrefabData data)
            {
                SetTarget(data);
            }
        }

        private void SetTarget(PrefabData target)
        {
            _target = target;
            if (_root == null)
            {
                return;
            }

            _targetField.SetValueWithoutNotify(target);
            _inspectorContainer.Clear();
            _validationSection?.Bind(target);
            if (target == null)
            {
                _statusLabel.text = "PrefabData を選択してください";
                return;
            }

            var so = new SerializedObject(target);
            _inspectorContainer.Add(new InspectorElement(so));
            _inspectorContainer.Bind(so);

            _statusLabel.text = _manager != null && _manager.IsValid(_previewHandle) ? "プレビュー配置中" : "「確認用シーンに配置」で確認できます";
        }

        private void EnsurePreviewRoot()
        {
            if (_previewRoot != null)
            {
                return;
            }

            _previewRoot = new GameObject(PreviewRootName) { hideFlags = HideFlags.DontSave };
            StageUtility.PlaceGameObjectInCurrentStage(_previewRoot);
            var pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            _previewRoot.transform.position = pivot;
            _pool.SetInstanceParent(_previewRoot.transform);
        }

        // U-4(2026-09-17): 「確認用シーンに配置」が確認用シーンを開かず、今開いているシーンにしか置いていなかった。
        // 他のエディタ(Model / Anim / Anim2D)と同じ「片付ける → 確認用シーンを開く → 配置」の順に揃える。
        // U-5: 右クリックの「このシーンに配置 / 本配置」は mode で分岐する(PreviewPlacement に集約)。
        private void PlacePreview(PreviewPlaceMode mode)
        {
            if (_target == null)
            {
                _statusLabel.text = "PrefabData を選択してください";
                return;
            }

            RemovePreview();
            if (!PreviewPlacement.PrepareScene(mode, VfxPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            if (PreviewPlacement.IsPersistent(mode))
            {
                // 本配置は Pool / Manager が追跡しない実体にする(後で Despawn されないように)。
                var pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
                var placed = PreviewPlacement.PlacePrefabPersistent(_target.Prefab, pivot, Quaternion.identity);
                _statusLabel.text = placed != null ? "このシーンに本配置しました(シーンを保存すると残ります)" : "本配置に失敗しました";
                return;
            }

            EnsurePreviewRoot();
            EditorAnchorRegistry.Refresh(_registry);

            _previewHandle = _manager.SpawnData(_target, _previewRoot.transform.position, Quaternion.identity);
            var placedOk = _manager.IsValid(_previewHandle);
            _statusLabel.text = placedOk ? "プレビュー配置中" : "配置に失敗しました";
            if (placedOk)
            {
                PreviewPlacement.Focus(_manager.GetGameObject(_previewHandle) ?? _previewRoot);
            }

            SceneView.RepaintAll();
        }

        private void RemovePreview()
        {
            if (_manager != null && _manager.IsValid(_previewHandle))
            {
                _manager.Despawn(_previewHandle);
            }

            _previewHandle = Handle<PrefabMarker>.Invalid;

            if (_previewRoot != null)
            {
                DestroyImmediate(_previewRoot);
            }

            _previewRoot = null;

            if (_statusLabel != null && _target != null)
            {
                _statusLabel.text = "「確認用シーンに配置」で確認できます";
            }

            SceneView.RepaintAll();
        }
    }
}
