using System.Collections.Generic;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Vfx;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
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
using AnchorId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anchoring.AnchorMarker>;

namespace DDrive.Editor.Anchor
{
    // [21_anchor_spec.md] §3.6 — AnchorData 専用エディタ。
    // 連鎖(ルート → 対象)の表示、スポーン先を基準にした解決状態、SceneView のギズモ/ハンドル編集、
    // 確認用 VFX / SE の試し出し(実 Manager 経由、ADR-4)、既存ヒエラルキーからの作成(§3.9)、Validation 表示。
    public sealed class AnchorEditorWindow : EditorWindow
    {
        private const string PathFieldName = "Path";

        [SerializeField] private AnchorData _target;
        [SerializeField] private bool _lockTarget;
        [SerializeField] private GameObject _attachTarget;
        [SerializeField] private bool _sceneHandleEnabled = true;
        [SerializeField] private VfxData _testVfx;
        [SerializeField] private SeData _testSe;

        private SerializedObject _serializedTarget;
        private SceneVfxPreviewDriver _vfxDriver;
        private PreviewService _sePreview;
        private Handle<VfxMarker> _vfxHandle = Handle<VfxMarker>.Invalid;

        private ObjectField _targetField;
        private ObjectField _attachField;
        private VisualElement _chainRow;
        private Label _statusLabel;
        private VisualElement _fieldsContainer;
        private ToolbarMenu _boneDropdown;
        private HelpBox _sceneHelp;
        private Foldout _validationFoldout;
        private Label _vfxStatusLabel;
        private Label _sceneOwnerLabel;

        [MenuItem(DDriveMenu.Editors + "Anchor")]
        public static void Open() => Open(null);

        public static void Open(AnchorData target)
        {
            var window = GetWindow<AnchorEditorWindow>("Anchor Editor");
            window.minSize = new Vector2(480, 360);
            if (target != null)
            {
                window.SetTarget(target);
            }
        }

        // ── ライフサイクル ──

        private void OnEnable()
        {
            _vfxDriver = new SceneVfxPreviewDriver();
            _vfxHandle = Handle<VfxMarker>.Invalid;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            SceneView.duringSceneGui += OnSceneGui;
        }

        // SceneView の描画権: 最後にフォーカスしたウィンドウだけが連鎖・ハンドルを描く(SceneGuiOwner)。
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
            if (!_lockTarget && Selection.activeObject is AnchorData selected && selected != _target)
            {
                SetTarget(selected);
            }
        }

        private void OnUndoRedo()
        {
            _serializedTarget?.Update();
            OnAnchorEdited();
        }

        private void OnActiveSceneChanged(Scene previous, Scene current)
        {
            _vfxHandle = Handle<VfxMarker>.Invalid;
            RefreshSceneHelp();
        }

        // ── UI 構築 ──

