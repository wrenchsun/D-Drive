using System.Collections.Generic;
using DDrive.Editor.Menu;
using DDrive.Editor.Validation;
using DDrive.Editor.Preview;
using DDrive.Editor.Vfx;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anchor
{
    // [22_anchor_group.md] §3.7 — 配置セット(AnchorGroupData)専用エディタ。
    // 全点のギズモ(番号付き)を SceneView に描き、クリックで点を選択、パターン(Grid の間隔 / Circle の半径 / Line の長さ)や
    // 手置きの点をハンドルで編集、▶ で全点または選択点だけを実 Manager 経由で試し出しする。
    [DDrive.Editor.Inspector.DataEditor(typeof(AnchorGroupData), "Anchor Group Editor で開く")]
    public sealed class AnchorGroupEditorWindow : EditorWindow
    {
        [SerializeField] private AnchorGroupData _target;
        [SerializeField] private bool _lockTarget;
        [SerializeField] private GameObject _attachTarget;
        [SerializeField] private bool _sceneEnabled = true;
        [SerializeField] private int _selectedIndex = -1;

        private SerializedObject _serializedTarget;
        private SceneVfxPreviewDriver _vfxDriver;
        private PreviewService _sePreview;
        private readonly List<Handle<VfxMarker>> _vfxHandles = new();
        private readonly List<AnchorGroupAction> _plan = new();
        private readonly AnchorSpawnSpec[] _pointSpecs = new AnchorSpawnSpec[AnchorGroupData.MaxPoints];
        private int _pointCount;

        private ObjectField _targetField;
        private ObjectField _attachField;
        private HelpBox _sceneHelp;
        private Label _statusLabel;
        private Label _sceneOwnerLabel;
        private Label _selectionLabel;
        private VisualElement _fieldsContainer;
        private DataValidationSection _validationSection;
        private Button _overrideButton;
        private VisualElement _convertRow;

        private static readonly string[] GridFields = { "GridCountX", "GridCountY", "GridCountZ", "GridSpacing", "GridCentered" };
        private static readonly string[] CircleFields = { "CircleCount", "CircleRadius", "CircleStartAngle", "CircleArc", "CircleFaceOutward" };
        private static readonly string[] LineFields = { "LineCount", "LineLength", "LineDirection", "LineCentered" };
        private static readonly string[] RandomFields = { "RandomCount", "RandomRadius", "RandomSeed" };
        private readonly Dictionary<string, VisualElement> _layoutSections = new();

        [MenuItem(DDriveMenu.Editors + "Anchor Group")]
        public static void Open() => Open(null);

        public static void Open(AnchorGroupData target)
        {
            var window = GetWindow<AnchorGroupEditorWindow>("Anchor Group Editor");
            window.minSize = new Vector2(500, 400);
            if (target != null)
            {
                window.SetTarget(target);
            }
        }

        // [08_presentation.md] 指摘3/4(2026-09-20)「一緒に調整」— PresentationEditor の AnchorGroup トラック
        // から開くときに使う。確認用シーンを開き直さない・プレビューを止めない。attachTarget は「スポーン先
        // (シーン内・任意)」欄(_attachTarget)にそのまま渡すだけ(原点の Space/Path の検索起点)。
        public static void Open(AnchorGroupData target, GameObject attachTarget)
        {
            var window = GetWindow<AnchorGroupEditorWindow>("Anchor Group Editor");
            window.minSize = new Vector2(500, 400);
            if (target != null)
            {
                window.SetTarget(target);
            }

            window._attachTarget = attachTarget;
            if (window._attachField != null)
            {
                window._attachField.SetValueWithoutNotify(attachTarget);
                window.RefreshStatus();
                SceneView.RepaintAll();
            }
        }

        // ── ライフサイクル ──

        private void OnEnable()
        {
            _vfxDriver = new SceneVfxPreviewDriver();
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            SceneView.duringSceneGui += OnSceneGui;
        }

        private void OnFocus()
        {
            SceneGuiOwner.Claim(this);
            RefreshSceneOwnerLabel();
        }

        private void OnLostFocus() => RefreshSceneOwnerLabel();

        private void OnDisable()
        {
            SceneGuiOwner.Release(this);
            SceneView.duringSceneGui -= OnSceneGui;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            _vfxDriver?.Dispose();
            _vfxDriver = null;
            _sePreview?.Dispose();
            _sePreview = null;
            SceneView.RepaintAll();
        }

        private void OnSelectionChange()
        {
            if (!_lockTarget && Selection.activeObject is AnchorGroupData selected && selected != _target)
            {
                SetTarget(selected);
            }
        }

        private void OnUndoRedo()
        {
            _serializedTarget?.Update();
            OnEdited();
        }

        private void OnActiveSceneChanged(Scene previous, Scene current)
        {
            _vfxHandles.Clear();
            RefreshSceneHelp();
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

            _targetField = new ObjectField("対象アセット") { objectType = typeof(AnchorGroupData) };
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as AnchorGroupData));
            root.Add(_targetField);

            _sceneHelp = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            root.Add(_sceneHelp);

            _attachField = new ObjectField("スポーン先(シーン内・任意)")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                tooltip = "原点の Space/Path をこの階層から検索する。キャラクターや AnchorRig を指定",
            };
            _attachField.SetValueWithoutNotify(_attachTarget);
            _attachField.RegisterValueChangedCallback(evt =>
            {
                _attachTarget = evt.newValue as GameObject;
                RefreshStatus();
                SceneView.RepaintAll();
            });
            root.Add(_attachField);

            _statusLabel = new Label { style = { opacity = 0.8f, marginLeft = 4, marginBottom = 4, whiteSpace = WhiteSpace.Normal } };
            root.Add(_statusLabel);

            BuildPreviewSection(root);

            var fieldsFoldout = new Foldout { text = "設定(原点 / パターン / 点ごとの生成 / 出すもの / 上書き / 入れ子)", value = true };
            _fieldsContainer = new VisualElement();
            fieldsFoldout.Add(_fieldsContainer);
            root.Add(fieldsFoldout);

            // 2026-09-17(U-13): 独自実装から共通の個別検証セクションに置き換えた([09] §11)。
            _validationSection = new DataValidationSection(includeSameTypeAssets: true);
            root.Add(_validationSection);

            if (_target == null && !_lockTarget && Selection.activeObject is AnchorGroupData selected)
            {
                SetTarget(selected);
            }
            else
            {
                RefreshTargetUi();
            }

            RefreshSceneHelp();
        }

        private void BuildToolbar(VisualElement root)
        {
            var toolbar = new Toolbar();
            var lockToggle = new ToolbarToggle { text = "🔒 対象を固定", value = _lockTarget };
            lockToggle.RegisterValueChangedCallback(evt => _lockTarget = evt.newValue);
            toolbar.Add(lockToggle);
            toolbar.Add(new ToolbarButton(VfxPreviewSceneSetup.OpenOrCreate) { text = "確認用シーンを開く" });
            toolbar.Add(new ToolbarButton(() => { if (_target != null) EditorGUIUtility.PingObject(_target); }) { text = "Project で表示" });
            var sceneToggle = new ToolbarToggle { text = "SceneView 表示", value = _sceneEnabled, tooltip = "OFF: この配置セットを SceneView に描かない。ON: 全点の番号付きギズモ。最後に操作したウィンドウならクリック選択とハンドルも" };
            sceneToggle.RegisterValueChangedCallback(evt =>
            {
                _sceneEnabled = evt.newValue;
                RefreshSceneOwnerLabel();
                SceneView.RepaintAll();
            });
            toolbar.Add(sceneToggle);
            toolbar.Add(DDrive.Editor.Inspector.NewAssetToolbarButton.CreateToolbarButton(typeof(AnchorGroupEditorWindow)));
            root.Add(toolbar);

            _sceneOwnerLabel = new Label { style = { opacity = 0.65f, marginLeft = 6, marginTop = 2, whiteSpace = WhiteSpace.Normal } };
            root.Add(_sceneOwnerLabel);
            RefreshSceneOwnerLabel();
        }

        private void BuildPreviewSection(VisualElement root)
        {
            var foldout = new Foldout { text = "試し出し(実 Manager 経由)", value = true };
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            row.Add(new Button(() => PlayPreview(onlySelected: false)) { text = "▶ 全点" });
            row.Add(new Button(() => PlayPreview(onlySelected: true)) { text = "▶ 選択点のみ" });
            row.Add(new Button(StopPreview) { text = "■ 停止" });
            foldout.Add(row);

            _selectionLabel = new Label { style = { opacity = 0.75f, marginLeft = 4, whiteSpace = WhiteSpace.Normal } };
            foldout.Add(_selectionLabel);
            var selRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _overrideButton = new Button(AddOverrideForSelection) { text = "選択点を Overrides に追加(この点だけ変える / 出さない)" };
            selRow.Add(_overrideButton);
            selRow.Add(new Button(() => { _selectedIndex = -1; RefreshSelectionLabel(); SceneView.RepaintAll(); }) { text = "選択解除" });
            foldout.Add(selRow);
            foldout.Add(new Label("SceneView で点をクリックすると選択できます。Grid は端の点、Circle は 0 番、Line は端の点をドラッグするとパターンの大きさが変わります。手置きの点は直接ドラッグ。") { style = { opacity = 0.6f, whiteSpace = WhiteSpace.Normal } });
            root.Add(foldout);
        }

        private void RebuildFields()
        {
            if (_fieldsContainer == null)
            {
                return;
            }

            _fieldsContainer.Clear();
            _fieldsContainer.Unbind();
            _layoutSections.Clear();
            if (_serializedTarget == null)
            {
                _fieldsContainer.Add(new Label("対象アセットを選択してください") { style = { opacity = 0.6f } });
                return;
            }

            AddField("OriginAnchorId", "原点 Anchor アセット");
            AddField("Origin", "原点(埋め込み。上が 0 のとき)");
            AddField("Layout", "配置パターン");
            AddSection("Grid", GridFields);
            AddSection("Circle", CircleFields);
            AddSection("Line", LineFields);
            AddSection("Random", RandomFields);
            AddConvertRow();
            AddField("Points", "手置きの点");
            AddField("DelayPerIndex", "番号順ディレイ(秒/点)");
            AddField("DelayJitterSec", "ディレイのランダム(秒)");
            AddField("ChancePerPoint", "各点の生成確率");
            AddField("PositionJitterRadius", "位置ランダム半径");
            AddField("EulerJitter", "回転ランダム");
            AddField("ScaleRange", "スケール範囲");
            AddField("SharedVfx", "全点共通 VFX");
            AddField("SharedSe", "全点共通 SE");
            AddField("Overrides", "特定の点だけ変える / 出さない");
            AddField("Children", "入れ子(各点に別の配置セット)");

            _fieldsContainer.Bind(_serializedTarget);
            _fieldsContainer.RegisterCallback<SerializedPropertyChangeEvent>(_ => OnEdited());
            RefreshLayoutSections();
        }

        private void AddField(string name, string label)
        {
            var prop = _serializedTarget.FindProperty(name);
            if (prop != null)
            {
                _fieldsContainer.Add(new PropertyField(prop, label));
            }
        }

        private void AddSection(string key, string[] names)
        {
            var section = new VisualElement { style = { marginLeft = 8 } };
            foreach (var n in names)
            {
                var prop = _serializedTarget.FindProperty(n);
                if (prop != null)
                {
                    section.Add(new PropertyField(prop));
                }
            }

            _layoutSections[key] = section;
            _fieldsContainer.Add(section);
        }

        // 「手置きの点に変換」(U-22)。パターンのときだけ出す。
        // 横幅 500px でも切れないように、行は折り返し可・ボタンとラベルは縮む([09] §7.1)。
        private void AddConvertRow()
        {
            _convertRow = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, marginLeft = 8, marginBottom = 4 },
            };
            _convertRow.Add(new Button(ConvertPatternToPoints)
            {
                text = "手置きの点に変換",
                tooltip = "今のパターンで計算された点を、そのまま手置きの点(Points)として書き込み、Layout を Manual に切り替える。点を 1 つずつ動かせるようになる。Ctrl+Z で元に戻せる",
                style = { flexShrink = 1f, minWidth = 0f, whiteSpace = WhiteSpace.Normal, marginRight = 4 },
            });
            _convertRow.Add(new Label("点を 1 つずつ動かしたくなったら押す。番号は変わりません。")
            {
                style = { opacity = 0.6f, flexShrink = 1f, flexGrow = 1f, minWidth = 0f, whiteSpace = WhiteSpace.Normal },
            });
            _fieldsContainer.Add(_convertRow);
        }

        private void RefreshLayoutSections()
        {
            if (_target == null)
            {
                return;
            }

            foreach (var (key, section) in _layoutSections)
            {
                section.style.display = key == _target.Layout.ToString() ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_convertRow != null)
            {
                _convertRow.style.display = AnchorGroupPointConverter.CanConvert(_target) ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // パターンの計算結果を手置きの点へ焼き付ける(U-22)。計算は AnchorLayout をそのまま通すので配置は変わらない。
        private void ConvertPatternToPoints()
        {
            if (!AnchorGroupPointConverter.CanConvert(_target))
            {
                return;
            }

            RefreshPoints();
            var message =
                $"「{_target.Layout}」パターンで計算された {_pointCount} 点を、手置きの点(Points)として書き込みます。\n\n" +
                "・Layout は Manual になり、点を 1 つずつ SceneView でドラッグして調整できます\n" +
                "・点の番号と並び順は変わらないので、Overrides / 入れ子の指定はそのまま使えます\n" +
                "・パターンの設定値(間隔・半径・長さなど)は残るので、Layout を戻せばやり直せます\n" +
                "・取り消しは Ctrl+Z(Undo)でできます";
            if (!EditorUtility.DisplayDialog("手置きの点に変換", message, "変換する", "キャンセル"))
            {
                return;
            }

            if (AnchorGroupPointConverter.Convert(_target) <= 0)
            {
                return;
            }

            _serializedTarget?.Update();
            OnEdited();
        }

        // ── 対象 ──

        public void SetTarget(AnchorGroupData data)
        {
            StopPreview();
            _target = data;
            _selectedIndex = -1;
            _targetField?.SetValueWithoutNotify(data);
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
            RefreshStatus();
            RefreshSelectionLabel();
            RefreshValidation();
            SceneView.RepaintAll();
        }

        private void OnEdited()
        {
            RefreshLayoutSections();
            EditorAnchorRegistry.Refresh(_vfxDriver?.Registry);
            RefreshStatus();
            RefreshSelectionLabel();
            RefreshValidation();
            SceneView.RepaintAll();
        }

        private void RefreshPoints()
        {
            _pointCount = _target != null ? AnchorGroupPlanner.EnumeratePoints(_vfxDriver?.Registry, _target, _pointSpecs) : 0;
            if (_selectedIndex >= _pointCount)
            {
                _selectedIndex = -1;
            }
        }

        private void RefreshStatus()
        {
            if (_statusLabel == null)
            {
                return;
            }

            if (_target == null)
            {
                _statusLabel.text = string.Empty;
                return;
            }

            RefreshPoints();
            var attach = _attachTarget != null ? _attachTarget.transform : null;
            var originDef = OriginDef();
            string originText;
            switch (originDef.Space)
            {
                case AnchorSpace.World:
                    originText = "原点: World 固定";
                    break;
                case AnchorSpace.ContextTarget:
                    originText = attach != null ? $"原点: ✓ スポーン先 '{attach.name}'" : "原点: ⚠ スポーン先が未指定(ワールド固定扱い)";
                    break;
                default:
                    if (attach == null)
                    {
                        originText = "原点: ⚠ スポーン先が未指定のため Path を検索できません";
                    }
                    else
                    {
                        var resolved = AnchorResolver.Resolve(originDef, attach);
                        originText = resolved != null ? $"原点: ✓ '{resolved.name}'" : $"原点: ⚠ '{originDef.Path}' が見つかりません";
                    }

                    break;
            }

            var vfxCount = CountValid(_target.SharedVfx);
            var seCount = CountValid(_target.SharedSe);
            _statusLabel.text = $"{originText}  / 点: {_pointCount}  / 全点共通: VFX {vfxCount}・SE {seCount}" +
                                (_target.Children != null && _target.Children.Length > 0 ? $"  / 入れ子: {_target.Children.Length}" : string.Empty) +
                                (_target.DelayPerIndex > 0f ? $"  / 番号順ディレイ {_target.DelayPerIndex:0.##}s" : string.Empty) +
                                (_target.ChancePerPoint < 1f ? $"  / 確率 {_target.ChancePerPoint:P0}" : string.Empty);
        }

        private static int CountValid<T>(DDrive.Foundation.Identity.AssetId<T>[] ids)
        {
            var n = 0;
            if (ids != null)
            {
                foreach (var id in ids)
                {
                    if (id.IsValid)
                    {
                        n++;
                    }
                }
            }

            return n;
        }

        private AnchorDef OriginDef()
        {
            if (_target.OriginAnchorId.IsValid && _vfxDriver != null)
            {
                return AnchorChain.Resolve(_vfxDriver.Registry, _target.OriginAnchorId, sampleRandom: false).Def;
            }

            return _target.Origin;
        }

        private void RefreshSelectionLabel()
        {
            if (_selectionLabel == null)
            {
                return;
            }

            if (_target == null || _selectedIndex < 0)
            {
                _selectionLabel.text = "選択中の点: なし";
                _overrideButton?.SetEnabled(false);
                return;
            }

            var manualStart = _pointCount - (_target.Points?.Length ?? 0);
            var kind = _selectedIndex >= manualStart ? "手置き" : _target.Layout.ToString();
            var overrideIndex = _target.FindOverride(_selectedIndex);
            var overrideText = overrideIndex >= 0 ? (_target.Overrides[overrideIndex].Skip ? "(出さない)" : "(上書きあり)") : string.Empty;
            _selectionLabel.text = $"選択中の点: #{_selectedIndex} [{kind}] {overrideText}  ローカル {_pointSpecs[_selectedIndex].Def.LocalOffset}";
            _overrideButton?.SetEnabled(overrideIndex < 0);
        }

        private void AddOverrideForSelection()
        {
            if (_target == null || _selectedIndex < 0 || _target.FindOverride(_selectedIndex) >= 0)
            {
                return;
            }

            Undo.RecordObject(_target, "Add Anchor Group Override");
            var list = new List<AnchorGroupOverride>(_target.Overrides ?? System.Array.Empty<AnchorGroupOverride>())
            {
                new AnchorGroupOverride { Index = _selectedIndex },
            };
            _target.Overrides = list.ToArray();
            EditorUtility.SetDirty(_target);
            _serializedTarget?.Update();
            OnEdited();
        }

        private void RefreshValidation() => _validationSection?.Bind(_target);

        private void RefreshSceneHelp()
        {
            if (_sceneHelp == null)
            {
                return;
            }

            var ok = (Camera.main != null || FindFirstObjectByType<Camera>() != null) && FindFirstObjectByType<Light>() != null;
            _sceneHelp.style.display = ok ? DisplayStyle.None : DisplayStyle.Flex;
            _sceneHelp.text = "開いているシーンにカメラまたはライトがありません。試し出しの見た目確認には「確認用シーンを開く」を押してください。";
        }

        private void RefreshSceneOwnerLabel()
        {
            if (_sceneOwnerLabel != null)
            {
                _sceneOwnerLabel.text = _sceneEnabled ? SceneGuiOwner.DescribeFor(this) : "SceneView: 表示オフ";
            }
        }

        // ── 試し出し ──

        private void PlayPreview(bool onlySelected)
        {
            if (_target == null || _vfxDriver == null)
            {
                return;
            }

            StopPreview();
            EditorAnchorRegistry.Refresh(_vfxDriver.Registry);
            _plan.Clear();
            AnchorGroupPlanner.Plan(_vfxDriver.Registry, _target, sampleRandom: true, _plan);
            var attach = _attachTarget != null ? _attachTarget.transform : null;

            foreach (var action in _plan)
            {
                if (onlySelected && action.PointIndex != _selectedIndex)
                {
                    continue;
                }

                if (action.Kind == AnchorGroupActionKind.Vfx)
                {
                    var data = _vfxDriver.Registry.ResolveOrPlaceholder<VfxData>(action.AssetId);
                    var h = _vfxDriver.Play(data, attach, action.Spec);
                    if (_vfxDriver.IsPlaying(h))
                    {
                        _vfxHandles.Add(h);
                    }
                }
                else
                {
                    if (_sePreview == null)
                    {
                        _sePreview = new PreviewService();
                        _sePreview.Initialize();
                    }

                    var data = _vfxDriver.Registry.ResolveOrPlaceholder<SeData>(action.AssetId);
                    _sePreview.PlaySe(data, attach, action.Spec);
                }
            }

            _plan.Clear();
        }

        private void StopPreview()
        {
            if (_vfxDriver != null)
            {
                foreach (var h in _vfxHandles)
                {
                    _vfxDriver.Kill(h);
                }
            }

            _vfxHandles.Clear();
            _sePreview?.StopAll();
        }

        // ── SceneView ──

        private void OnSceneGui(SceneView sceneView)
        {
            if (_target == null || !_sceneEnabled)
            {
                return;
            }

            RefreshPoints();
            var attach = _attachTarget != null ? _attachTarget.transform : null;
            var originDef = OriginDef();
            var baseTransform = AnchorResolver.Resolve(originDef, attach);
            var extraOffset = Vector3.zero;
            if (baseTransform != null && baseTransform.TryGetComponent<AnchorPoint>(out var point))
            {
                extraOffset = point.SpawnOffset;
            }

            var color = new Color(0.4f, 0.8f, 1f);
            var originPos = AnchorPose.WorldPosition(originDef, baseTransform, extraOffset);

            if (!SceneGuiOwner.IsOwner(this))
            {
                Handles.color = new Color(color.r, color.g, color.b, 0.25f);
                for (var i = 0; i < _pointCount; i++)
                {
                    var p = AnchorPose.WorldPosition(_pointSpecs[i].Def, baseTransform, extraOffset);
                    Handles.DrawWireDisc(p, Vector3.up, HandleUtility.GetHandleSize(p) * 0.1f);
                }

                Handles.Label(originPos, $"Group: {_target.name}", EditorStyles.miniLabel);
                return;
            }

            // 基準(解決先 Transform。未解決ならワールド原点)→ 原点(基準 + Origin の LocalOffset)(U-24)
            var baseWorld = AnchorSceneHandles.DrawOrigin(baseTransform, extraOffset, AnchorSceneHandles.DescribeBase(originDef, baseTransform), originDef.FollowRotation);
            AnchorSceneHandles.DrawOffsetLink(baseWorld, originPos, originDef.LocalOffset, color);

            // 原点
            Handles.color = color;
            var originSize = HandleUtility.GetHandleSize(originPos);
            Handles.DrawWireDisc(originPos, Vector3.up, originSize * 0.3f);
            Handles.Label(originPos + Vector3.up * originSize * 0.4f, $"Group: {_target.name}(原点)");

            // 各点(クリックで選択)
            var manualStart = _pointCount - (_target.Points?.Length ?? 0);
            for (var i = 0; i < _pointCount; i++)
            {
                var p = AnchorPose.WorldPosition(_pointSpecs[i].Def, baseTransform, extraOffset);
                var size = HandleUtility.GetHandleSize(p) * 0.12f;
                var overrideIndex = _target.FindOverride(i);
                var skipped = overrideIndex >= 0 && _target.Overrides[overrideIndex].Skip;
                Handles.color = i == _selectedIndex ? Color.yellow : skipped ? new Color(0.5f, 0.5f, 0.5f, 0.6f) : (overrideIndex >= 0 ? new Color(1f, 0.6f, 0.3f) : color);
                if (Handles.Button(p, Quaternion.identity, size, size * 1.5f, Handles.SphereHandleCap))
                {
                    _selectedIndex = i;
                    RefreshSelectionLabel();
                    Repaint();
                }

                Handles.Label(p + Vector3.up * size * 1.6f, skipped ? $"{i} ×" : i.ToString(), EditorStyles.miniLabel);
                if (i >= manualStart)
                {
                    Handles.DrawDottedLine(originPos, p, 3f);
                }
            }

            if (_selectedIndex < 0 || _selectedIndex >= _pointCount)
            {
                return;
            }

            // 選択点のハンドル: 手置きは直接、パターンは大きさ(間隔/半径/長さ)を変える。
            var selectedWorld = AnchorPose.WorldPosition(_pointSpecs[_selectedIndex].Def, baseTransform, extraOffset);
            EditorGUI.BeginChangeCheck();
            var newWorld = Handles.PositionHandle(selectedWorld, Quaternion.identity);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            // ワールド → 原点ローカル(原点の回転・スケール込みで戻す)
            var composed = AnchorPose.LocalOffsetFromWorld(baseTransform, newWorld, extraOffset);
            var originRot = Quaternion.Euler(originDef.LocalEuler);
            var originScale = originDef.LocalScale == Vector3.zero ? Vector3.one : originDef.LocalScale;
            var local = Quaternion.Inverse(originRot) * (composed - originDef.LocalOffset);
            local = new Vector3(
                originScale.x != 0f ? local.x / originScale.x : 0f,
                originScale.y != 0f ? local.y / originScale.y : 0f,
                originScale.z != 0f ? local.z / originScale.z : 0f);

            Undo.RecordObject(_target, "Move Anchor Group Point");
            if (_selectedIndex >= manualStart)
            {
                _target.Points[_selectedIndex - manualStart].LocalOffset = local;
            }
            else
            {
                ApplyPatternDrag(local);
            }

            EditorUtility.SetDirty(_target);
            _serializedTarget?.Update();
            OnEdited();
        }

        // 選択したパターン点のドラッグを、パターンの大きさに変換する。
        private void ApplyPatternDrag(Vector3 local)
        {
            switch (_target.Layout)
            {
                case AnchorLayoutKind.Grid:
                {
                    // 端の点(角)からの距離で間隔を決める。中央寄せなら半分が距離。
                    var cx = Mathf.Max(1, _target.GridCountX);
                    var cy = Mathf.Max(1, _target.GridCountY);
                    var cz = Mathf.Max(1, _target.GridCountZ);
                    var ix = _selectedIndex % cx;
                    var iz = (_selectedIndex / cx) % cz;
                    var iy = _selectedIndex / (cx * cz);
                    var spacing = _target.GridSpacing;
                    var fx = _target.GridCentered ? ix - (cx - 1) * 0.5f : ix;
                    var fz = _target.GridCentered ? iz - (cz - 1) * 0.5f : iz;
                    var fy = _target.GridCentered ? iy - (cy - 1) * 0.5f : iy;
                    if (Mathf.Abs(fx) > 1e-3f) spacing.x = Mathf.Max(0f, local.x / fx);
                    if (Mathf.Abs(fz) > 1e-3f) spacing.z = Mathf.Max(0f, local.z / fz);
                    if (Mathf.Abs(fy) > 1e-3f) spacing.y = Mathf.Max(0f, local.y / fy);
                    _target.GridSpacing = spacing;
                    break;
                }
                case AnchorLayoutKind.Circle:
                    _target.CircleRadius = new Vector2(local.x, local.z).magnitude;
                    break;
                case AnchorLayoutKind.Line:
                {
                    var dir = _target.LineDirection.sqrMagnitude > 1e-6f ? _target.LineDirection.normalized : Vector3.forward;
                    var n = Mathf.Max(1, _target.LineCount);
                    if (n > 1)
                    {
                        var t = _selectedIndex / (float)(n - 1);
                        var along = Vector3.Dot(local, dir);
                        var f = _target.LineCentered ? t - 0.5f : t;
                        if (Mathf.Abs(f) > 1e-3f)
                        {
                            _target.LineLength = Mathf.Max(0f, along / f);
                        }
                    }

                    break;
                }
                case AnchorLayoutKind.Random:
                    _target.RandomRadius = Mathf.Max(_target.RandomRadius, local.magnitude);
                    break;
            }
        }
    }
}
