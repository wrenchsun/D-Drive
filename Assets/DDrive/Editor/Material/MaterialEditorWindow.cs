using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Material;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Materials
{
    // [06_material_texture.md] A-4 — MaterialData / TextureData の専用エディタ(3-5 の最小版。2026-09-10)。
    // 編集は SerializedObject バインド(Undo 対応)、プレビューは実 MaterialManager で生成した共有 Material を
    // 開いているシーンのプレビュー球(DontSave)に適用して SceneView で確認する(ADR-4: Editor 専用の再生経路を作らない)。
    // 球 / 板 / 任意 ModelData / Skybox 切替 / 変換前後比較は 3-9 で拡張する。
    [DDrive.Editor.Inspector.DataEditor(typeof(MaterialData), "Material Editor で開く")]
    [DDrive.Editor.Inspector.DataEditor(typeof(TextureData), "Material Editor で開く")]
    public sealed class MaterialEditorWindow : EditorWindow
    {
        public const string PreviewRootName = "[D-Drive] Material Preview";

        private UnityEngine.Object _target;
        private bool _lockTarget;
        private MaterialManager _manager;
        private AssetRegistry _registry;
        private GameObject _previewRoot;
        private Renderer _previewRenderer;
        private double _lastTickTime;

        private ScrollView _root;
        private ObjectField _targetField;
        private VisualElement _inspectorContainer;
        private Label _statusLabel;
        private Image _textureImage;
        private Label _textureInfoLabel;
        private Button _applyRuleButton;
        private TextureImportProfile.Rule _matchedRule;
        private bool _hasMatchedRule;

        [MenuItem(DDriveMenu.Editors + "Material")]
        public static void OpenFromMenu() => Open(Selection.activeObject as MaterialData);

        public static void Open(MaterialData target) => OpenWith(target);

        public static void Open(TextureData target) => OpenWith(target);

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
            _previewRoot = null;
            _previewRenderer = null;
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

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, marginBottom = 4 } };
            buttons.Add(new Button(PlacePreview) { text = "シーンにプレビュー球を配置", tooltip = "開いているシーンに球(保存されない)を置き、生成した共有 Material を適用する" });
            buttons.Add(new Button(RebuildPreview) { text = "再生成", tooltip = "Data の変更を共有 Material に反映し直す" });
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
                _statusLabel.text = _previewRenderer != null ? "プレビュー球に適用中" : "「シーンにプレビュー球を配置」で確認できます";
                if (_previewRenderer != null)
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

        private void PlacePreview()
        {
            if (_target is not MaterialData data)
            {
                _statusLabel.text = "MaterialData を選択してください";
                return;
            }

            if (_previewRoot == null)
            {
                _previewRoot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _previewRoot.name = PreviewRootName;
                _previewRoot.hideFlags = HideFlags.DontSave;
                StageUtility.PlaceGameObjectInCurrentStage(_previewRoot);
                var pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
                _previewRoot.transform.position = pivot;
                _previewRenderer = _previewRoot.GetComponent<Renderer>();
            }

            EditorAnchorRegistry.Refresh(_registry);
            _manager.ApplyData(_previewRenderer, 0, data);
            _statusLabel.text = "プレビュー球に適用中";
            SceneView.RepaintAll();
        }

        private void RebuildPreview()
        {
            if (_target is not MaterialData data)
            {
                return;
            }

            _manager.Clear();
            EditorAnchorRegistry.Refresh(_registry);
            if (_previewRenderer != null)
            {
                _manager.ApplyData(_previewRenderer, 0, data);
                SceneView.RepaintAll();
            }
        }

        private void RemovePreview()
        {
            if (_previewRoot != null)
            {
                DestroyImmediate(_previewRoot);
            }

            _previewRoot = null;
            _previewRenderer = null;
            if (_statusLabel != null && _target is MaterialData)
            {
                _statusLabel.text = "「シーンにプレビュー球を配置」で確認できます";
            }

            SceneView.RepaintAll();
        }

        // MaterialAnim(UV スクロール等)を EditMode でも動かす。
        private void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastTickTime), 0f, 0.25f);
            _lastTickTime = now;
            if (_manager == null || _previewRenderer == null)
            {
                return;
            }

            _manager.Tick(dt);
            if (_target is MaterialData data && data.HasAnims)
            {
                SceneView.RepaintAll();
            }
        }
    }
}
