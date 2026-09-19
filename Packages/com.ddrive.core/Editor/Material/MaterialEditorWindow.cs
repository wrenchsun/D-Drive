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
using DDrive.Editor.Validation;

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

        // ドメインリロード(スクリプト再コンパイル)を跨いで残すウィンドウ状態は [SerializeField] を付ける
        // (AnimEditorWindow と同じ方式。2026-09-11 レビュー対応)。VisualElement / PreviewRenderUtility 等は対象外。
        [SerializeField] private UnityEngine.Object _target;
        [SerializeField] private bool _lockTarget;
        private MaterialManager _manager;
        private AssetRegistry _registry;
        private PoolService _pool;
        private ModelsManager _modelsManager;

        private GameObject _previewRoot;
        [SerializeField] private MaterialPreviewShape _shape = MaterialPreviewShape.Sphere;
        [SerializeField] private ModelData _previewModel;
        [SerializeField] private MaterialData _compareTarget;
        private MaterialPreviewBuilder.Preview _primaryPreview;
        private MaterialPreviewBuilder.Preview _comparePreview;

        [SerializeField] private bool _turntableEnabled;
        private readonly float _turntableSpeedDegPerSec = 45f;
        private double _lastTickTime;

        private Light _rotatedLight;
        private Quaternion _originalLightRotation;

        private ScrollView _root;
        private ObjectField _targetField;
        private DataValidationSection _validationSection; // 2026-09-17 U-13([09] §11)
        private VisualElement _inspectorContainer;
        private VisualElement _inspectorHost; // 対象ごとに作り直す(バインド + 変更追跡の持ち主)
        private Label _statusLabel;
        private Image _textureImage;
        private Label _textureInfoLabel;
        private Button _applyRuleButton;
        private TextureImportProfile.Rule _matchedRule;
        private bool _hasMatchedRule;

        private VisualElement _specificRow;
        private Label _specificLabel;
        private Button _removeConflictsButton;
        private Shader _trackedShader;

        // Data の編集(Inspector バインド / Undo)を検知して共有 Material を自動で作り直す(2026-09-11)。
        // ドラッグ中に毎フレーム作り直さないよう OnEditorUpdate 側で間引く。
        private bool _previewDirty;
        private double _lastAutoRebuildTime;
        private const double AutoRebuildIntervalSec = 0.1;

        // Registry(TextureData 等の ID 解決)の再走査が必要か。OnEnable の Build 直後は最新。projectChanged で立てる。
        private bool _registryDirty;

        // ウィンドウ内サムネイル([09] §2 の Material 例外、2026-09-11)。実 Manager の共有 Material を PreviewRenderUtility で描く。
        private MaterialThumbnailRenderer _thumbnail;
        private Image _thumbnailImage;
        private Label _thumbnailLabel;
        private VisualElement _thumbnailRow;
        private bool _thumbnailDirty;
        private double _lastThumbnailTime;
        private const double ThumbnailIntervalSec = 1.0 / 30.0; // 描き直しの最短間隔(2026-09-11 レビュー対応)

        // サムネイル比較(2026-09-11): 比較対象があるとき「左右」(2 分割)か「切替」(1 枚を A/B で切替)で見る。
        private enum ThumbnailCompareMode { SideBySide, Toggle }
        [SerializeField] private ThumbnailCompareMode _compareMode = ThumbnailCompareMode.SideBySide;
        [SerializeField] private bool _showCompareInToggle; // 切替モードで B(比較対象)を表示中
        private VisualElement _thumbnailArea;
        private VisualElement _compareColumn;
        private Image _compareThumbnailImage;
        private Label _thumbnailCaptionA;
        private Label _thumbnailCaptionB;
        private MaterialThumbnailRenderer _compareThumbnail;
        private VisualElement _compareControls;
        private Button _compareModeButton;
        private Button _abButton;
        [SerializeField] private float _thumbnailAngle;
        [SerializeField] private float _thumbnailPitch;
        [SerializeField] private float _thumbnailLightDeg;
        private const int ThumbnailHeight = 220;

        private EnumField _shapeField;
        private ObjectField _modelField;
        private Toggle _turntableToggle;
        private Slider _lightRotationSlider;
        private ObjectField _compareField;

        [MenuItem(DDriveMenu.Editors + "Material")]
        public static void OpenFromMenu() => Open(Selection.activeObject as MaterialData);

        public static void Open(MaterialData target) => OpenWith(target);

        public static void Open(TextureData target) => OpenWith(target);

        // OpenCompare(MaterialConvertWindow の「Material Editor で比較」用)は、変換ウィンドウ内に見た目比較が入って
        // ボタンが廃止されたあと呼び出し元が無く、CreateGUI 前に呼ぶと NRE になるため削除した(2026-09-11 レビュー対応)。
        // 比較は Material Editor の「比較対象」フィールドか、変換ウィンドウ内の A/B 表示で行う。

        // 両サムネイル共通の Image(ドラッグ回転は共有の角度を動かす)。
        private Image CreateThumbnailImage()
        {
            var image = new Image { scaleMode = ScaleMode.ScaleToFit, style = { height = ThumbnailHeight } };
            image.RegisterCallback<GeometryChangedEvent>(_ => _thumbnailDirty = true);
            image.tooltip = "ドラッグで回転(横 = Y 軸、縦 = 傾き)";
            MaterialThumbnailRenderer.AttachDrag(image, (yaw, pitch) =>
            {
                _thumbnailAngle = (_thumbnailAngle + yaw) % 360f;
                _thumbnailPitch = Mathf.Clamp(_thumbnailPitch + pitch, -MaterialThumbnailRenderer.MaxPitchDeg, MaterialThumbnailRenderer.MaxPitchDeg);
                _thumbnailDirty = true;
            });
            return image;
        }

        private void ToggleCompareMode()
        {
            _compareMode = _compareMode == ThumbnailCompareMode.SideBySide ? ThumbnailCompareMode.Toggle : ThumbnailCompareMode.SideBySide;
            UpdateCompareUi();
        }

        private void ToggleAB()
        {
            _showCompareInToggle = !_showCompareInToggle;
            UpdateCompareUi();
        }

        // 比較対象の有無とモードに合わせて 2 列目・ボタンの表示を切り替え、描き直す。
        private void UpdateCompareUi()
        {
            if (_compareControls == null)
            {
                return;
            }

            var hasCompare = _compareTarget != null && _target is MaterialData;
            _compareControls.style.display = hasCompare ? DisplayStyle.Flex : DisplayStyle.None;
            _compareColumn.style.display = hasCompare && _compareMode == ThumbnailCompareMode.SideBySide ? DisplayStyle.Flex : DisplayStyle.None;
            _compareModeButton.text = _compareMode == ThumbnailCompareMode.SideBySide ? "左右" : "切替";
            _abButton.style.display = hasCompare && _compareMode == ThumbnailCompareMode.Toggle ? DisplayStyle.Flex : DisplayStyle.None;
            _abButton.text = _showCompareInToggle ? "B → A" : "A → B";
            _thumbnailDirty = true;
        }

        private static string DisplayNameOf(MaterialData data)
            => data == null ? "" : string.IsNullOrEmpty(data.DisplayName) ? data.name : data.DisplayName;

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
            _registryDirty = false;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.projectChanged += OnProjectChanged;
            ObjectChangeEvents.changesPublished += OnObjectChanges;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
        }

        private void OnDisable()
        {
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            ObjectChangeEvents.changesPublished -= OnObjectChanges;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.update -= OnEditorUpdate;
            RemovePreview();
            _thumbnail?.Dispose();
            _thumbnail = null;
            _compareThumbnail?.Dispose();
            _compareThumbnail = null;
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

        private void OnProjectChanged() => _registryDirty = true;

        // アセットの追加・削除後(例: Maya インポートで TextureData が増えた)に Registry を再走査し、古い ID 解決で作った
        // 共有 Material を捨てる。描画・再生成の直前に必ず通す(2026-09-11: サムネイル経路が通っておらずテクスチャ無しで描いていた)。
        private bool EnsureRegistryFresh()
        {
            if (!_registryDirty)
            {
                return false;
            }

            _registryDirty = false;
            EditorAnchorRegistry.Refresh(_registry);
            _manager.Clear();
            return true;
        }

        // 比較対象(B)は SerializedObject でバインドしていないので、その変更(Inspector / Undo)はここで拾う。
        private void OnObjectChanges(ref ObjectChangeEventStream stream)
        {
            if (_compareTarget == null)
            {
                return;
            }

            var compareId = _compareTarget.GetInstanceID();
            for (var i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    continue;
                }

                stream.GetChangeAssetObjectPropertiesEvent(i, out var evt);
                if (evt.instanceId == compareId)
                {
                    _previewDirty = true;
                    return;
                }
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
            lockToggle.SetValueWithoutNotify(_lockTarget); // ドメインリロード後の復元
            lockToggle.RegisterValueChangedCallback(evt => _lockTarget = evt.newValue);
            toolbar.Add(lockToggle);
            toolbar.Add(DDrive.Editor.Inspector.NewAssetToolbarButton.CreateToolbarButton(typeof(MaterialEditorWindow)));
            _root.Add(toolbar);

            // サムネイル(Material のみ。Model 形状・TextureData 選択時は非表示)。比較対象があれば左右 2 分割 or 切替。
            _thumbnailArea = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, marginBottom = 2 } };
            _thumbnailArea.style.display = DisplayStyle.None;

            var columnA = new VisualElement { style = { flexGrow = 1, flexBasis = 0, flexShrink = 1 } };
            _thumbnailImage = CreateThumbnailImage();
            columnA.Add(_thumbnailImage);
            _thumbnailCaptionA = new Label { style = { fontSize = 10, unityTextAlign = TextAnchor.MiddleCenter } };
            columnA.Add(_thumbnailCaptionA);
            _thumbnailArea.Add(columnA);

            _compareColumn = new VisualElement { style = { flexGrow = 1, flexBasis = 0, flexShrink = 1, marginLeft = 4 } };
            _compareThumbnailImage = CreateThumbnailImage();
            _compareColumn.Add(_compareThumbnailImage);
            _thumbnailCaptionB = new Label { style = { fontSize = 10, unityTextAlign = TextAnchor.MiddleCenter } };
            _compareColumn.Add(_thumbnailCaptionB);
            _compareColumn.style.display = DisplayStyle.None;
            _thumbnailArea.Add(_compareColumn);
            _root.Add(_thumbnailArea);

            var thumbnailRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4, flexWrap = Wrap.Wrap } };
            _thumbnailLabel = new Label { style = { marginLeft = 4, whiteSpace = WhiteSpace.Normal, fontSize = 10, flexGrow = 1, flexShrink = 1 } };
            thumbnailRow.Add(_thumbnailLabel);

            _compareControls = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexShrink = 0, marginRight = 6 } };
            _compareModeButton = new Button(ToggleCompareMode) { tooltip = "比較の見せ方: 左右(2 分割) ⇄ 切替(1 枚を A/B で切替)" };
            _compareControls.Add(_compareModeButton);
            _abButton = new Button(ToggleAB) { tooltip = "表示する方を切り替える(A = 対象、B = 比較対象)" };
            _compareControls.Add(_abButton);
            _compareControls.style.display = DisplayStyle.None;
            thumbnailRow.Add(_compareControls);

            thumbnailRow.Add(MaterialThumbnailRenderer.CreateInvertToggles());
            thumbnailRow.Add(new Button(PopOutThumbnail) { text = "ポップアップ", tooltip = "サムネイルを独立したウィンドウ(Material プレビュー)に出す。形状・回転・ライトの設定を引き継ぐ" });
            thumbnailRow.style.display = DisplayStyle.None;
            _root.Add(thumbnailRow);
            _thumbnailRow = thumbnailRow;

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

            // Specific 自動解決(2026-09-11): Shader を変えると自動で同期。ボタンは手動で同期し直すとき用。
            _specificRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };
            _specificRow.Add(new Button(SyncSpecificFromShader)
            {
                text = "シェーダーから固有を同期",
                tooltip = "シェーダーの固有プロパティ(共通チャンネル名の規約に該当しないもの)を既定値で Specific に追加する。既存の値は保持",
            });
            _removeConflictsButton = new Button(RemoveSpecificConflicts)
            {
                text = "共通チャンネルの重複を削除",
                tooltip = "Specific に紛れている共通チャンネル名 / 描画ステート名を取り除く(そのままだと Common の値を上書きしてしまう)",
            };
            _removeConflictsButton.style.display = DisplayStyle.None;
            _specificRow.Add(_removeConflictsButton);
            _specificLabel = new Label { style = { marginLeft = 4, whiteSpace = WhiteSpace.Normal, flexShrink = 1 } };
            _specificRow.Add(_specificLabel);
            _specificRow.style.display = DisplayStyle.None;
            _root.Add(_specificRow);

            _inspectorContainer = new VisualElement();
            _root.Add(_inspectorContainer);

            // 2026-09-17(U-13): 「検証」を全エディタで揃える([09] §11)。MaterialData / TextureData の
            // どちらでも、その種別の Validator がそのまま走る。
            _validationSection = new DataValidationSection();
            _root.Add(_validationSection);

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
                _thumbnailDirty = true;
            });
            foldout.Add(_shapeField);

            _modelField = new ObjectField("プレビュー用モデル") { objectType = typeof(ModelData), allowSceneObjects = false };
            _modelField.SetValueWithoutNotify(_previewModel);
            _modelField.RegisterValueChangedCallback(evt => _previewModel = evt.newValue as ModelData);
            _modelField.style.display = _shape == MaterialPreviewShape.Model ? DisplayStyle.Flex : DisplayStyle.None;
            foldout.Add(_modelField);

            var turntableRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _turntableToggle = new Toggle("ターンテーブル") { tooltip = "サムネイルと配置したプレビューを Y 軸で回す" };
            _turntableToggle.SetValueWithoutNotify(_turntableEnabled);
            _turntableToggle.RegisterValueChangedCallback(evt => _turntableEnabled = evt.newValue);
            turntableRow.Add(_turntableToggle);
            foldout.Add(turntableRow);

            _lightRotationSlider = new Slider("ライト回転", 0f, 360f) { tooltip = "サムネイルのライトと、シーンの最初の Directional Light を Y 軸で回す(無ければサムネイルのみ)", style = { flexGrow = 1 } };
            _lightRotationSlider.SetValueWithoutNotify(_thumbnailLightDeg);
            _lightRotationSlider.RegisterValueChangedCallback(evt =>
            {
                _thumbnailLightDeg = evt.newValue;
                _thumbnailDirty = true;
                ApplyLightRotation(evt.newValue);
                SceneView.RepaintAll();
            });
            foldout.Add(_lightRotationSlider);

            var compareRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _compareField = new ObjectField("比較対象") { objectType = typeof(MaterialData), allowSceneObjects = false, style = { flexGrow = 1 } };
            _compareField.SetValueWithoutNotify(_compareTarget);
            _compareField.RegisterValueChangedCallback(evt =>
            {
                _compareTarget = evt.newValue as MaterialData;
                UpdateCompareUi();
            });
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
            _validationSection?.Bind(target as DDrive.Foundation.Data.AssetDataBase);
            _textureImage.style.display = DisplayStyle.None;
            if (target == null)
            {
                _statusLabel.text = "MaterialData か TextureData を選択してください";
                return;
            }

            // 対象ごとに新しいホスト要素へバインドする。同じ要素に TrackPropertyValue を 2 回付けると
            // "An element can track properties on only one serializedObject at a time" になるため(2026-09-11 修正)。
            _inspectorHost?.Unbind();
            var so = new SerializedObject(target);
            _inspectorHost = new VisualElement();
            _inspectorHost.Add(new InspectorElement(so));
            _inspectorContainer.Add(_inspectorHost);
            _inspectorHost.Bind(so);

            if (target is MaterialData mat)
            {
                _trackedShader = mat.Shader;
                _inspectorHost.TrackPropertyValue(so.FindProperty(nameof(MaterialData.Shader)), OnShaderPropertyChanged);
                _inspectorHost.TrackSerializedObjectValue(so, _ => _previewDirty = true);
                _specificRow.style.display = DisplayStyle.Flex;
                UpdateSpecificStatus(mat, null);
                _thumbnailArea.style.display = DisplayStyle.Flex;
                _thumbnailRow.style.display = DisplayStyle.Flex;
                UpdateCompareUi();
            }
            else
            {
                _trackedShader = null;
                _specificRow.style.display = DisplayStyle.None;
                _thumbnailArea.style.display = DisplayStyle.None;
                _thumbnailRow.style.display = DisplayStyle.None;
            }

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

        // Shader フィールドが変わったら(Undo / Redo 含む)固有を同期する。Undo 処理中に Undo を積まないよう delayCall で 1 フレーム遅らせる。
        private void OnShaderPropertyChanged(SerializedProperty property)
        {
            if (_target is not MaterialData mat || mat.Shader == _trackedShader)
            {
                return;
            }

            _trackedShader = mat.Shader;
            EditorApplication.delayCall += () =>
            {
                if (_target != mat || mat == null)
                {
                    return;
                }

                SyncSpecificFromShader();
            };
        }

        private void SyncSpecificFromShader()
        {
            if (_target is not MaterialData mat)
            {
                return;
            }

            var report = MaterialSpecificSync.Sync(mat);
            UpdateSpecificStatus(mat, report);
            if (report.Changed && _primaryPreview != null)
            {
                RebuildPreview();
            }
        }

        // Common を上書きしてしまう項目を Specific から取り除く(2026-09-11 レビュー対応)。
        private void RemoveSpecificConflicts()
        {
            if (_target is not MaterialData mat)
            {
                return;
            }

            var removed = MaterialSpecificSync.RemoveConflicts(mat);
            if (removed > 0)
            {
                _statusLabel.text = $"Specific から共通チャンネル名 {removed} 件を削除しました";
                _previewDirty = true;
            }

            UpdateSpecificStatus(mat, null);
        }

        // Specific に共通チャンネル名が紛れていないか(Common を黙って上書きする)。
        private static int CountSpecificConflicts(MaterialData mat)
        {
            if (mat.Specific == null || mat.Shader == null)
            {
                return 0;
            }

            var count = 0;
            for (var i = 0; i < mat.Specific.Length; i++)
            {
                if (MaterialSpecificResolver.IsConflicting(mat.Shader, mat.Specific[i].Property))
                {
                    count++;
                }
            }

            return count;
        }

        private void UpdateSpecificStatus(MaterialData mat, MaterialSpecificResolver.MergeReport report)
        {
            if (mat.Shader == null)
            {
                _specificLabel.text = "Shader 未設定(既定の Lit で生成)";
                _removeConflictsButton.style.display = DisplayStyle.None;
                return;
            }

            var conflicts = CountSpecificConflicts(mat);
            _removeConflictsButton.style.display = conflicts > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            if (report != null && report.Changed)
            {
                _specificLabel.text = MaterialSpecificSync.Describe(report);
                return;
            }

            var unregistered = MaterialSpecificResolver.FindUnregistered(mat.Specific, mat.Shader);
            _specificLabel.text = unregistered.Count == 0
                ? $"固有 {mat.Specific?.Length ?? 0} 件(シェーダーと同期済み)"
                : $"未登録の固有 {unregistered.Count} 件: {string.Join(", ", unregistered)}";
            if (conflicts > 0)
            {
                _specificLabel.text += $" / Common を上書きする項目 {conflicts} 件";
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
            _pool.SetInstanceParent(_previewRoot.transform);
        }

        // 配置のたびに「今見ている SceneView の視点中心」へ置き、そこへ SceneView を寄せる(2026-09-11: 以前はルート生成時の pivot に固定されていた)。
        private void MovePreviewRootToSceneViewAndFrame()
        {
            var sceneView = SceneView.lastActiveSceneView;
            var position = Vector3.zero;
            if (sceneView != null && sceneView.camera != null)
            {
                var cam = sceneView.camera.transform;
                var distance = Mathf.Clamp(sceneView.cameraDistance, 2f, 10f);
                position = sceneView.in2DMode ? sceneView.pivot : cam.position + cam.forward * distance;
            }

            _previewRoot.transform.position = position;
            _previewRoot.transform.rotation = Quaternion.identity;

            if (sceneView != null)
            {
                var size = _comparePreview != null ? CompareOffsetX + 2f : 2f;
                sceneView.Frame(new Bounds(position + new Vector3(_comparePreview != null ? CompareOffsetX * 0.5f : 0f, 0f, 0f), Vector3.one * size), false);
            }
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

            DeselectPreviewObjects();
            _primaryPreview?.Dispose();
            _comparePreview?.Dispose();
            EnsurePreviewRoot();
            EnsureRegistryFresh();

            _primaryPreview = MaterialPreviewBuilder.Create(_shape, _previewModel, data, _manager, _previewRoot.transform, Vector3.zero, _modelsManager, "Primary");
            if (_compareTarget != null)
            {
                _comparePreview = MaterialPreviewBuilder.Create(_shape, _previewModel, _compareTarget, _manager, _previewRoot.transform,
                    new Vector3(CompareOffsetX, 0f, 0f), _modelsManager, "Compare");
            }

            _statusLabel.text = _primaryPreview != null
                ? (_comparePreview != null ? "プレビューに適用中(比較対象あり)" : "プレビューに適用中")
                : "プレビューの生成に失敗しました(ModelData の Prefab を確認してください)";
            if (_primaryPreview != null)
            {
                MovePreviewRootToSceneViewAndFrame();
            }

            _previewDirty = false;
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
            // Registry の再走査(全 Data の FindAssets + 同期ロード)は重いので、アセットの追加・削除があったときだけ行う(2026-09-11)。
            EnsureRegistryFresh();

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

        // プレビュー物を Inspector で選択したまま破棄すると GameObjectInspector が MissingReference を出すので、先に選択を外す(2026-09-11)。
        private void DeselectPreviewObjects()
        {
            if (_previewRoot == null || Selection.activeTransform == null)
            {
                return;
            }

            if (Selection.activeTransform == _previewRoot.transform || Selection.activeTransform.IsChildOf(_previewRoot.transform))
            {
                Selection.activeObject = null;
            }
        }

        private void RemovePreview()
        {
            DeselectPreviewObjects();
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

            // Data 編集の自動反映(間引き)。共有 Material を作り直し、配置済みプレビューがあれば再適用、サムネイルも描き直す。
            if (_previewDirty && now - _lastAutoRebuildTime >= AutoRebuildIntervalSec)
            {
                _previewDirty = false;
                _lastAutoRebuildTime = now;
                if (_primaryPreview != null)
                {
                    RebuildPreview();
                }
                else
                {
                    _manager.Clear();
                }

                _thumbnailDirty = true;
            }

            if (_turntableEnabled)
            {
                _thumbnailAngle = (_thumbnailAngle + _turntableSpeedDegPerSec * dt) % 360f;
                _thumbnailDirty = true;
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

            // MaterialAnim の再生。非フォーカスかつターンテーブル off のときは描き直さない(2026-09-11 レビュー対応。
            // 以前は EditorApplication.update のたびに PreviewRenderUtility で描いていて、裏に回しても負荷が下がらなかった)。
            if (_target is MaterialData data && data.HasAnims && (hasFocus || _turntableEnabled))
            {
                _thumbnailDirty = true;
                if (_primaryPreview != null)
                {
                    SceneView.RepaintAll();
                }
            }

            // サムネイルの描き直しは 30fps 上限に間引く。
            if (_thumbnailDirty && now - _lastThumbnailTime >= ThumbnailIntervalSec)
            {
                _thumbnailDirty = false;
                _lastThumbnailTime = now;
                RenderThumbnail();
            }
        }

        private void PopOutThumbnail()
        {
            if (_target is not MaterialData data)
            {
                return;
            }

            MaterialThumbnailWindow.Open(data, _shape, _turntableEnabled, _lightRotationSlider?.value ?? 0f);
        }

        // 共有 Material(実 Manager 生成)を PreviewRenderUtility で描いて Image に出す。Model 形状はシーン配置へ誘導。
        private void RenderThumbnail()
        {
            if (_thumbnailArea == null || _thumbnailArea.style.display == DisplayStyle.None || _target is not MaterialData data)
            {
                return;
            }

            if (_shape == MaterialPreviewShape.Model)
            {
                _thumbnailImage.image = null;
                _compareThumbnailImage.image = null;
                _thumbnailCaptionA.text = _thumbnailCaptionB.text = "";
                _thumbnailLabel.text = "Model 形状はサムネイルに出しません。「シーンにプレビューを配置」で確認してください";
                return;
            }

            var hasCompare = _compareTarget != null;
            var sideBySide = hasCompare && _compareMode == ThumbnailCompareMode.SideBySide;
            var showB = hasCompare && _compareMode == ThumbnailCompareMode.Toggle && _showCompareInToggle;

            EnsureRegistryFresh();
            _thumbnail ??= new MaterialThumbnailRenderer();
            var primary = showB ? _compareTarget : data;
            RenderInto(_thumbnail, _thumbnailImage, primary);
            _thumbnailCaptionA.text = hasCompare ? (showB ? "B: " : "A: ") + DisplayNameOf(primary) : "";

            if (sideBySide)
            {
                _compareThumbnail ??= new MaterialThumbnailRenderer();
                RenderInto(_compareThumbnail, _compareThumbnailImage, _compareTarget);
                _thumbnailCaptionB.text = "B: " + DisplayNameOf(_compareTarget);
            }

            _thumbnailLabel.text = hasCompare
                ? "比較: A = 対象 / B = 比較対象(回転・ライトは共通)。既定ライトのみ"
                : "サムネイル: 既定ライトのみ(実シーン照明・モデル適用は「シーンにプレビューを配置」で確認)";
        }

        private void RenderInto(MaterialThumbnailRenderer renderer, Image image, MaterialData data)
        {
            var width = Mathf.RoundToInt(image.resolvedStyle.width);
            if (float.IsNaN(image.resolvedStyle.width) || width < 16)
            {
                width = 320;
            }

            image.image = renderer.Render(_manager.GetData(data), _shape, _thumbnailAngle, _thumbnailPitch, _thumbnailLightDeg, width, ThumbnailHeight);
            image.MarkDirtyRepaint();
        }
    }
}
