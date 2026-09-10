using System.Collections.Generic;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ui;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.CanvasTool
{
    // [07_canvas_prefab.md] Part A の最小エディタ(4-1)。編集は SerializedObject バインド(Undo 対応)、
    // プレビューは実 UiManager で OpenData した実体を開いているシーン / プレハブステージの DontSave
    // ルートに置いて SceneView / Game View で確認する(ADR-4: Editor 専用の再生経路を作らない。
    // PrefabEditorWindow / MaterialEditorWindow と同じ設計。owner instruction 2026-09-10: EditorWindow 内部には描画しない)。
    // ノードグラフによるナビゲーション編集・ゲームパッド入力シミュレーションはチケット 4-3(未実装)。
    [DDrive.Editor.Inspector.DataEditor(typeof(CanvasData), "Canvas Editor で開く")]
    public sealed class CanvasEditorWindow : EditorWindow
    {
        private const string NoneChoice = "なし";

        private CanvasData _target;
        private bool _lockTarget;
        private UiManager _manager;
        private AssetRegistry _registry;
        private PoolService _pool;
        private UiTweenManager _tweenManager;
        private double _lastEditorTime;
        private UiPreset _bulkPreset;

        private GameObject _previewRoot;
        private Handle<CanvasMarker> _previewHandle = Handle<CanvasMarker>.Invalid;

        private ScrollView _root;
        private ObjectField _targetField;
        private VisualElement _inspectorContainer;
        private VisualElement _elementFxContainer;
        private Label _statusLabel;
        private VisualElement _validationFoldout;

        private readonly List<(string label, UiTweenData tween)> _catalogChoices = new();

        [MenuItem(DDriveMenu.Editors + "Canvas")]
        public static void OpenFromMenu() => Open(Selection.activeObject as CanvasData);

        public static void Open(CanvasData target)
        {
            var window = GetWindow<CanvasEditorWindow>("Canvas Editor");
            window.minSize = new Vector2(380, 320);
            if (target != null)
            {
                window.SetTarget(target);
            }
        }

        private void OnEnable()
        {
            _registry = EditorAnchorRegistry.Build();
            _pool = new PoolService();
            _tweenManager = new UiTweenManager(_registry);
            _manager = new UiManager(_pool, _registry, tweens: _tweenManager);
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            _lastEditorTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            RemovePreview();
            _manager = null;
            _tweenManager = null;
            _pool = null;
            _registry = null;
        }

        // ADR-4 / owner instruction 2026-09-10: 確認用シーンのプレビューは実 UiManager + UiTweenManager を
        // EditorApplication.update から駆動する(ウィンドウ内には何も描画しない)。ElementFx(4-9)の
        // Appear/Idle/Disappear は UiTweenManager.Tick が進めないと一切動かないため、UiTweenEditorWindow と
        // 同じ手順でここでも Tick する。
        private void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = (float)(now - _lastEditorTime);
            _lastEditorTime = now;
            if (_manager == null || dt <= 0f || dt > 1f)
            {
                return;
            }

            _tweenManager?.Tick(dt);
            _manager.Tick(dt);
        }

        private void OnSelectionChange()
        {
            if (!_lockTarget && Selection.activeObject is CanvasData data && data != _target)
            {
                SetTarget(data);
            }
        }

        private void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene previous, UnityEngine.SceneManagement.Scene current)
        {
            // シーンが切り替わると DontSave の配置物は Unity 側で既に失われているので、参照だけ捨てる。
            _previewRoot = null;
            _previewHandle = Handle<CanvasMarker>.Invalid;
        }

        public void CreateGUI()
        {
            _root = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            rootVisualElement.Add(_root);

            var toolbar = new Toolbar();
            _targetField = new ObjectField("対象") { objectType = typeof(CanvasData), allowSceneObjects = false, style = { flexGrow = 1 } };
            _targetField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is CanvasData data)
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
            _root.Add(toolbar);

            var navButtons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
            navButtons.Add(new Button(CollectSelectables) { text = "Selectable を自動収集", tooltip = "Prefab 内の Selectable から Navigation を作る(既存の行は保持する)" });
            _root.Add(navButtons);

            var fxButtons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
            fxButtons.Add(new Button(CollectElementFx) { text = "要素を自動収集(Image / UiButton / パネル)", tooltip = "Prefab 内の Graphic/UiInteractable から ElementFx の行を作る(既存の行は保持する)" });
            _root.Add(fxButtons);

            var bulkRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, alignItems = Align.Center } };
            var presetField = new EnumField("プリセット", _bulkPreset) { style = { flexGrow = 1f } };
            presetField.RegisterValueChangedCallback(evt => _bulkPreset = (UiPreset)evt.newValue);
            bulkRow.Add(presetField);
            bulkRow.Add(new Button(ApplyBulkPresetToButtons) { text = "一括適用: 全ボタンに反映", tooltip = "Prefab 内の全 UiButton の AppearPreset にこのプリセットを設定する" });
            _root.Add(bulkRow);

            _root.Add(new Label("ElementFx 割当(Appear / Idle / Disappear)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
            _root.Add(new HelpBox("各要素の行でプリセット・プロジェクト独自カタログ([Catalog] 名前)・UiTweenData 直接指定のいずれかを選べます。", HelpBoxMessageType.Info));
            _elementFxContainer = new VisualElement();
            _root.Add(_elementFxContainer);

            var previewButtons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, marginBottom = 4 } };
            previewButtons.Add(new Button(PlacePreview) { text = "確認用シーンで開く", tooltip = "開いているシーン(またはプレハブステージ)に実 UiManager で OpenData する" });
            previewButtons.Add(new Button(RemovePreview) { text = "閉じる" });
            _root.Add(previewButtons);

            _statusLabel = new Label { style = { marginLeft = 4, marginBottom = 4 } };
            _root.Add(_statusLabel);

            _root.Add(new HelpBox("ノードグラフでのナビゲーション編集・ゲームパッド入力シミュレーションはチケット 4-3 で実装予定です。ここでは自動収集とテキストでの編集のみ行えます。", HelpBoxMessageType.Info));

            _inspectorContainer = new VisualElement();
            _root.Add(_inspectorContainer);

            var validationHeader = new Label("Validation") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } };
            _root.Add(validationHeader);
            _validationFoldout = new VisualElement();
            _root.Add(_validationFoldout);

            if (_target != null)
            {
                SetTarget(_target);
            }
            else if (Selection.activeObject is CanvasData data)
            {
                SetTarget(data);
            }
        }

        private void SetTarget(CanvasData target)
        {
            _target = target;
            if (_root == null)
            {
                return;
            }

            _targetField.SetValueWithoutNotify(target);
            _inspectorContainer.Clear();
            if (target == null)
            {
                _statusLabel.text = "CanvasData を選択してください";
                _validationFoldout?.Clear();
                _elementFxContainer?.Clear();
                return;
            }

            var so = new SerializedObject(target);
            _inspectorContainer.Add(new InspectorElement(so));
            _inspectorContainer.Bind(so);

            _statusLabel.text = _manager != null && _manager.IsOpen(_previewHandle) ? "プレビュー表示中" : "「確認用シーンで開く」で確認できます";
            RefreshValidation();
            RebuildElementFxAssignments();
        }

        private void CollectSelectables()
        {
            if (_target == null || _target.Prefab == null)
            {
                _statusLabel.text = "Prefab を設定してください";
                return;
            }

            var merged = CanvasNavigationCollector.CollectMerged(_target.Prefab, _target.Navigation);

            Undo.RecordObject(_target, "Collect Selectables");
            _target.Navigation = merged;
            EditorUtility.SetDirty(_target);

            SetTarget(_target); // Inspector / Validation を再構築
            _statusLabel.text = $"Selectable を {merged.Length} 件収集しました";
        }

        // 4-9: Graphic(Image 等)/UiInteractable(UiButton 等)/パネル(RectTransform+Graphic)を持つパスを
        // ElementEffects に追加する(既存の行・値は保持する)。
        private void CollectElementFx()
        {
            if (_target == null || _target.Prefab == null)
            {
                _statusLabel.text = "Prefab を設定してください";
                return;
            }

            var merged = CanvasElementFxCollector.CollectMerged(_target.Prefab, _target.ElementEffects);

            Undo.RecordObject(_target, "Collect ElementFx");
            _target.ElementEffects = merged;
            EditorUtility.SetDirty(_target);

            SetTarget(_target);
            _statusLabel.text = $"ElementFx を {merged.Length} 件収集しました";
        }

        // 4-10: 各 ElementFx 行の Appear/Idle/Disappear を「なし / 組み込みプリセット / プロジェクトの
        // UiPresetCatalog / UiTweenData 直接指定」から選べる UI(PopupField + ObjectField)。
        // 選択の解決優先順位はランタイム側([15] B-5 実装メモ)と同じ id > Preset。
        private void RebuildElementFxAssignments()
        {
            if (_elementFxContainer == null)
            {
                return;
            }

            _elementFxContainer.Clear();
            if (_target == null || _target.ElementEffects == null || _target.ElementEffects.Length == 0)
            {
                _elementFxContainer.Add(new Label("ElementFx がありません(上の「要素を自動収集」で追加してください)") { style = { opacity = 0.7f } });
                return;
            }

            _catalogChoices.Clear();
            foreach (var (name, tween) in UiPresetCatalogUtility.Collect())
            {
                _catalogChoices.Add(($"[Catalog] {name}", tween));
            }

            for (var i = 0; i < _target.ElementEffects.Length; i++)
            {
                var index = i;
                var fx = _target.ElementEffects[i];
                var box = new Box { style = { marginBottom = 6, paddingLeft = 4, paddingTop = 2, paddingBottom = 4 } };
                box.Add(new Label(string.IsNullOrEmpty(fx.ElementPath) ? "(ルート)" : fx.ElementPath) { style = { unityFontStyleAndWeight = FontStyle.Bold } });

                box.Add(BuildPhaseRow(
                    "Appear",
                    () => _target.ElementEffects[index].AppearPreset,
                    v => { var e = _target.ElementEffects[index]; e.AppearPreset = v; _target.ElementEffects[index] = e; },
                    () => _target.ElementEffects[index].Appear,
                    v => { var e = _target.ElementEffects[index]; e.Appear = v; _target.ElementEffects[index] = e; }));

                box.Add(BuildPhaseRow(
                    "Idle",
                    () => _target.ElementEffects[index].IdlePreset,
                    v => { var e = _target.ElementEffects[index]; e.IdlePreset = v; _target.ElementEffects[index] = e; },
                    () => _target.ElementEffects[index].Idle,
                    v => { var e = _target.ElementEffects[index]; e.Idle = v; _target.ElementEffects[index] = e; }));

                box.Add(BuildPhaseRow(
                    "Disappear",
                    () => _target.ElementEffects[index].DisappearPreset,
                    v => { var e = _target.ElementEffects[index]; e.DisappearPreset = v; _target.ElementEffects[index] = e; },
                    () => _target.ElementEffects[index].Disappear,
                    v => { var e = _target.ElementEffects[index]; e.Disappear = v; _target.ElementEffects[index] = e; }));

                box.Add(new Button(() => CopyRowToOthers(index)) { text = "この要素の設定を他の要素へコピー", style = { marginTop = 4 } });

                _elementFxContainer.Add(box);
            }
        }

        private VisualElement BuildPhaseRow(
            string label,
            System.Func<UiPresetRef> getPreset, System.Action<UiPresetRef> setPreset,
            System.Func<AssetId<UiTweenMarker>> getId, System.Action<AssetId<UiTweenMarker>> setId)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 2 } };
            row.Add(new Label(label) { style = { width = 70 } });

            var choices = new List<string> { NoneChoice };
            foreach (var name in System.Enum.GetNames(typeof(UiPreset)))
            {
                if (name == nameof(UiPreset.None))
                {
                    continue;
                }

                choices.Add(name);
            }

            foreach (var (choiceLabel, _) in _catalogChoices)
            {
                choices.Add(choiceLabel);
            }

            var currentId = getId();
            var currentPreset = getPreset();
            var currentLabel = NoneChoice;
            if (currentId.IsValid)
            {
                var match = _catalogChoices.Find(c => c.tween != null && c.tween.Id == currentId.Value);
                currentLabel = match.tween != null ? match.label : $"(Id 0x{currentId.Value:X})";
                if (!choices.Contains(currentLabel))
                {
                    choices.Add(currentLabel);
                }
            }
            else if (currentPreset.Preset != UiPreset.None)
            {
                currentLabel = currentPreset.Preset.ToString();
            }

            var startIndex = choices.IndexOf(currentLabel);
            var popup = new PopupField<string>(choices, startIndex >= 0 ? startIndex : 0) { style = { flexGrow = 1f } };
            popup.RegisterValueChangedCallback(evt =>
            {
                if (_target == null)
                {
                    return;
                }

                Undo.RecordObject(_target, "ElementFx: 割当変更");
                var value = evt.newValue;
                if (value == NoneChoice)
                {
                    setPreset(default);
                    setId(default);
                }
                else
                {
                    var match = _catalogChoices.Find(c => c.label == value);
                    if (match.tween != null)
                    {
                        setId(new AssetId<UiTweenMarker>(match.tween.Id, AssetType.UiTween));
                        setPreset(default);
                    }
                    else if (System.Enum.TryParse<UiPreset>(value, out var preset))
                    {
                        setPreset(new UiPresetRef { Preset = preset });
                        setId(default);
                    }
                }

                EditorUtility.SetDirty(_target);
                RebuildElementFxAssignments();
            });
            row.Add(popup);

            var objectField = new ObjectField { objectType = typeof(UiTweenData), style = { width = 160 } };
            objectField.SetValueWithoutNotify(currentId.IsValid ? FindUiTweenData(currentId.Value) : null);
            objectField.RegisterValueChangedCallback(evt =>
            {
                if (_target == null)
                {
                    return;
                }

                Undo.RecordObject(_target, "ElementFx: Tween 直接指定");
                if (evt.newValue is UiTweenData tween && tween.Id != 0)
                {
                    setId(new AssetId<UiTweenMarker>(tween.Id, AssetType.UiTween));
                    setPreset(default);
                }
                else
                {
                    setId(default);
                }

                EditorUtility.SetDirty(_target);
                RebuildElementFxAssignments();
            });
            row.Add(objectField);

            return row;
        }

        private static UiTweenData FindUiTweenData(ulong id)
        {
            if (id == 0)
            {
                return null;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(UiTweenData)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UiTweenData>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.Id == id)
                {
                    return asset;
                }
            }

            return null;
        }

        // 4-10:「この要素の設定を他の要素へコピー」。実体は CanvasElementFxCollector.CopyPhases(配列操作のみ切り出し済み)。
        private void CopyRowToOthers(int index)
        {
            if (_target == null || _target.ElementEffects == null)
            {
                return;
            }

            Undo.RecordObject(_target, "ElementFx: 設定を他の要素へコピー");
            var rows = _target.ElementEffects;
            CanvasElementFxCollector.CopyPhases(ref rows, index);
            _target.ElementEffects = rows;
            EditorUtility.SetDirty(_target);
            RebuildElementFxAssignments();
            _statusLabel.text = "設定を他の要素へコピーしました";
        }

        // 4-9: Prefab 内の全 UiButton へ、選択中のプリセットを AppearPreset として一括設定する(行が無ければ追加する)。
        private void ApplyBulkPresetToButtons()
        {
            if (_target == null || _target.Prefab == null)
            {
                _statusLabel.text = "Prefab を設定してください";
                return;
            }

            var merged = CanvasElementFxCollector.ApplyPresetToButtons(_target.Prefab, _target.ElementEffects, _bulkPreset);

            Undo.RecordObject(_target, "Apply Preset To Buttons");
            _target.ElementEffects = merged;
            EditorUtility.SetDirty(_target);

            SetTarget(_target);
            _statusLabel.text = $"全ボタンの AppearPreset に {_bulkPreset} を設定しました";
        }

        private void EnsurePreviewRoot()
        {
            if (_previewRoot != null)
            {
                return;
            }

            _previewRoot = new GameObject("[D-Drive] Canvas Preview") { hideFlags = HideFlags.DontSave };
            StageUtility.PlaceGameObjectInCurrentStage(_previewRoot);
            _pool.SetInstanceParent(_previewRoot.transform);
        }

        private void PlacePreview()
        {
            if (_target == null)
            {
                _statusLabel.text = "CanvasData を選択してください";
                return;
            }

            RemovePreview();
            EnsurePreviewRoot();
            EditorAnchorRegistry.Refresh(_registry);

            _previewHandle = _manager.OpenData(_target);
            _statusLabel.text = _manager.IsOpen(_previewHandle) ? "プレビュー表示中" : "表示に失敗しました";
            SceneView.RepaintAll();
        }

        private void RemovePreview()
        {
            if (_manager != null && _manager.IsOpen(_previewHandle))
            {
                _manager.StopAll(DDrive.Foundation.Manager.StopReason.Manual);
            }

            _previewHandle = Handle<CanvasMarker>.Invalid;

            if (_previewRoot != null)
            {
                DestroyImmediate(_previewRoot);
            }

            _previewRoot = null;

            if (_statusLabel != null && _target != null)
            {
                _statusLabel.text = "「確認用シーンで開く」で確認できます";
            }

            SceneView.RepaintAll();
        }

        // ── Validation ──

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
            foreach (var result in new CanvasDataValidator().Validate(_target, new ValidationContext(new List<AssetDataBase> { _target })))
            {
                any = true;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                row.Add(new HelpBox(result.Message, ToBoxType(result.Severity)) { style = { flexGrow = 1 } });
                if (result.FixAction != null)
                {
                    var fixAction = result.FixAction;
                    row.Add(new Button(() =>
                    {
                        fixAction();
                        EditorUtility.SetDirty(_target);
                        RefreshValidation();
                    })
                    { text = "修正" });
                }

                _validationFoldout.Add(row);
            }

            if (!any)
            {
                _validationFoldout.Add(new Label("Validation に問題はありません。") { style = { opacity = 0.6f } });
            }
        }

        private static HelpBoxMessageType ToBoxType(ValidationSeverity severity) => severity switch
        {
            ValidationSeverity.Error => HelpBoxMessageType.Error,
            ValidationSeverity.Warning => HelpBoxMessageType.Warning,
            _ => HelpBoxMessageType.Info,
        };
    }
}
