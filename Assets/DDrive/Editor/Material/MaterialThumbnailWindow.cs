using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Material;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Materials
{
    // [09_editor_tools.md] §2 の Material 例外 — サムネイルだけを独立ウィンドウに出す(2026-09-11)。
    // Material Editor の「ポップアップ」ボタン / Inspector の「プレビューをポップアップ」/ メニューから開く。
    // 描くのは実 MaterialManager の共有 Material(MaterialThumbnailRenderer)。Data の変更は ObjectChangeEvents
    // (Inspector 編集・Undo・Material Editor からの自動同期をすべて含む)で検知して描き直す。
    // ウィンドウごとに Manager を持つので、Material Editor を閉じても単独で動く。
    [DataEditor(typeof(MaterialData), "プレビューをポップアップ", Order = 5)]
    public sealed class MaterialThumbnailWindow : EditorWindow
    {
        private const float TurntableSpeedDegPerSec = 45f;

        private MaterialData _target;
        private MaterialPreviewShape _shape = MaterialPreviewShape.Sphere;
        private bool _turntable;
        private float _angle;
        private float _pitch;
        private float _lightDeg;
        private bool _lockTarget;

        private AssetRegistry _registry;
        private MaterialManager _manager;
        private MaterialThumbnailRenderer _renderer;
        private bool _dirty;
        private bool _materialDirty;
        private double _lastTickTime;
        private double _lastRebuildTime;

        private ScrollView _root;
        private ObjectField _targetField;
        private EnumField _shapeField;
        private Toggle _turntableToggle;
        private Slider _lightSlider;
        private Image _image;
        private Label _status;

        [MenuItem(DDriveMenu.Editors + "Material プレビュー")]
        public static void OpenFromMenu() => Open(Selection.activeObject as MaterialData);

        public static void Open(MaterialData target) => Open(target, MaterialPreviewShape.Sphere, false, 0f);

        // Material Editor から設定ごと引き継いで開く。
        public static MaterialThumbnailWindow Open(MaterialData target, MaterialPreviewShape shape, bool turntable, float lightDeg)
        {
            var window = GetWindow<MaterialThumbnailWindow>("Material プレビュー");
            window.minSize = new Vector2(240, 240);
            window._shape = shape == MaterialPreviewShape.Model ? MaterialPreviewShape.Sphere : shape;
            window._turntable = turntable;
            window._lightDeg = lightDeg;
            window.SyncControls();
            if (target != null)
            {
                window.SetTarget(target);
            }

            return window;
        }

        private void OnEnable()
        {
            _registry = EditorAnchorRegistry.Build();
            _manager = new MaterialManager(_registry);
            _renderer = new MaterialThumbnailRenderer();
            _lastTickTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
            ObjectChangeEvents.changesPublished += OnObjectChanges;
        }

        private void OnDisable()
        {
            ObjectChangeEvents.changesPublished -= OnObjectChanges;
            EditorApplication.update -= OnEditorUpdate;
            _renderer?.Dispose();
            _renderer = null;
            _manager?.Clear();
            _manager = null;
        }

        private void OnSelectionChange()
        {
            if (!_lockTarget && Selection.activeObject is MaterialData data && data != _target)
            {
                SetTarget(data);
            }
        }

        public void CreateGUI()
        {
            _root = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            _root.contentContainer.style.flexGrow = 1;
            rootVisualElement.Add(_root);

            var toolbar = new Toolbar();
            _targetField = new ObjectField { objectType = typeof(MaterialData), allowSceneObjects = false, style = { flexGrow = 1 } };
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as MaterialData));
            toolbar.Add(_targetField);
            var lockToggle = new ToolbarToggle { text = "🔒", tooltip = "選択に追従しない" };
            lockToggle.RegisterValueChangedCallback(evt => _lockTarget = evt.newValue);
            toolbar.Add(lockToggle);
            _root.Add(toolbar);

            var controls = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 2, marginBottom = 2, flexWrap = Wrap.Wrap } };
            _shapeField = new EnumField(_shape) { style = { width = 90 } };
            _shapeField.RegisterValueChangedCallback(evt =>
            {
                var shape = (MaterialPreviewShape)evt.newValue;
                if (shape == MaterialPreviewShape.Model)
                {
                    _shapeField.SetValueWithoutNotify(_shape); // Model はシーン配置の担当
                    return;
                }

                _shape = shape;
                _dirty = true;
            });
            controls.Add(_shapeField);

            _turntableToggle = new Toggle("回転") { value = _turntable, style = { marginLeft = 6 } };
            _turntableToggle.RegisterValueChangedCallback(evt => _turntable = evt.newValue);
            controls.Add(_turntableToggle);

            _lightSlider = new Slider("ライト", 0f, 360f) { value = _lightDeg, style = { flexGrow = 1, minWidth = 160, marginLeft = 6 } };
            _lightSlider.labelElement.style.minWidth = 40;
            _lightSlider.RegisterValueChangedCallback(evt =>
            {
                _lightDeg = evt.newValue;
                _dirty = true;
            });
            controls.Add(_lightSlider);
            var invert = MaterialThumbnailRenderer.CreateInvertToggles();
            invert.style.marginLeft = 6;
            controls.Add(invert);
            _root.Add(controls);

            _image = new Image { scaleMode = ScaleMode.ScaleToFit, style = { flexGrow = 1, minHeight = 160 } };
            _image.RegisterCallback<GeometryChangedEvent>(_ => _dirty = true);
            _image.tooltip = "ドラッグで回転(横 = Y 軸、縦 = 傾き)";
            MaterialThumbnailRenderer.AttachDrag(_image, (yaw, pitch) =>
            {
                _angle = (_angle + yaw) % 360f;
                _pitch = Mathf.Clamp(_pitch + pitch, -MaterialThumbnailRenderer.MaxPitchDeg, MaterialThumbnailRenderer.MaxPitchDeg);
                _dirty = true;
            });
            _root.Add(_image);

            _status = new Label { style = { marginLeft = 4, marginBottom = 2, fontSize = 10, whiteSpace = WhiteSpace.Normal } };
            _root.Add(_status);

            SyncControls();
            if (_target != null)
            {
                SetTarget(_target);
            }
            else if (Selection.activeObject is MaterialData selected)
            {
                SetTarget(selected);
            }
        }

        private void SyncControls()
        {
            _shapeField?.SetValueWithoutNotify(_shape);
            _turntableToggle?.SetValueWithoutNotify(_turntable);
            _lightSlider?.SetValueWithoutNotify(_lightDeg);
        }

        private void SetTarget(MaterialData target)
        {
            _target = target;
            _materialDirty = true;
            _dirty = true;
            if (_targetField != null)
            {
                _targetField.SetValueWithoutNotify(target);
                titleContent = new GUIContent(target != null ? "Material: " + (string.IsNullOrEmpty(target.DisplayName) ? target.name : target.DisplayName) : "Material プレビュー");
            }
        }

        // 対象 Data 自身への変更(Inspector / Undo / 他ウィンドウ)を拾う。
        private void OnObjectChanges(ref ObjectChangeEventStream stream)
        {
            if (_target == null)
            {
                return;
            }

            var targetId = _target.GetInstanceID();
            for (var i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.ChangeAssetObjectProperties:
                        stream.GetChangeAssetObjectPropertiesEvent(i, out var assetEvent);
                        if (assetEvent.instanceId == targetId)
                        {
                            _materialDirty = true;
                        }

                        break;
                    case ObjectChangeKind.UpdatePrefabInstances:
                    case ObjectChangeKind.ChangeScene:
                        break;
                    default:
                        // アセット全体の再インポート等は保守的に描き直す
                        _materialDirty = true;
                        break;
                }
            }
        }

        private void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastTickTime), 0f, 0.25f);
            _lastTickTime = now;
            if (_manager == null || _image == null)
            {
                return;
            }

            _manager.Tick(dt);

            if (_materialDirty && now - _lastRebuildTime >= 0.1)
            {
                _materialDirty = false;
                _lastRebuildTime = now;
                _manager.Clear();
                _dirty = true;
            }

            if (_turntable)
            {
                _angle = (_angle + TurntableSpeedDegPerSec * dt) % 360f;
                _dirty = true;
            }

            if (_target != null && _target.HasAnims)
            {
                _dirty = true;
            }

            if (_dirty)
            {
                _dirty = false;
                Render();
            }
        }

        private void Render()
        {
            if (_target == null)
            {
                _image.image = null;
                _status.text = "MaterialData を選択してください";
                return;
            }

            var width = Mathf.RoundToInt(_image.resolvedStyle.width);
            var height = Mathf.RoundToInt(_image.resolvedStyle.height);
            if (float.IsNaN(_image.resolvedStyle.width) || width < 16 || float.IsNaN(_image.resolvedStyle.height) || height < 16)
            {
                return;
            }

            _renderer ??= new MaterialThumbnailRenderer();
            _image.image = _renderer.Render(_manager.GetData(_target), _shape, _angle, _pitch, _lightDeg, width, height);
            _image.MarkDirtyRepaint();
            var shaderName = _target.Shader != null ? _target.Shader.name : "(既定 Lit)";
            _status.text = $"{shaderName}  {_shape}  既定ライトのみ(実シーン照明・モデル適用は Material Editor の「シーンにプレビューを配置」で)";
        }
    }
}
