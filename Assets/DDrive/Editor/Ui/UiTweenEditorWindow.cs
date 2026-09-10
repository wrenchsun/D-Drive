using System.Collections.Generic;
using DDrive.Editor.Menu;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace DDrive.Editor.Ui
{
    // [15_ui_interaction.md] B-6 — UiTweenData 専用エディタ(チケット 4-8 の最小実装。カーブ/スプラインの
    // ハンドル編集や実寸プレビューギャラリーは 4-10 で拡張する)。プロジェクト方針(2026-09-10)により
    // ウィンドウ内には何も描画しない。SerializedObject をそのまま InspectorElement で表示し、確認は
    // 「確認用シーンに配置」で開いているシーン/Game ビュー側に実 Manager(UiTweenManager)を駆動して出す(ADR-4)。
    [DDrive.Editor.Inspector.DataEditor(typeof(UiTweenData), "UI Tween Editor で開く")]
    public sealed class UiTweenEditorWindow : EditorWindow
    {
        private const string PreviewRootName = "[D-Drive] Ui Preview";

        [SerializeField] private UiTweenData _target;
        [SerializeField] private UiPreset _preset;

        private ObjectField _targetField;
        private VisualElement _inspectorContainer;
        private VisualElement _validationContainer;
        private Label _statusLabel;

        private UiTweenManager _previewManager;
        private RectTransform _previewTarget;
        private DDrive.Foundation.Handle.Handle<UiTweenMarker> _previewHandle;
        private double _lastEditorTime;

        [MenuItem(DDriveMenu.Editors + "UI Tween")]
        public static void OpenFromMenu() => Open(Selection.activeObject as UiTweenData);

        public static void Open(UiTweenData target)
        {
            var window = GetWindow<UiTweenEditorWindow>("UI Tween Editor");
            window.minSize = new Vector2(380, 360);
            if (target != null)
            {
                window.SetTarget(target);
            }
        }

        private void CreateGUI()
        {
            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(scrollView);
            scrollView.style.paddingLeft = 6;
            scrollView.style.paddingRight = 6;
            scrollView.style.paddingTop = 6;

            _targetField = new ObjectField("対象 UiTweenData") { objectType = typeof(UiTweenData) };
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as UiTweenData));
            scrollView.Add(_targetField);

            var presetRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6, alignItems = Align.Center } };
            var presetField = new EnumField("プリセット", _preset);
            presetField.style.flexGrow = 1f;
            presetField.RegisterValueChangedCallback(evt => _preset = (UiPreset)evt.newValue);
            presetRow.Add(presetField);
            presetRow.Add(new Button(GenerateFromPreset) { text = "プリセットから Tracks を生成" });
            scrollView.Add(presetRow);

            var sceneRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, marginBottom = 4 } };
            sceneRow.Add(new Button(PlaceInScene) { text = "確認用シーンに配置" });
            sceneRow.Add(new Button(Play) { text = "▶ 再生" });
            sceneRow.Add(new Button(Stop) { text = "■ 停止" });
            sceneRow.Add(new Button(RemoveFromScene) { text = "撤去" });
            scrollView.Add(sceneRow);

            _statusLabel = new Label();
            scrollView.Add(_statusLabel);

            _inspectorContainer = new VisualElement();
            scrollView.Add(_inspectorContainer);

            _validationContainer = new VisualElement { style = { marginTop = 8 } };
            scrollView.Add(_validationContainer);

            if (_target == null && Selection.activeObject is UiTweenData selected)
            {
                SetTarget(selected);
            }
            else
            {
                _targetField.SetValueWithoutNotify(_target);
                RebuildInspector();
                RebuildValidation();
            }

            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDestroy()
        {
            EditorApplication.update -= OnEditorUpdate;
            RemoveFromScene();
        }

        private void SetTarget(UiTweenData target)
        {
            _target = target;
            _targetField?.SetValueWithoutNotify(_target);
            RebuildInspector();
            RebuildValidation();
        }

        private void RebuildInspector()
        {
            if (_inspectorContainer == null)
            {
                return;
            }

            _inspectorContainer.Clear();
            if (_target == null)
            {
                _inspectorContainer.Add(new Label("UiTweenData を選択してください"));
                return;
            }

            var so = new SerializedObject(_target);
            _inspectorContainer.Add(new InspectorElement(so));
        }

        private void RebuildValidation()
        {
            if (_validationContainer == null)
            {
                return;
            }

            _validationContainer.Clear();
            if (_target == null)
            {
                return;
            }

            _validationContainer.Add(new Label("Validation") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            var validator = new UiTweenDataValidator();
            var ctx = new ValidationContext(new List<DDrive.Foundation.Data.AssetDataBase>());
            var any = false;
            foreach (var result in validator.Validate(_target, ctx))
            {
                any = true;
                _validationContainer.Add(new Label($"[{result.Severity}] {result.Message}"));
            }

            if (!any)
            {
                _validationContainer.Add(new Label("問題なし"));
            }
        }

        // Data 自体は編集しない。プリセットの展開結果を Tracks へ書き込むだけ(Undo 対応)。
        private void GenerateFromPreset()
        {
            if (_target == null)
            {
                return;
            }

            var target = _previewTarget != null ? _previewTarget : CreateScratchRect();
            var buffer = new TweenTrack[UiTweenManager.MaxTracksPerTween];
            var refValue = new UiPresetRef { Preset = _preset };
            var count = UiPresetFactory.Build(in refValue, target, buffer);

            Undo.RecordObject(_target, "UiTweenData: プリセットから Tracks を生成");
            var tracks = new TweenTrack[count];
            System.Array.Copy(buffer, tracks, count);
            _target.Tracks = tracks;
            EditorUtility.SetDirty(_target);

            if (target != _previewTarget)
            {
                Object.DestroyImmediate(target.gameObject);
            }

            RebuildInspector();
            RebuildValidation();
        }

        private static RectTransform CreateScratchRect()
        {
            var go = new GameObject("ScratchRect", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(200f, 80f);
            return rt;
        }

        private void PlaceInScene()
        {
            RemoveFromScene();

            var canvasGo = new GameObject(PreviewRootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
            {
                hideFlags = HideFlags.DontSave,
            };
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var imageGo = new GameObject("PreviewImage", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            imageGo.transform.SetParent(canvasGo.transform, false);
            _previewTarget = (RectTransform)imageGo.transform;
            _previewTarget.sizeDelta = new Vector2(200f, 80f);

            Selection.activeGameObject = imageGo;
            _statusLabel.text = "確認用シーンに配置しました";
        }

        private void RemoveFromScene()
        {
            Stop();
            var existing = GameObject.Find(PreviewRootName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            _previewTarget = null;
        }

        // ADR-4: プレビューは実 Manager(UiTweenManager)を EditorApplication.update から駆動する。
        // EditorWindow 内で自前描画はしない。
        private void Play()
        {
            if (_target == null || _previewTarget == null)
            {
                _statusLabel.text = "先に「確認用シーンに配置」してください";
                return;
            }

            _previewManager ??= new UiTweenManager(EditorAnchorRegistryFallback());
            _previewHandle = _previewManager.PlayData(_target, _previewTarget);
            _lastEditorTime = EditorApplication.timeSinceStartup;
            _statusLabel.text = "再生中";
        }

        private void Stop()
        {
            if (_previewManager != null)
            {
                _previewManager.Stop(_previewHandle);
            }

            if (_statusLabel != null)
            {
                _statusLabel.text = "停止";
            }
        }

        private void OnEditorUpdate()
        {
            if (_previewManager == null || _previewTarget == null)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var dt = (float)(now - _lastEditorTime);
            _lastEditorTime = now;
            _previewManager.Tick(dt);

            if (_statusLabel != null && !_previewManager.IsPlaying(_previewHandle))
            {
                _statusLabel.text = "完了";
            }
        }

        // UiTweenData は AssetId を引かないため、プレビュー用 Registry は Placeholder 解決さえできれば十分。
        private static DDrive.Foundation.Registry.IAssetRegistry EditorAnchorRegistryFallback()
            => DDrive.Editor.Preview.EditorAnchorRegistry.Build();
    }
}
