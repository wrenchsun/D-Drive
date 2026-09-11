using System.Collections.Generic;
using DDrive.Editor.Anim;
using DDrive.Editor.Preview;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Anim2D;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anim2D
{
    public sealed partial class Anim2DEditorWindow
    {
        [SerializeField] private Anim2DData _editTarget;

        private SerializedObject _editSerialized;
        private AnimationClip _editClip;
        private Sprite[] _editSprites;
        private float[] _editTimes;
        private PlacementMode _placementMode = PlacementMode.Uniform;

        private ObjectField _editTargetField;
        private Label _editSummaryLabel;
        private Label _previewStatusLabel;
        private Label _eventSummaryLabel;
        private Foldout _validationFoldout;

        // プレビューはウィンドウ内描画ではなく、開いているシーン / プレハブモードに DontSave の SpriteRenderer + Animator を
        // 配置して実 AnimManager(SceneAnimPreviewDriver)で動かし、SceneView で確認する(2026-09-10 決定、全エディタ共通)。
        public const string PreviewObjectName = Anim2DPreviewObject.Name;
        private SceneAnimPreviewDriver _scene;
        private GameObject _previewObject;
        private Handle<AnimMarker> _previewHandle = Handle<AnimMarker>.Invalid;

        public void SetEditTarget(Anim2DData target)
        {
            _editTarget = target;
            LoadEditTarget();
        }

        private void BuildEditSection(VisualElement root)
        {
            root.Add(new Label("編集対象") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });

            _editTargetField = new ObjectField("Anim2DData") { objectType = typeof(Anim2DData), value = _editTarget };
            _editTargetField.RegisterValueChangedCallback(evt =>
            {
                _editTarget = evt.newValue as Anim2DData;
                LoadEditTarget();
            });
            root.Add(_editTargetField);

            var loadButton = new Button(LoadEditTarget) { text = "読み込み" };
            root.Add(loadButton);

            _editSummaryLabel = new Label("(未読み込み)") { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4 } };
            root.Add(_editSummaryLabel);

            root.Add(new Label("プレビュー(SceneView で確認)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
            var previewButtons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            previewButtons.Add(new Button(OpenPreviewScene) { text = "確認用シーンを開く", tooltip = "確認用シーンを開き(無ければ生成)、SpriteRenderer 付きのプレビュー物を配置する" });
            previewButtons.Add(new Button(PlacePreview) { text = "今のシーンに配置", tooltip = "開いているシーン / プレハブモードに保存されないプレビュー物を置く" });
            previewButtons.Add(new Button(PlayPreview) { text = "▶ 再生" });
            previewButtons.Add(new Button(StopPreview) { text = "■ 停止" });
            previewButtons.Add(new Button(RemovePreview) { text = "撤去" });
            root.Add(previewButtons);
            _previewStatusLabel = new Label("プレビュー物は未配置") { style = { marginLeft = 4, whiteSpace = WhiteSpace.Normal } };
            root.Add(_previewStatusLabel);

            root.Add(new Label("イベント / 同時プレビュー") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
            var openAnimEditorButton = new Button(() =>
            {
                if (_editTarget != null)
                {
                    // プレビュー物はシーンに残したまま(スプライトが消えないように)、こちらの再生とドライバの対象だけ手放して
                    // Anim Editor に引き渡す(Anim Editor は開いた時点で同名のプレビュー物を引き取る。2026-09-11)
                    StopPreview();
                    _scene?.ReleaseTarget();
                    AnimEditorWindow.Open(_editTarget);
                }
            })
            {
                text = "Anim Editor で開く(イベント D&D・SE/VFX 同時再生)",
                tooltip = "Frame/Time イベントの編集・タイムライン上でのマーカー D&D・SE/VFX/配置セットの同時プレビューは Anim2DData も AnimData なので共通の Anim Editor をそのまま使う",
            };
            root.Add(openAnimEditorButton);
            _eventSummaryLabel = new Label { style = { opacity = 0.75f, marginLeft = 4, whiteSpace = WhiteSpace.Normal } };
            root.Add(_eventSummaryLabel);

            root.Add(new Label("リタイミング") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });

            var placementField = new EnumField("配置モード", _placementMode);
            placementField.RegisterValueChangedCallback(evt => _placementMode = (PlacementMode)evt.newValue);
            root.Add(placementField);

            // Anim2DData.Retiming(ValueDef)を SerializedObject 経由でバインドする(ValueDefDrawer を流用)。
            var retimingField = new PropertyField();
            retimingField.RegisterCallback<AttachToPanelEvent>(_ => BindRetimingField(retimingField));
            root.Add(retimingField);

            var applyButton = new Button(ApplyRetiming) { text = "適用(Clip に焼き込む)", style = { marginTop = 10, height = 28 } };
            root.Add(applyButton);

            RefreshEventSummary();
        }

        // 検証パネル: Create / Edit どちらのモードでも見えるよう、モード切替のトグル対象外(共通ルート)に置く。
        private void BuildValidationSection(VisualElement root)
        {
            _validationFoldout = new Foldout { text = "検証", value = true, style = { marginTop = 8 } };
            root.Add(_validationFoldout);
            RefreshValidation();
        }

        private void BindRetimingField(PropertyField field)
        {
            if (_editSerialized == null)
            {
                return;
            }

            var prop = _editSerialized.FindProperty("Retiming");
            if (prop != null)
            {
                field.BindProperty(prop);
            }
        }

        private void LoadEditTarget()
        {
            if (_editTarget == null)
            {
                _editSummaryLabel.text = "(未読み込み)";
                _editClip = null;
                _editSprites = null;
                _editTimes = null;
                return;
            }

            _editSerialized = new SerializedObject(_editTarget);
            _editClip = _editTarget.Clip;

            if (_editClip == null || !AnimationClipEditorUtility.LoadSprites(_editClip, out _editSprites, out _editTimes))
            {
                _editSummaryLabel.text = "Clip が未設定、または Sprite キーフレームを読み込めませんでした。";
                return;
            }

            _editSummaryLabel.text = $"{_editClip.name}: {_editSprites.Length} 枚 / {_editClip.length:0.###} 秒 / {_editClip.frameRate} fps";
            if (_previewObject != null)
            {
                ApplyFirstFrame();
            }

            RefreshEventSummary();
            RefreshValidation();
        }

        // Anim Editor で編集するイベント(基底 AnimData.Events)の件数だけをここに要約する(編集そのものは Anim Editor で行う)。
        private void RefreshEventSummary()
        {
            if (_eventSummaryLabel == null)
            {
                return;
            }

            var events = _editTarget != null ? _editTarget.Events : null;
            if (events == null || events.Length == 0)
            {
                _eventSummaryLabel.text = "イベント: なし。「Anim Editor で開く」から追加できます。";
                return;
            }

            var frame = 0;
            var time = 0;
            var playAsset = 0;
            foreach (var e in events)
            {
                switch (e.Trigger)
                {
                    case EventTrigger.Frame: frame++; break;
                    case EventTrigger.Time: time++; break;
                }

                if (e.Action == EventAction.PlayAsset)
                {
                    playAsset++;
                }
            }

            _eventSummaryLabel.text = $"イベント: {events.Length} 件(Frame {frame} / Time {time} / PlayAsset(SE・VFX 等) {playAsset})";
        }

        // Anim2DDataValidator(方向・Clip 未生成等) + AnimDataValidator(Clip/StateName/イベント範囲等) +
        // Anim2DEditorValidator(Controller の x,y パラメータ・スライス済みスプライトの参照切れ)をまとめて表示する。
        private void RefreshValidation()
        {
            if (_validationFoldout == null)
            {
                return;
            }

            _validationFoldout.Clear();
            if (_editTarget == null)
            {
                _validationFoldout.Add(new Label("対象を読み込むと検証結果が出ます。") { style = { opacity = 0.6f } });
                return;
            }

            var ctx = new ValidationContext(new List<AssetDataBase> { _editTarget });
            var results = new List<ValidationResult>();
            results.AddRange(new Anim2DDataValidator().Validate(_editTarget, ctx));
            results.AddRange(new AnimDataValidator().Validate(_editTarget, ctx));
            results.AddRange(new Anim2DEditorValidator().Validate(_editTarget, ctx));

            if (results.Count == 0)
            {
                _validationFoldout.Add(new Label("Validation に問題はありません。") { style = { opacity = 0.6f } });
                return;
            }

            foreach (var result in results)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 2 } };
                var type = result.Severity switch
                {
                    ValidationSeverity.Error => HelpBoxMessageType.Error,
                    ValidationSeverity.Warning => HelpBoxMessageType.Warning,
                    _ => HelpBoxMessageType.Info,
                };
                row.Add(new HelpBox(result.Message, type) { style = { flexGrow = 1f } });

                if (result.FixAction != null)
                {
                    var fix = result.FixAction;
                    row.Add(new Button(() =>
                    {
                        fix();
                        AssetDatabase.SaveAssets();
                        RefreshValidation();
                    })
                    { text = "修正" });
                }

                _validationFoldout.Add(row);
            }
        }

        private void ApplyRetiming()
        {
            if (_editTarget == null || _editClip == null || _editSprites == null || _editSprites.Length == 0)
            {
                Debug.LogError("[Anim2DEditorWindow] 編集対象が読み込まれていません。");
                return;
            }

            var totalSeconds = _editClip.length > 0f ? _editClip.length : _editSprites.Length / Mathf.Max(1f, _editClip.frameRate);
            var times = AnimationClipEditorUtility.BuildTimes(_editSprites.Length, _placementMode, _editTarget.Retiming);

            Undo.RecordObject(_editClip, "Anim2D Retiming");
            Undo.RecordObject(_editTarget, "Anim2D Retiming");

            if (!AnimationClipEditorUtility.RebuildClip(_editClip, _editSprites, times, totalSeconds))
            {
                Debug.LogError("[Anim2DEditorWindow] Clip の再構築に失敗しました。");
                return;
            }

            _editTimes = times;

            // 方向 Clip(4 / 8 方向)にも同じ配置をまとめて適用する(OH_CASE2026_ITAMI の BlendTree 一括リタイミング相当、2026-09-11)。
            var applied = Anim2DRetiming.ApplyToDirectionClips(_editTarget, _placementMode, totalSeconds, _editSprites.Length, out var skipped);

            EditorUtility.SetDirty(_editTarget);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Anim2DEditorWindow] {_editClip.name} のリタイミングを適用しました({_editSprites.Length} 枚)。方向 Clip: 適用 {applied} / スキップ {skipped}(枚数が違う・Sprite キー無し)。");
            RefreshValidation();
        }

        // ── シーンプレビュー ──

        private void OpenPreviewScene()
        {
            StopPreview();
            VfxPreviewSceneSetup.OpenOrCreate();
            PlacePreview();
        }

        private void PlacePreview()
        {
            if (_editTarget == null)
            {
                _previewStatusLabel.text = "編集対象の Anim2DData を読み込んでください";
                return;
            }

            _scene ??= new SceneAnimPreviewDriver();
            if (_previewObject == null)
            {
                _previewObject = Anim2DPreviewObject.Create(_editTarget);
            }

            ApplyFirstFrame();
            _scene.SetTarget(_previewObject.GetComponent<Animator>());
            _previewStatusLabel.text = "プレビュー物を配置済み(SceneView を確認)";
            SceneView.RepaintAll();
        }

        private void ApplyFirstFrame()
        {
            if (_previewObject == null || _editSprites == null || _editSprites.Length == 0)
            {
                return;
            }

            _previewObject.GetComponent<SpriteRenderer>().sprite = _editSprites[0];
        }

        private void PlayPreview()
        {
            if (_editTarget == null || _editTarget.Clip == null)
            {
                _previewStatusLabel.text = "Clip のある Anim2DData を読み込んでください";
                return;
            }

            if (_previewObject == null)
            {
                PlacePreview();
            }

            if (_previewObject == null)
            {
                return;
            }

            _scene.StopSpawned();
            _previewHandle = _scene.Play(_editTarget, _previewObject.GetComponent<Animator>(), 0f);
            _previewStatusLabel.text = _scene.Manager.IsPlaying(_previewHandle) ? "● 再生中(SceneView)" : "再生できません";
        }

        private void StopPreview()
        {
            _scene?.Stop();
            _previewHandle = Handle<AnimMarker>.Invalid;
            if (_previewStatusLabel != null && _previewObject != null)
            {
                _previewStatusLabel.text = "■ 停止";
            }
        }

        private void RemovePreview()
        {
            StopPreview();
            _scene?.ReleaseTarget();
            Anim2DPreviewObject.Destroy(_previewObject);
            _previewObject = null;
            if (_previewStatusLabel != null)
            {
                _previewStatusLabel.text = "プレビュー物は未配置";
            }

            SceneView.RepaintAll();
        }

        private void DisposeScenePreview()
        {
            RemovePreview();
            _scene?.Dispose();
            _scene = null;
        }

        // 再生中はウィンドウのステータスだけ追従させる(描画はシーン側)。
        private void OnEditorUpdatePreview()
        {
            if (_scene == null || _previewStatusLabel == null || !_scene.Manager.IsPlaying(_previewHandle))
            {
                return;
            }

            var t = _scene.Manager.GetNormalizedTime(_previewHandle);
            var status = $"● 再生中 {t:P0}(周回 {_scene.Manager.GetLoopCount(_previewHandle)})";
            if (_previewStatusLabel.text != status)
            {
                _previewStatusLabel.text = status;
            }
        }
    }
}