        private void CreateGUI()
        {
            BuildToolbar(rootVisualElement);

            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(scrollView);
            var root = scrollView;
            root.style.paddingLeft = 6;
            root.style.paddingRight = 6;
            root.style.paddingTop = 6;

            _targetField = new ObjectField("対象アセット") { objectType = typeof(AnchorData) };
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as AnchorData));
            root.Add(_targetField);

            _chainRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginBottom = 4 } };
            root.Add(_chainRow);

            _sceneHelp = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            root.Add(_sceneHelp);

            _attachField = new ObjectField("スポーン先(シーン内・任意)")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                tooltip = "ルート Anchor の Space/Path をこの階層から検索する。キャラクターや AnchorRig を指定",
            };
            _attachField.SetValueWithoutNotify(_attachTarget);
            _attachField.RegisterValueChangedCallback(evt =>
            {
                _attachTarget = evt.newValue as GameObject;
                RefreshBoneMenu();
                RefreshStatus();
                RestartVfxIfPlaying();
                SceneView.RepaintAll();
            });
            root.Add(_attachField);

            _statusLabel = new Label { style = { opacity = 0.8f, marginLeft = 4, marginBottom = 4, whiteSpace = WhiteSpace.Normal } };
            root.Add(_statusLabel);

            BuildFieldsSection(root);
            BuildPreviewSection(root);
            BuildCreateSection(root);
            BuildValidationSection(root);

            if (_target == null && !_lockTarget && Selection.activeObject is AnchorData selected)
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
            var lockToggle = new ToolbarToggle { text = "🔒 対象を固定", value = _lockTarget, tooltip = "ON: Project ウィンドウの選択に追従しない" };
            lockToggle.RegisterValueChangedCallback(evt => _lockTarget = evt.newValue);
            toolbar.Add(lockToggle);
            toolbar.Add(new ToolbarButton(VfxPreviewSceneSetup.OpenOrCreate) { text = "確認用シーンを開く", tooltip = "AnchorRig 配置済みの VFX 確認用シーンを開く(無ければ生成)" });
            toolbar.Add(new ToolbarButton(() => { if (_target != null) EditorGUIUtility.PingObject(_target); }) { text = "Project で表示" });
            var handleToggle = new ToolbarToggle { text = "SceneView 表示", value = _sceneHandleEnabled, tooltip = "OFF: この Anchor を SceneView に一切描かない。ON: 目印を描き、このウィンドウを最後に操作していれば連鎖・ランダム半径・移動/回転ハンドルも出す" };
            handleToggle.RegisterValueChangedCallback(evt =>
            {
                _sceneHandleEnabled = evt.newValue;
                RefreshSceneOwnerLabel();
                SceneView.RepaintAll();
            });
            toolbar.Add(handleToggle);
            root.Add(toolbar);

            _sceneOwnerLabel = new Label { style = { opacity = 0.65f, marginLeft = 6, marginTop = 2, whiteSpace = WhiteSpace.Normal } };
            root.Add(_sceneOwnerLabel);
            RefreshSceneOwnerLabel();
        }

        private void BuildFieldsSection(VisualElement root)
        {
            var foldout = new Foldout { text = "設定(入れ子 / 基準 / オフセット / ランダム / 生成タイミング)", value = true };
            _fieldsContainer = new VisualElement();
            foldout.Add(_fieldsContainer);
            root.Add(foldout);
        }

        // SerializedObject バインドの PropertyField で全項目を出す(Undo はバインドが面倒を見る)。
        // Path の右に「一覧から選択」(スポーン先の階層のボーン/★AnchorPoint)を足す。
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

            var names = new[]
            {
                "Parent", "Space", PathFieldName, "LocalOffset", "LocalEuler", "LocalScale", "FollowRotation", "DetachOnStop",
                "PositionJitterRadius", "EulerJitter", "ScaleRange", "DelaySec", "DelayJitterSec", "SpawnChance",
            };
            foreach (var name in names)
            {
                var prop = _serializedTarget.FindProperty(name);
                if (prop == null)
                {
                    continue;
                }

                var field = new PropertyField(prop);
                if (name == PathFieldName)
                {
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                    field.style.flexGrow = 1f;
                    row.Add(field);
                    _boneDropdown = new ToolbarMenu { text = "一覧から選択" };
                    row.Add(_boneDropdown);
                    _fieldsContainer.Add(row);
                }
                else
                {
                    _fieldsContainer.Add(field);
                }

                if (name == "Parent")
                {
                    var openParent = new Button(() =>
                    {
                        var parent = _target != null && _target.Parent.IsValid ? EditorAnchorRegistry.Find(_target.Parent.Value) : null;
                        if (parent != null)
                        {
                            SetTarget(parent);
                        }
                    }) { text = "親を開く", style = { alignSelf = Align.FlexEnd } };
                    _fieldsContainer.Add(openParent);
                }
            }

            _fieldsContainer.Bind(_serializedTarget);
            _fieldsContainer.RegisterCallback<SerializedPropertyChangeEvent>(_ => OnAnchorEdited());
            RefreshBoneMenu();
        }

        private void BuildPreviewSection(VisualElement root)
        {
            var foldout = new Foldout { text = "試し出し(実 Manager 経由。この Anchor を指定して再生)", value = true };

            var vfxRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var vfxField = new ObjectField("確認用 VFX") { objectType = typeof(VfxData), style = { flexGrow = 1f } };
            vfxField.SetValueWithoutNotify(_testVfx);
            vfxField.RegisterValueChangedCallback(evt => _testVfx = evt.newValue as VfxData);
            vfxRow.Add(vfxField);
            vfxRow.Add(new Button(PlayVfx) { text = "▶" });
            vfxRow.Add(new Button(StopVfx) { text = "■" });
            foldout.Add(vfxRow);
            _vfxStatusLabel = new Label { style = { opacity = 0.7f, marginLeft = 4 } };
            foldout.Add(_vfxStatusLabel);

            var seRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var seField = new ObjectField("確認用 SE") { objectType = typeof(SeData), style = { flexGrow = 1f } };
            seField.SetValueWithoutNotify(_testSe);
            seField.RegisterValueChangedCallback(evt => _testSe = evt.newValue as SeData);
            seRow.Add(seField);
            seRow.Add(new Button(PlaySe) { text = "▶" });
            seRow.Add(new Button(() => _sePreview?.StopAll()) { text = "■" });
            foldout.Add(seRow);

            foldout.Add(new Label("Delay / SpawnChance / ランダムもここで確認できます(▶ を連打すると散らばりが分かります)") { style = { opacity = 0.6f, whiteSpace = WhiteSpace.Normal } });
            root.Add(foldout);
        }

        private void BuildCreateSection(VisualElement root)
        {
            var foldout = new Foldout { text = "既存のヒエラルキーから作る", value = false };
            foldout.Add(new Label("Hierarchy で Transform(ボーン配下の空オブジェクト・AnchorPoint 等)を選んで作成すると、その親を基準にしたオフセットが自動で入ります。") { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.8f } });
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            row.Add(new Button(() =>
            {
                var t = Selection.activeTransform;
                if (t == null)
                {
                    Debug.LogWarning("[DDrive] Hierarchy で Transform を選択してください。");
                    return;
                }

                var asset = AnchorAssetFactory.CreateFromTransform(t, CategoryFor(t.root.name));
                if (asset != null)
                {
                    AfterCreated(asset);
                }
            }) { text = "選択した Transform から作成" });
            row.Add(new Button(() =>
            {
                var go = Selection.activeGameObject;
                if (go == null || go.GetComponentInChildren<AnchorPoint>(true) == null)
                {
                    Debug.LogWarning("[DDrive] AnchorPoint を含む AnchorRig を選択してください。");
                    return;
                }

                var created = AnchorAssetFactory.CreateFromRig(go);
                if (created.Count > 0)
                {
                    AfterCreated(created[0]);
                }
            }) { text = "AnchorRig から一括生成" });
            foldout.Add(row);
            root.Add(foldout);
        }

        private void BuildValidationSection(VisualElement root)
        {
            _validationFoldout = new Foldout { text = "検証", value = true };
            root.Add(_validationFoldout);
        }

        // ── 対象 ──

        public void SetTarget(AnchorData data)
        {
            StopVfx();
            _target = data;
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
            RefreshChainRow();
            RefreshStatus();
            RefreshValidation();
            SceneView.RepaintAll();
        }

        private void OnAnchorEdited()
        {
            EditorAnchorRegistry.Refresh(_vfxDriver?.Registry);
            _vfxDriver?.ReapplyAnchorToAll();
            RefreshChainRow();
            RefreshStatus();
            RefreshValidation();
            SceneView.RepaintAll();
        }

        private void AfterCreated(AnchorData asset)
        {
            EditorAnchorRegistry.Refresh(_vfxDriver?.Registry);
            SetTarget(asset);
            EditorGUIUtility.PingObject(asset);
        }

        private static string CategoryFor(string rootName)
        {
            var id = AnchorAssetFactory.ToIdentifier(rootName);
            return string.IsNullOrEmpty(id) ? AnchorAssetFactory.DefaultCategory : id;
        }

        // ルート → … → 対象 をボタンで並べる(押すとその Anchor に切り替え)。
        private void RefreshChainRow()
        {
            if (_chainRow == null)
            {
                return;
            }

            _chainRow.Clear();
            if (_target == null)
            {
                return;
            }

            var chain = AnchorChainEditor.CollectRootToTarget(_target);
            _chainRow.Add(new Label("連鎖:") { style = { marginRight = 4, opacity = 0.8f } });
            for (var i = 0; i < chain.Count; i++)
            {
                var node = chain[i];
                var isTarget = ReferenceEquals(node, _target);
                var button = new Button(() => SetTarget(node)) { text = isTarget ? $"[{node.name}]" : node.name };
                button.SetEnabled(!isTarget);
                _chainRow.Add(button);
                if (i < chain.Count - 1)
                {
                    _chainRow.Add(new Label("→") { style = { marginLeft = 2, marginRight = 2 } });
                }
            }

            if (_target.Parent.IsValid && chain.Count > 0 && chain[0].Parent.IsValid)
            {
                _chainRow.Add(new Label("⚠ ルートに辿り着けません(未登録または循環)") { style = { color = new Color(1f, 0.6f, 0.2f), marginLeft = 6 } });
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

            var chain = AnchorChainEditor.CollectRootToTarget(_target);
            var def = AnchorChainEditor.ComposeUpTo(chain, chain.Count - 1);
            var attach = _attachTarget != null ? _attachTarget.transform : null;
            var prefix = chain.Count > 1 ? $"ルート '{chain[0].name}' の基準で" : "基準:";

            switch (def.Space)
            {
                case AnchorSpace.World:
                    _statusLabel.text = $"{prefix} World 固定(合成オフセット = ワールド座標 {def.LocalOffset})";
                    break;
                case AnchorSpace.ContextTarget:
                    _statusLabel.text = attach != null ? $"{prefix} ✓ スポーン先 '{attach.name}' そのもの" : $"{prefix} ⚠ スポーン先が未指定のためワールド固定扱い";
                    break;
                default:
                    if (attach == null)
                    {
                        _statusLabel.text = $"{prefix} ⚠ スポーン先が未指定のため Path を検索できません(ワールド固定扱い)";
                    }
                    else if (string.IsNullOrEmpty(def.Path))
                    {
                        _statusLabel.text = $"{prefix} ⚠ Path が空です。「一覧から選択」でボーンか ★AnchorPoint を選んでください";
                    }
                    else
                    {
                        var resolved = AnchorResolver.Resolve(def, attach);
                        _statusLabel.text = resolved == null
                            ? $"{prefix} ⚠ '{def.Path}' がスポーン先 '{attach.name}' の階層に見つかりません"
                            : $"{prefix} ✓ '{resolved.name}'{(resolved.TryGetComponent<AnchorPoint>(out _) ? "(★AnchorPoint: SpawnOffset/ランダムが追加適用)" : string.Empty)}";
                    }

                    break;
            }

            var delay = 0f;
            var chance = 1f;
            foreach (var node in chain)
            {
                delay += node.DelaySec;
                chance *= Mathf.Clamp01(node.SpawnChance);
            }

            if (delay > 0f || chance < 1f)
            {
                _statusLabel.text += $"  / 生成: {delay:0.##}s 後・確率 {chance:P0}";
            }
        }

        private void RefreshBoneMenu()
        {
            if (_boneDropdown == null)
            {
                return;
            }

            _boneDropdown.menu.MenuItems().Clear();
            if (_attachTarget == null)
            {
                _boneDropdown.menu.AppendAction("(「スポーン先」にシーン内のキャラクターや AnchorRig を指定してください)", _ => { }, DropdownMenuAction.Status.Disabled);
                return;
            }

            foreach (var point in _attachTarget.GetComponentsInChildren<AnchorPoint>(true))
            {
                var name = point.name;
                _boneDropdown.menu.AppendAction($"★ {name}", _ => SelectPath(name));
            }

            _boneDropdown.menu.AppendSeparator();
            foreach (var t in _attachTarget.GetComponentsInChildren<Transform>(true))
            {
                var name = t.name;
                _boneDropdown.menu.AppendAction(name, _ => SelectPath(name));
            }
        }

        private void SelectPath(string name)
        {
            if (_target == null)
            {
                return;
            }

            Undo.RecordObject(_target, "Change Anchor Path");
            _target.Path = name;
            if (_target.Space == AnchorSpace.World || _target.Space == AnchorSpace.ContextTarget)
            {
                _target.Space = AnchorSpace.NamedObject;
            }

            EditorUtility.SetDirty(_target);
            _serializedTarget?.Update();
            OnAnchorEdited();
            RestartVfxIfPlaying();
        }

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

            var all = new List<AssetDataBase>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(AnchorData)))
            {
                var a = AssetDatabase.LoadAssetAtPath<AnchorData>(AssetDatabase.GUIDToAssetPath(guid));
                if (a != null)
                {
                    all.Add(a);
                }
            }

            if (!all.Contains(_target))
            {
                all.Add(_target);
            }

            var any = false;
            foreach (var result in new AnchorDataValidator().Validate(_target, new ValidationContext(all)))
            {
                any = true;
                var type = result.Severity == ValidationSeverity.Error ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning;
                _validationFoldout.Add(new HelpBox(result.Message, type));
            }

            if (!any)
            {
                _validationFoldout.Add(new Label("Validation に問題はありません。") { style = { opacity = 0.6f } });
            }
        }

        private void RefreshSceneHelp()
        {
            if (_sceneHelp == null)
            {
                return;
            }

            var hasCamera = Camera.main != null || FindFirstObjectByType<Camera>() != null;
            var hasLight = FindFirstObjectByType<Light>() != null;
            if (hasCamera && hasLight)
            {
                _sceneHelp.style.display = DisplayStyle.None;
                return;
            }

            _sceneHelp.style.display = DisplayStyle.Flex;
            _sceneHelp.text = "開いているシーンにカメラまたはライトがありません。試し出しの見た目確認には「確認用シーンを開く」を押してください。";
        }

        // ── 試し出し ──

        private void PlayVfx()
        {
            if (_target == null || _testVfx == null || _vfxDriver == null)
            {
                return;
            }

            StopVfx();
            EditorAnchorRegistry.Refresh(_vfxDriver.Registry);
            _vfxHandle = _vfxDriver.Play(_testVfx, _attachTarget != null ? _attachTarget.transform : null, new AnchorId(_target.Id, AssetType.Anchor));
            _vfxStatusLabel.text = _vfxDriver.IsPlaying(_vfxHandle)
                ? (_vfxDriver.Manager.IsPending(_vfxHandle) ? "● 生成待ち(Delay)" : "● 再生中")
                : "(SpawnChance に外れた、または Prefab 未設定)";
        }

        private void StopVfx()
        {
            if (_vfxDriver != null && _vfxDriver.IsPlaying(_vfxHandle))
            {
                _vfxDriver.Kill(_vfxHandle);
            }

            _vfxHandle = Handle<VfxMarker>.Invalid;
            if (_vfxStatusLabel != null)
            {
                _vfxStatusLabel.text = string.Empty;
            }
        }

        private void RestartVfxIfPlaying()
        {
            if (_vfxDriver != null && _vfxDriver.IsPlaying(_vfxHandle))
            {
                PlayVfx();
            }
        }

        private void PlaySe()
        {
            if (_target == null || _testSe == null)
            {
                return;
            }

            if (_sePreview == null)
            {
                _sePreview = new PreviewService();
                _sePreview.Initialize();
            }

            _sePreview.PlaySe(_testSe, _attachTarget != null ? _attachTarget.transform : null, new AnchorId(_target.Id, AssetType.Anchor));
        }

        // ── SceneView ──

        private void RefreshSceneOwnerLabel()
        {
            if (_sceneOwnerLabel != null)
            {
                _sceneOwnerLabel.text = _sceneHandleEnabled ? SceneGuiOwner.DescribeFor(this) : "SceneView: 表示オフ";
            }
        }

        private void OnSceneGui(SceneView sceneView)
        {
            if (_target == null || !_sceneHandleEnabled)
            {
                return;
            }

            var chain = AnchorChainEditor.CollectRootToTarget(_target);
            if (chain.Count == 0)
            {
                return;
            }

            var attach = _attachTarget != null ? _attachTarget.transform : null;
            var rootDef = chain[0].ToDef();
            var baseTransform = AnchorResolver.Resolve(rootDef, attach);
            var extraOffset = Vector3.zero;
            if (baseTransform != null && baseTransform.TryGetComponent<AnchorPoint>(out var point))
            {
                extraOffset = point.SpawnOffset;
            }

            var color = new Color(0.95f, 0.75f, 0.3f);
            var targetDef = AnchorChainEditor.ComposeUpTo(chain, chain.Count - 1);

            // 描画権が他のウィンドウにあるときは対象の薄い目印だけ(連鎖・半径・ハンドルは描かない)。
            if (!SceneGuiOwner.IsOwner(this))
            {
                AnchorSceneHandles.DrawInactiveMarker(targetDef, baseTransform, extraOffset, $"Anchor: {_target.name}", color);
                return;
            }

            AnchorSceneHandles.DrawChain(chain, baseTransform, color);
            var result = AnchorSceneHandles.Draw(targetDef, baseTransform, extraOffset, $"Anchor: {_target.name}", color);
            if (!result.PositionChanged && !result.RotationChanged)
            {
                return;
            }

            AnchorDef? parentDef = chain.Count > 1 ? AnchorChainEditor.ComposeUpTo(chain, chain.Count - 2) : null;
            Undo.RecordObject(_target, "Move Anchor");
            if (result.PositionChanged)
            {
                _target.LocalOffset = AnchorChainEditor.ToChildLocalOffset(parentDef, result.LocalOffset);
            }

            if (result.RotationChanged)
            {
                _target.LocalEuler = AnchorChainEditor.ToChildLocalEuler(parentDef, result.LocalEuler);
            }

            EditorUtility.SetDirty(_target);
            _serializedTarget?.Update();
            OnAnchorEdited();
        }
    }
}
