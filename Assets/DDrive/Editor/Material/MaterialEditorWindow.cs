using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Materials
{
    // [06_material_texture.md] A-4 — MaterialData / TextureData の専用エディタ(3-5 の最小版 → 3-9 で拡張。2026-09-10)。
    // 編集は SerializedObject バインド(Undo 対応)、プレビューは実 MaterialManager で生成した共有 Material を
    // 開いているシーンのプレビュー形状(DontSave)に適用して SceneView で確認する(ADR-4: Editor 専用の再生経路を作らない)。
    // 3-9: 球 / 板(Quad) / Cube / 任意 ModelData の切替、ターンテーブル、ライト回転、変換前後の並列比較を追加。
    // 生成ロジック自体は MaterialPreviewBuilder に分離してテストできるようにしてある。
    [DDrive.Editor.Inspector.DataEditor(typeof(MaterialData), "Material Editor で開く")]
    [DDrive.Editor.Inspector.DataEditor(typeof(TextureData), "Material Editor で開く")]
    public sealed class MaterialEditorWindow : EditorWindow
    {
        public const string PreviewRootName = "[D-Drive] Material Preview";
        private const float CompareOffsetX = 1.5f;

        private UnityEngine.Object _target;
        private bool _lockTarget;
        private MaterialManager _manager;
        private AssetRegistry _registry;
        private PoolService _pool;
        private ModelsManager _modelsManager;

        private GameObject _previewRoot;
        private MaterialPreviewShape _shape = MaterialPreviewShape.Sphere;
        private ModelData _previewModel;
        private MaterialData _compareTarget;
        private MaterialPreviewBuilder.Preview _primaryPreview;
        private MaterialPreviewBuilder.Preview _comparePreview;

        private bool _turntableEnabled;
        private readonly float _turntableSpeedDegPerSec = 45f;
        private double _lastTickTime;

        private Light _rotatedLight;
        private Quaternion _originalLightRotation;

        private ScrollView _root;
        private ObjectField _targetField;
        private VisualElement _inspectorContainer;
        private Label _statusLabel;
        private Image _textureImage;
        private Label _textureInfoLabel;
        private Button _applyRuleButton;
        private TextureImportProfile.Rule _matchedRule;
        private bool _hasMatchedRule;

        private EnumField _shapeField;
        private ObjectField _modelField;
        private Toggle _turntableToggle;
        private Slider _lightRotationSlider;
        private ObjectField _compareField;

        [MenuItem(DDriveMenu.Editors + "Material")]
        public static void OpenFromMenu() => Open(Selection.activeObject as MaterialData);

        public static void Open(MaterialData target) => OpenWith(target);

        public static void Open(TextureData target) => OpenWith(target);

        // MaterialConvertWindow の「Material Editor で比較」から呼ぶ(a=変換元、b=変換で新規作成された Data。無ければ null)。
        public static void OpenCompare(MaterialData a, MaterialData b)
        {
            var window = GetWindow<MaterialEditorWindow>("Material Editor");
            window.minSize = new Vector2(420, 360);
            if (a != null)
            {
                window.SetTarget(a);
            }

            window._compareTarget = b;
            window._compareField?.SetValueWithoutNotify(b);
            if (a != null)
            {
                window.PlacePreview();
            }
        }

        private static void OpenWith(UnityEngine.Object target)
        {
            var window = GetWindow<MaterialEditorWindow>("Material Editor");
            window.minSize = new Vector2(420, 360);
            if (target != null)
            {
                window.SetTarget(target);
            }
        }

        private void OnEnable()
        {
            _registry = EditorAnchorRegistry.Build();
            _manager = new MaterialManager(_registry);
            _pool = new PoolService();
            _modelsManager = new ModelsManager(_pool, _registry, null, _manager);
            _lastTickTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
        }

        private void OnDisable()
        {
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            EditorApplication.update -= OnEditorUpdate;
            RemovePreview();
            _manager?.Clear();
            _manager = null;
            _modelsManager = null;
            _pool = null;
        }

        private void OnSelectionChange()
        {
            if (!_lockTarget && (Selection.activeObject is MaterialData || Selection.activeObject is TextureData) && Selection.activeObject != _target)
            {
                SetTarget(Selection.activeObject);
            }
        }

        private void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene previous, UnityEngine.SceneManagement.Scene current)
        {
            // シーンが切り替わると DontSave の配置物は Unity 側で既に失われているので、参照だけ捨てる。
            _previewRoot = null;
            _primaryPreview = null;
            _comparePreview = null;
            _rotatedLight = null;
        }

        public void CreateGUI()
        {
            _root = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            rootVisualElement.Add(_root);

            var toolbar = new Toolbar();
            _targetField = new ObjectField("対象") { objectType = typeof(UnityEngine.Object), allowSceneObjects = false, style = { flexGrow = 1 } };
            _targetField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is MaterialData || evt.newValue is TextureData)
                {
                    SetTarget(evt.newValue);
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
            _root.Add(toolbar);

            BuildPreviewControls(_root);

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, marginBottom = 4 } };
            buttons.Add(new Button(PlacePreview) { text = "シーンにプレビューを配置", tooltip = "開いているシーンに選んだ形状(保存されない)を置き、生成した共有 Material を適用する" });
            buttons.Add(new Button(RebuildPreview) { text = "再生成", tooltip = "Data の変更を共有 Material に反映し直す(比較用も含む)" });
            buttons.Add(new Button(RemovePreview) { text = "撤去" });
            _root.Add(buttons);

            _statusLabel = new Label { style = { marginLeft = 4, marginBottom = 4 } };
            _root.Add(_statusLabel);

            _textureImage = new Image { scaleMode = ScaleMode.ScaleToFit, style = { height = 160, marginBottom = 4 } };
            _textureImage.style.display = DisplayStyle.None;
            _root.Add(_textureImage);

            _textureInfoLabel = new Label { style = { marginLeft = 4, marginBottom = 4, whiteSpace = WhiteSpace.Normal } };
            _textureInfoLabel.style.display = DisplayStyle.None;
            _root.Add(_textureInfoLabel);

            _applyRuleButton = new Button(ApplyMatchedRule) { text = "命名規約を適用して再インポート" };
            _applyRuleButton.style.display = DisplayStyle.None;
            _root.Add(_applyRuleButton);

            _inspectorContainer = new VisualElement();
            _root.Add(_inspectorContainer);

            if (_target != null)
            {
                SetTarget(_target);
            }
            else if (Selection.activeObject is MaterialData || Selection.activeObject is TextureData)
            {
                SetTarget(Selection.activeObject);
            }
        }

        // 形状 / ターンテーブル / ライト回転 / 比較対象(3-9)。
        private void BuildPreviewControls(VisualElement root)
        {
            var foldout = new Foldout { text = "プレビュー", value = true };

            _shapeField = new EnumField("形状", _shape) { tooltip = "球 / 板(Quad) / Cube / 任意 ModelData" };
            _shapeField.RegisterValueChangedCallback(evt =>
            {
                _shape = (MaterialPreviewShape)evt.newValue;
                _modelField.style.display = _shape == MaterialPreviewShape.Model ? DisplayStyle.Flex : DisplayStyle.None;
            });
            foldout.Add(_shapeField);

            _modelField = new ObjectField("プレビュー用モデル") { objectType = typeof(ModelData), allowSceneObjects = false };
            _modelField.RegisterValueChangedCallback(evt => _previewModel = evt.newValue as ModelData);
            _modelField.style.display = DisplayStyle.None;
            foldout.Add(_modelField);

            var turntableRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _turntableToggle = new Toggle("ターンテーブル") { tooltip = "配置したプレビューを Y 軸で回す" };
            _turntableToggle.RegisterValueChangedCallback(evt => _turntableEnabled = evt.newValue);
            turntableRow.Add(_turntableToggle);
            foldout.Add(turntableRow);

            _lightRotationSlider = new Slider("ライト回転", 0f, 360f) { tooltip = "シーンの最初の Directional Light を Y 軸で回す(無ければ何もしない)", style = { flexGrow = 1 } };
            _lightRotationSlider.RegisterValueChangedCallback(evt =>
            {
                ApplyLightRotation(evt.newValue);
                SceneView.RepaintAll();
            });
            foldout.Add(_lightRotationSlider);

            var compareRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _compareField = new ObjectField("比較対象") { objectType = typeof(MaterialData), allowSceneObjects = false, style = { flexGrow = 1 } };
            _compareField.RegisterValueChangedCallback(evt => _compareTarget = evt.newValue as MaterialData);
            compareRow.Add(_compareField);
            compareRow.Add(new Button(CompareSideBySide) { text = "並べて比較", tooltip = "対象の隣(X+1.5)に比較対象の Material を適用したプレビューを並べる" });
            foldout.Add(compareRow);

            root.Add(foldout);
        }

        private void SetTarget(UnityEngine.Object target)
        {
            _target = target;
            if (_root == null)
            {
                return;
            }

            _targetField.SetValueWithoutNotify(target);
            _inspectorContainer.Clear();
            _textureImage.style.display = DisplayStyle.None;
            if (target == null)
            {
                _statusLabel.text = "MaterialData か TextureData を選択してください";
                return;
            }

            var so = new SerializedObject(target);
            _inspectorContainer.Add(new InspectorElement(so));
            _inspectorContainer.Bind(so);

            if (target is TextureData tex)
            {
                _textureImage.image = tex.Texture;
                _textureImage.style.display = tex.Texture != null ? DisplayStyle.Flex : DisplayStyle.None;
                _statusLabel.text = tex.Texture != null ? $"{tex.Texture.width}×{tex.Texture.height}  Channel={tex.Channel}" : "Texture が未設定です";
                UpdateTextureInfo(tex);
            }
            else
            {
                _textureInfoLabel.style.display = DisplayStyle.None;
                _applyRuleButton.style.display = DisplayStyle.None;
                _statusLabel.text = _primaryPreview != null ? "プレビューに適用中" : "「シーンにプレビューを配置」で確認できます";
                if (_primaryPreview != null)
                {
                    RebuildPreview();
                }
            }
        }

        // [06] B-3/B-4 — Importer の現状と命名規約(TextureImportProfile)への適合を表示する(3-8)。
        private void UpdateTextureInfo(TextureData tex)
        {
            _hasMatchedRule = false;
            if (tex.Texture == null)
            {
                _textureInfoLabel.style.display = DisplayStyle.None;
                _applyRuleButton.style.display = DisplayStyle.None;
                return;
            }

            var path = AssetDatabase.GetAssetPath(tex.Texture);
            var importer = string.IsNullOrEmpty(path) ? null : AssetImporter.GetAtPath(path) as TextureImporter;

            _textureInfoLabel.style.display = DisplayStyle.Flex;
            if (importer == null)
            {
                _textureInfoLabel.text = "Importer 情報なし(メモリ上のテクスチャ)";
                _applyRuleButton.style.display = DisplayStyle.None;
                return;
            }

            var profile = TextureImportProfile.FindOrDefault();
            string ruleText;
            _applyRuleButton.style.display = DisplayStyle.None;
            if (profile.AppliesTo(path) && profile.TryMatch(path, out var rule))
            {
                _matchedRule = rule;
                _hasMatchedRule = true;
                ruleText = $"規約: {rule.Name}";
                if (TextureImportProfile.Diff(importer, rule).Count > 0)
                {
                    _applyRuleButton.style.display = DisplayStyle.Flex;
                }
            }
            else
            {
                ruleText = "規約に該当なし";
            }

            _textureInfoLabel.text =
                $"Texture Type: {importer.textureType}  sRGB: {importer.sRGBTexture}  Mipmap: {importer.mipmapEnabled}  " +
                $"圧縮: {importer.textureCompression}  Max Size: {importer.maxTextureSize}\n{ruleText}";
        }

        private void ApplyMatchedRule()
        {
            if (_target is not TextureData tex || tex.Texture == null || !_hasMatchedRule)
            {
                return;
            }

            var path = AssetDatabase.GetAssetPath(tex.Texture);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                return;
            }

            if (TextureImportProfile.Apply(importer, _matchedRule))
            {
                importer.SaveAndReimport();
            }

            UpdateTextureInfo(tex);
            _statusLabel.text = $"命名規約 '{_matchedRule.Name}' を適用しました";
        }

        // ── プレビュー配置(3-9: 球/板/Cube/Model + 比較対象) ──

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

        private void PlacePreview()
        {
            if (_target is not MaterialData data)
            {
                _statusLabel.text = "MaterialData を選択してください";
                return;
            }

            if (_shape == MaterialPreviewShape.Model && _previewModel == null)
            {
                _statusLabel.text = "プレビュー用の ModelData を選択してください";
                return;
            }

            _primaryPreview?.Dispose();
            _comparePreview?.Dispose();
            EnsurePreviewRoot();
            EditorAnchorRegistry.Refresh(_registry);

            _primaryPreview = MaterialPreviewBuilder.Create(_shape, _previewModel, data, _manager, _previewRoot.transform, Vector3.zero, _modelsManager, "Primary");
            if (_compareTarget != null)
            {
                _comparePreview = MaterialPreviewBuilder.Create(_shape, _previewModel, _compareTarget, _manager, _previewRoot.transform,
                    new Vector3(CompareOffsetX, 0f, 0f), _modelsManager, "Compare");
            }

            _statusLabel.text = _primaryPreview != null
                ? (_comparePreview != null ? "プレビューに適用中(比較対象あり)" : "プレビューに適用中")
                : "プレビューの生成に失敗しました(ModelData の Prefab を確認してください)";
            SceneView.RepaintAll();
        }

        private void CompareSideBySide()
        {
            if (_compareTarget == null)
            {
                _statusLabel.text = "比較対象の MaterialData を選択してください";
                return;
            }

            PlacePreview();
        }

        private void RebuildPreview()
        {
            if (_target is not MaterialData data)
            {
                return;
            }

            _manager.Clear();
            EditorAnchorRegistry.Refresh(_registry);
            if (_primaryPreview != null)
            {
                MaterialPreviewBuilder.Apply(_primaryPreview, _shape, _previewModel, data, _manager);
            }

            if (_comparePreview != null && _compareTarget != null)
            {
                MaterialPreviewBuilder.Apply(_comparePreview, _shape, _previewModel, _compareTarget, _manager);
            }

            SceneView.RepaintAll();
        }

        private void RemovePreview()
        {
            _primaryPreview?.Dispose();
            _primaryPreview = null;
            _comparePreview?.Dispose();
            _comparePreview = null;

            if (_previewRoot != null)
            {
                DestroyImmediate(_previewRoot);
            }

            _previewRoot = null;
            RestoreLightRotation();

            if (_statusLabel != null && _target is MaterialData)
            {
                _statusLabel.text = "「シーンにプレビューを配置」で確認できます";
            }

            SceneView.RepaintAll();
        }

        // ── ライト回転(3-9) ──

        private void FindDirectionalLight()
        {
            if (_rotatedLight != null)
            {
                return;
            }

            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional)
                {
                    _rotatedLight = light;
                    _originalLightRotation = light.transform.rotation;
                    return;
                }
            }
        }

        private void ApplyLightRotation(float degreesY)
        {
            FindDirectionalLight();
            if (_rotatedLight == null)
            {
                return;
            }

            var euler = _originalLightRotation.eulerAngles;
            _rotatedLight.transform.rotation = Quaternion.Euler(euler.x, degreesY, euler.z);
        }

        private void RestoreLightRotation()
        {
            if (_rotatedLight != null)
            {
                _rotatedLight.transform.rotation = _originalLightRotation;
            }

            _rotatedLight = null;
            _lightRotationSlider?.SetValueWithoutNotify(0f);
        }

        // MaterialAnim(UV スクロール等)/ ターンテーブルを EditMode でも動かす。
        private void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastTickTime), 0f, 0.25f);
            _lastTickTime = now;
            if (_manager == null)
            {
                return;
            }

            _manager.Tick(dt);

            if (_turntableEnabled)
            {
                if (_primaryPreview?.Root != null)
                {
                    _primaryPreview.Root.transform.Rotate(Vector3.up, _turntableSpeedDegPerSec * dt, Space.World);
                }

                if (_comparePreview?.Root != null)
                {
                    _comparePreview.Root.transform.Rotate(Vector3.up, _turntableSpeedDegPerSec * dt, Space.World);
                }

                SceneView.RepaintAll();
            }

            if (_primaryPreview != null && _target is MaterialData data && data.HasAnims)
            {
                SceneView.RepaintAll();
            }
        }
    }
}
