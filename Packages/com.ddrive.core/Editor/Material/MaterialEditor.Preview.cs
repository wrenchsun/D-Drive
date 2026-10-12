using DDrive.Editor.Anim;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Materials
{
    // 「確認する」の欄(docs/1010 UX-8): 実 MaterialManager が作った共有 Material を、
    //   ① この窓のサムネイル(PreviewRenderUtility。docs/09 §2 の Material の例外。ADR-4 の SceneView 確認は ② が担う)
    //   ② 開いているシーンの球 / 板 / Cube / 任意の ModelData(保存されない DontSave。確認用シーン or 今のシーン)
    // で見る。Data の編集(つまみ・欄・Undo・Inspector)は EditorUtility.GetDirtyCount の変化で拾い、共有 Material を間引いて作り直す。
    internal sealed partial class MaterialEditor
    {
        private const float CompareOffsetX = 1.5f;
        private const double AutoRebuildIntervalSec = 0.1;
        private const double ThumbnailIntervalSec = 1.0 / 30.0; // 描き直しの最短間隔
        private const float TurntableSpeedDegPerSec = 45f;

        private enum CompareMode { SideBySide, Toggle }

        private GameObject _previewRoot;
        private MaterialPreviewBuilder.Preview _primaryPreview;
        private MaterialPreviewBuilder.Preview _comparePreview;
        private double _lastTickTime;
        private Light _rotatedLight;
        private Quaternion _originalLightRotation;

        private bool _previewDirty;
        private double _lastAutoRebuildTime;
        private int _lastDirtyCount;
        private bool _registryDirty; // Registry(TextureData 等の ID 解決)の再走査が必要か。projectChanged で立てる

        private MaterialThumbnailRenderer _thumbnail;
        private MaterialThumbnailRenderer _compareThumbnail;
        private bool _thumbnailDirty;
        private double _lastThumbnailTime;
        private Image _thumbnailImage;
        private Image _compareThumbnailImage;
        private Label _thumbnailCaptionA;
        private Label _thumbnailCaptionB;
        private Label _thumbnailLabel;
        private VisualElement _compareColumn;
        private VisualElement _compareControls;
        private Button _compareModeButton;
        private Button _abButton;
        private ObjectField _modelField;
        private VisualElement _modelLine;
        private Slider _lightRotationSlider;
        private Label _sceneStatus;

        // ── 欄 ──

        private UxRow CreateThumbnailRow(FieldGuideEntry e)
        {
            var box = new VisualElement();

            var area = new VisualElement();
            area.AddToClassList("ux-mat-thumbs");
            var colA = new VisualElement();
            colA.AddToClassList("ux-mat-col");
            _thumbnailImage = CreateThumbnailImage();
            colA.Add(_thumbnailImage);
            _thumbnailCaptionA = new Label();
            _thumbnailCaptionA.AddToClassList("ux-mat-cap");
            colA.Add(_thumbnailCaptionA);
            area.Add(colA);

            _compareColumn = new VisualElement();
            _compareColumn.AddToClassList("ux-mat-col");
            _compareThumbnailImage = CreateThumbnailImage();
            _compareColumn.Add(_compareThumbnailImage);
            _thumbnailCaptionB = new Label();
            _thumbnailCaptionB.AddToClassList("ux-mat-cap");
            _compareColumn.Add(_thumbnailCaptionB);
            _compareColumn.Show(false);
            area.Add(_compareColumn);
            box.Add(area);

            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            line.Add(InvertToggle("X 反転", "横ドラッグの回転方向を反転する", () => MaterialThumbnailRenderer.InvertDragX, v => MaterialThumbnailRenderer.InvertDragX = v));
            line.Add(InvertToggle("Y 反転", "縦ドラッグの傾き方向を反転する", () => MaterialThumbnailRenderer.InvertDragY, v => MaterialThumbnailRenderer.InvertDragY = v));
            line.Add(MakeButton("ポップアップ", null, PopOutThumbnail, "サムネイルを独立したウィンドウ(Material プレビュー)に出す。形状・回転・ライトの設定を引き継ぐ"));
            box.Add(line);

            _thumbnailLabel = new Label();
            _thumbnailLabel.AddToClassList("ux-dim");
            _thumbnailLabel.AddToClassList("ux-note");
            box.Add(_thumbnailLabel);
            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        // 形状 / モデル / ターンテーブル / ライト回転。
        private UxRow CreateShapeRow(FieldGuideEntry e)
        {
            var box = new VisualElement();

            var shape = new EnumField(_shape) { tooltip = "球 / 板(Quad) / Cube / 任意 ModelData" };
            shape.RegisterValueChangedCallback(evt =>
            {
                _shape = (MaterialPreviewShape)evt.newValue;
                _modelLine?.Show(_shape == MaterialPreviewShape.Model);
                _thumbnailDirty = true;
            });
            box.Add(Line("形状", shape));

            _modelField = new ObjectField { objectType = typeof(ModelData), allowSceneObjects = false };
            _modelField.SetValueWithoutNotify(_previewModel);
            _modelField.RegisterValueChangedCallback(evt => _previewModel = evt.newValue as ModelData);
            _modelLine = Line("プレビュー用モデル", _modelField);
            _modelLine.Show(_shape == MaterialPreviewShape.Model);
            box.Add(_modelLine);

            var turntable = new Toggle { text = "ターンテーブル", tooltip = "サムネイルと配置したプレビューを Y 軸で回す" };
            turntable.SetValueWithoutNotify(_turntableEnabled);
            turntable.RegisterValueChangedCallback(evt => _turntableEnabled = evt.newValue);
            box.Add(Line(string.Empty, turntable));

            _lightRotationSlider = new Slider(0f, 360f) { showInputField = true, tooltip = "サムネイルのライトと、シーンの最初の Directional Light を Y 軸で回す(無ければサムネイルのみ)" };
            _lightRotationSlider.SetValueWithoutNotify(_thumbnailLightDeg);
            _lightRotationSlider.RegisterValueChangedCallback(evt =>
            {
                _thumbnailLightDeg = evt.newValue;
                _thumbnailDirty = true;
                ApplyLightRotation(evt.newValue);
                SceneView.RepaintAll();
            });
            box.Add(Line("ライト回転", _lightRotationSlider));

            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        private UxRow CreateCompareRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            var field = new ObjectField { objectType = typeof(MaterialData), allowSceneObjects = false };
            field.SetValueWithoutNotify(_compareTarget);
            field.RegisterValueChangedCallback(evt =>
            {
                _compareTarget = evt.newValue as MaterialData;
                UpdateCompareUi();
            });
            box.Add(Line("比較対象", field));

            _compareControls = new VisualElement();
            _compareControls.AddToClassList("ux-toolsline");
            _compareModeButton = MakeButton("左右", null, ToggleCompareMode, "比較の見せ方: 左右(2 分割) ⇄ 切替(1 枚を A/B で切替)");
            _compareControls.Add(_compareModeButton);
            _abButton = MakeButton("A → B", null, ToggleAB, "表示する方を切り替える(A = 対象、B = 比較対象)");
            _compareControls.Add(_abButton);
            _compareControls.Add(MakeButton("並べて比較", null, CompareSideBySide, "対象の隣(X+1.5)に比較対象の Material を適用したプレビューをシーンに並べる"));
            _compareControls.Show(false);
            box.Add(_compareControls);
            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        private UxRow CreateSceneRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            var scene = PreviewPlacementButton.Create(
                "確認用シーン",
                "確認用シーンを開き(無ければ生成)、選んだ形に Material を貼って置く。右クリックで、いま開いているシーンへ配置",
                OpenPreviewScene);
            scene.AddToClassList("ux-btn");
            line.Add(scene);
            line.Add(MakeButton("今のシーンに配置", null, PlacePreview, "開いているシーンに選んだ形(保存されない)を置き、生成した共有 Material を適用する"));
            line.Add(MakeButton("再生成", null, RebuildPreview, "Data の変更を共有 Material に反映し直す(比較用も含む)"));
            line.Add(MakeButton("撤去", null, RemovePreview, "置いたプレビューをシーンから消す"));
            box.Add(line);
            _sceneStatus = new Label(StatusText());
            _sceneStatus.AddToClassList("ux-dim");
            _sceneStatus.AddToClassList("ux-note");
            box.Add(_sceneStatus);
            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        private string StatusText() => _primaryPreview != null
            ? (_comparePreview != null ? "プレビューに適用中(比較対象あり)" : "プレビューに適用中")
            : "「確認用シーン」「今のシーンに配置」で SceneView に置いて確認できます";

        // 回転方向の反転(EditorPrefs。ポップアップと共有)。
        private static Toggle InvertToggle(string text, string tooltip, System.Func<bool> get, System.Action<bool> set)
        {
            var t = new Toggle { text = text, tooltip = tooltip };
            t.AddToClassList("ux-mat-toggle");
            t.SetValueWithoutNotify(get());
            t.RegisterValueChangedCallback(evt => set(evt.newValue));
            return t;
        }

        // ラベル + 部品の 1 行。
        private static VisualElement Line(string label, VisualElement control)
        {
            var line = new VisualElement();
            line.AddToClassList("ux-mat-ctrl");
            var l = new Label(label);
            l.AddToClassList("ux-mat-ctrl__label");
            line.Add(l);
            control.AddToClassList("ux-mat-ctrl__input");
            line.Add(control);
            return line;
        }

        // 両サムネイル共通の Image(ドラッグ回転は共有の角度を動かす)。
        private Image CreateThumbnailImage()
        {
            var image = new Image { scaleMode = ScaleMode.ScaleToFit };
            image.AddToClassList("ux-mat-thumb");
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
            _compareMode = _compareMode == CompareMode.SideBySide ? CompareMode.Toggle : CompareMode.SideBySide;
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

            var hasCompare = _compareTarget != null && Target != null;
            _compareControls.Show(hasCompare);
            _compareColumn?.Show(hasCompare && _compareMode == CompareMode.SideBySide);
            if (_compareModeButton != null)
            {
                _compareModeButton.text = _compareMode == CompareMode.SideBySide ? "左右" : "切替";
            }

            if (_abButton != null)
            {
                _abButton.Show(hasCompare && _compareMode == CompareMode.Toggle);
                _abButton.text = _showCompareInToggle ? "B → A" : "A → B";
            }

            _thumbnailDirty = true;
        }

        // ── Registry・Data の変更の取り込み ──

        private void OnProjectChanged() => _registryDirty = true;

        // アセットの追加・削除後(例: Maya インポートで TextureData が増えた)に Registry を再走査し、古い ID 解決で作った
        // 共有 Material を捨てる。描画・再生成の直前に必ず通す。
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

        // 比較対象(B)の変更(Inspector / Undo)を拾う。
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
            SetLabel(_sceneStatus, StatusText());
        }

        // ── シーンへの配置 ──

        private void EnsurePreviewRoot()
        {
            if (_previewRoot != null)
            {
                return;
            }

            _previewRoot = new GameObject(PreviewRootName) { hideFlags = HideFlags.DontSave };
            UnityEditor.SceneManagement.StageUtility.PlaceGameObjectInCurrentStage(_previewRoot);
            _pool.SetInstanceParent(_previewRoot.transform);
        }

        // 配置のたびに「今見ている SceneView の視点中心」へ置き、そこへ SceneView を寄せる。
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

        // 左クリック = 確認用シーンを開いて配置 / 右クリック = このシーンに配置・本配置(本配置は共有 Material がシーンに保存されないので一時配置にする)。
        private void OpenPreviewScene(PreviewPlaceMode mode)
        {
            if (!PreviewPlacement.PrepareScene(mode, VfxPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            PlacePreview();
            if (PreviewPlacement.IsPersistent(mode))
            {
                SetLabel(_sceneStatus, "Material のプレビューは共有 Material を使うためシーンに保存できません。一時配置にしました");
            }
        }

        private void PlacePreview()
        {
            if (Target == null)
            {
                SetLabel(_sceneStatus, "MaterialData を選択してください");
                return;
            }

            if (_manager == null)
            {
                return;
            }

            if (_shape == MaterialPreviewShape.Model && _previewModel == null)
            {
                SetLabel(_sceneStatus, "プレビュー用の ModelData を選択してください");
                return;
            }

            DeselectPreviewObjects();
            _primaryPreview?.Dispose();
            _comparePreview?.Dispose();
            EnsurePreviewRoot();
            EnsureRegistryFresh();

            _primaryPreview = MaterialPreviewBuilder.Create(_shape, _previewModel, Target, _manager, _previewRoot.transform, Vector3.zero, _modelsManager, "Primary");
            if (_compareTarget != null)
            {
                _comparePreview = MaterialPreviewBuilder.Create(_shape, _previewModel, _compareTarget, _manager, _previewRoot.transform,
                    new Vector3(CompareOffsetX, 0f, 0f), _modelsManager, "Compare");
            }

            SetLabel(_sceneStatus, _primaryPreview != null
                ? StatusText()
                : "プレビューの生成に失敗しました(ModelData の Prefab を確認してください)");
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
                SetLabel(_sceneStatus, "比較対象の MaterialData を選択してください");
                return;
            }

            PlacePreview();
        }

        private void RebuildPreview()
        {
            if (Target == null || _manager == null)
            {
                return;
            }

            _manager.Clear();
            // Registry の再走査(全 Data の FindAssets + 同期ロード)は重いので、アセットの追加・削除があったときだけ行う。
            EnsureRegistryFresh();

            if (_primaryPreview != null)
            {
                MaterialPreviewBuilder.Apply(_primaryPreview, _shape, _previewModel, Target, _manager);
            }

            if (_comparePreview != null && _compareTarget != null)
            {
                MaterialPreviewBuilder.Apply(_comparePreview, _shape, _previewModel, _compareTarget, _manager);
            }

            _thumbnailDirty = true;
            SceneView.RepaintAll();
        }

        // プレビュー物を Inspector で選択したまま破棄すると GameObjectInspector が MissingReference を出すので、先に選択を外す。
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
            SetLabel(_sceneStatus, StatusText());
            SceneView.RepaintAll();
        }

        // ── ライト回転 ──

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

        // ── 毎フレームの更新(MaterialAnim / ターンテーブル / サムネイル) ──

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

            // Data の編集(つまみ・欄・Undo・Inspector)は dirty カウントの変化で拾う。
            if (Target != null)
            {
                var dirty = EditorUtility.GetDirtyCount(Target);
                if (dirty != _lastDirtyCount)
                {
                    _lastDirtyCount = dirty;
                    _previewDirty = true;
                }
            }

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
                _thumbnailAngle = (_thumbnailAngle + TurntableSpeedDegPerSec * dt) % 360f;
                _thumbnailDirty = true;
                if (_primaryPreview?.Root != null)
                {
                    _primaryPreview.Root.transform.Rotate(Vector3.up, TurntableSpeedDegPerSec * dt, Space.World);
                }

                if (_comparePreview?.Root != null)
                {
                    _comparePreview.Root.transform.Rotate(Vector3.up, TurntableSpeedDegPerSec * dt, Space.World);
                }

                SceneView.RepaintAll();
            }

            // MaterialAnim の再生。非フォーカスかつターンテーブル off のときは描き直さない。
            if (Target != null && Target.HasAnims && (hasFocus || _turntableEnabled))
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
            if (Target == null)
            {
                return;
            }

            MaterialThumbnailWindow.Open(Target, _shape, _turntableEnabled, _lightRotationSlider?.value ?? 0f);
        }

        // 共有 Material(実 Manager 生成)を PreviewRenderUtility で描いて Image に出す。Model 形状はシーン配置へ誘導。
        private void RenderThumbnail()
        {
            if (_thumbnailImage == null || Target == null || _manager == null)
            {
                return;
            }

            if (_shape == MaterialPreviewShape.Model)
            {
                _thumbnailImage.image = null;
                _compareThumbnailImage.image = null;
                SetLabel(_thumbnailCaptionA, string.Empty);
                SetLabel(_thumbnailCaptionB, string.Empty);
                SetLabel(_thumbnailLabel, "Model 形状はサムネイルに出しません。「確認用シーン」「今のシーンに配置」で確認してください");
                return;
            }

            var hasCompare = _compareTarget != null;
            var sideBySide = hasCompare && _compareMode == CompareMode.SideBySide;
            var showB = hasCompare && _compareMode == CompareMode.Toggle && _showCompareInToggle;

            EnsureRegistryFresh();
            _thumbnail ??= new MaterialThumbnailRenderer();
            var primary = showB ? _compareTarget : Target;
            RenderInto(_thumbnail, _thumbnailImage, primary);
            SetLabel(_thumbnailCaptionA, hasCompare ? (showB ? "B: " : "A: ") + DisplayNameOf(primary) : string.Empty);

            if (sideBySide)
            {
                _compareThumbnail ??= new MaterialThumbnailRenderer();
                RenderInto(_compareThumbnail, _compareThumbnailImage, _compareTarget);
                SetLabel(_thumbnailCaptionB, "B: " + DisplayNameOf(_compareTarget));
            }

            SetLabel(_thumbnailLabel, hasCompare
                ? "比較: A = 対象 / B = 比較対象(回転・ライトは共通)。既定ライトのみ"
                : "サムネイル: 既定ライトのみ(実シーン照明・モデル適用は「確認用シーン」「今のシーンに配置」で確認)");
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

        private const int ThumbnailHeight = 220; // ux-mat-thumb の高さと同じ
    }
}
