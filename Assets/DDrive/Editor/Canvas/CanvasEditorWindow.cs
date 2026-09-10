using System.Collections.Generic;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
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
        private Label _statusLabel;
        private VisualElement _validationFoldout;

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
                return;
            }

            var so = new SerializedObject(target);
            _inspectorContainer.Add(new InspectorElement(so));
            _inspectorContainer.Bind(so);

            _statusLabel.text = _manager != null && _manager.IsOpen(_previewHandle) ? "プレビュー表示中" : "「確認用シーンで開く」で確認できます";
            RefreshValidation();
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
