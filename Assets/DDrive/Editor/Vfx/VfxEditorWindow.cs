using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Foundation.Handle;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DDrive.Editor.Vfx
{
    // [04_vfx.md] §5(2026-07-28 改定 / 2026-09-08 使い勝手改修 [19_vfx_usability_review.md]) — VFX 専用エディタ(2-4/2-12)。
    // プレビューは独自ビューポートではなく「開いているシーンへ直接スポーン → SceneView で確認」方式。
    // ライティング/ポストプロセス/Skybox はシーン側の設定がそのまま適用されるため、
    // 確認専用シーンを開いた状態で調整する運用を想定する(SceneVfxPreviewDriver 参照)。
    //
    // 本ファイル: 対象管理 / 再生制御 / 基本設定 / ライフサイクル。
    // VfxEditorWindow.Anchor.cs: Anchor 編集(2D パッド + SceneView ハンドル + ボーン/AnchorPoint 選択)。
    // VfxEditorWindow.Params.cs: パラメータ即時反映 / 定義編集 / イベント / 複数同時再生 / 検証。
    //
    // 設計方針: このウィンドウだけで VfxData の調整が完結する(Inspector との往復を不要にする)。
    // Data への書き込みは全て Undo 対応。プレビューは実 VfxManager を駆動する(ADR-4)。
    [DDrive.Editor.Inspector.DataEditor(typeof(VfxData), "VFX Editor で開く")]
    public sealed partial class VfxEditorWindow : EditorWindow
    {
        private const float RepeatGapSec = 0.35f;

        // ドメインリロード(再コンパイル/PlayMode 遷移)後も編集状態を保持する。
        [SerializeField] private VfxData _target;
        [SerializeField] private GameObject _attachTarget; // スポーン位置/Anchor 解決の基準になるシーン内オブジェクト
        [SerializeField] private bool _lockTarget;
        [SerializeField] private bool _repeat;
        [SerializeField] private bool _sceneHandleEnabled = true;
        [SerializeField] private float _speed = 1f;

        private SceneVfxPreviewDriver _driver;
        private Handle<VfxMarker> _mainHandle;
        private SerializedObject _serializedTarget;

        // 「再生」ボタンを押してから「停止」を押すまで true。リピート再生の判定に使う。
        private bool _wantPlaying;
        private double _repeatWaitStart = -1;

        private ObjectField _targetField;
        private ObjectField _attachField;
        private ToolbarToggle _lockToggle;
        private HelpBox _sceneHelp;
        private Label _statusLabel;
        private Label _uiModeLabel;
        private Button _playButton;
        private Toggle _repeatToggle;
        private Slider _speedSlider;
        private Foldout _settingsFoldout;

        [MenuItem(DDriveMenu.Editors + "VFX")]
        public static void OpenFromMenu() => Open(Selection.activeObject as VfxData);

        public static void Open(VfxData target)
        {
            var window = GetWindow<VfxEditorWindow>("VFX Editor");
            window.minSize = new Vector2(500, 380); // [09] §7.1: 横幅の下限 500px を minSize で回避しない(2026-09-17、U-27)
            if (target != null)
            {
                window.SetTarget(target);
            }
        }

        // [08_presentation.md] 指摘3(2026-09-20)「一緒に調整」— PresentationEditor 等、他のエディタが
        // 開いている確認用シーン・配置済みモデルをそのまま使う。確認用シーンを開き直さない・プレビューを
        // 止めない(通常の Open(VfxData) と違い、シーン準備やプレビュー開始は一切行わない)。
        // attachTarget は「スポーン先(シーン内・任意)」欄(_attachTarget)にそのまま渡すだけ。
        public static void Open(VfxData target, GameObject attachTarget)
        {
            var window = GetWindow<VfxEditorWindow>("VFX Editor");
            window.minSize = new Vector2(500, 380);
            if (target != null)
            {
                window.SetTarget(target);
            }

            window._attachTarget = attachTarget;
            if (window._attachField != null)
            {
                window._attachField.SetValueWithoutNotify(attachTarget);
                window.RefreshBoneMenu();
                window.RefreshAnchorStatus();
            }
        }

        // ── ライフサイクル ──

        private void OnEnable()
        {
            _driver = new SceneVfxPreviewDriver { Speed = _speed };
            // default(Handle) は (0,0) で「最初のスロットの旧世代」と衝突し、毎フレームの IsPlaying が無効アクセス警告を出すため明示的に Invalid にする。
            _mainHandle = Handle<VfxMarker>.Invalid;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnEditorUpdate;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
            PrefabStage.prefabSaved += OnPrefabSaved;
            SceneView.duringSceneGui += OnSceneGui;
        }

        // SceneView の描画権: 最後にフォーカスしたウィンドウだけがハンドルと詳細を描く(SceneGuiOwner)。
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
            PrefabStage.prefabSaved -= OnPrefabSaved;
            PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
            PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= OnUndoRedo;
            _wantPlaying = false;
            _driver?.Dispose();
            _driver = null;
            SceneView.RepaintAll();
        }

        // Project ウィンドウで別の VfxData を選んだらそれに追従する(ロック中は固定)。
        private void OnSelectionChange()
        {
            if (!_lockTarget && Selection.activeObject is VfxData selected && selected != _target)
            {
                SetTarget(selected);
            }
        }

        private void OnUndoRedo()
        {
            // 2026-09-17([39] U-13): Undo/Redo の時点で対象が破棄されていると SerializedObject.Update() が
            // 「target has been destroyed」を出し、後続の BindProperty が例外になって RefreshValidation まで
            // 到達しなかった。作り直してから進める。
            EnsureSerializedTarget();
            RefreshAnchorUi();
            RebuildParamsUiIfChanged();
            RefreshValidation();
            _driver?.ReapplyAnchorToAll();
            SceneView.RepaintAll();
        }

        private void OnActiveSceneChanged(Scene previous, Scene current) => ResetForStageChange();

        // プレハブモードの開閉はスポーン先のシーンが変わるので、シーン切替と同じ扱い(Driver 側も台帳をリセットする)。
        private void OnPrefabStageChanged(PrefabStage stage) => ResetForStageChange();

        private void ResetForStageChange()
        {
            _mainHandle = Handle<VfxMarker>.Invalid;
            _wantPlaying = false;
            ResetSlotsUi();
            RefreshSceneHelp();
        }

        // プレハブモードで対象の Prefab を保存したら、再生中の実体を撮り直して編集内容を反映する
        // (スポーン物は Pool がアセットから生成するため、未保存の編集は反映されない)。
        private void OnPrefabSaved(GameObject prefabRoot)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || _target == null || _target.Prefab == null)
            {
                return;
            }

            if (stage.assetPath == AssetDatabase.GetAssetPath(_target.Prefab))
            {
                RestartMainIfPlaying();
            }
        }

        private void OnEditorUpdate()
        {
            if (_driver == null || _statusLabel == null)
            {
                return;
            }

            var playing = _driver.IsPlaying(_mainHandle);
            if (!playing)
            {
                // 終了済み Handle を毎フレーム問い合わせて無効 Handle 警告を出さないよう Invalid に戻す。
                _mainHandle = Handle<VfxMarker>.Invalid;
            }

            // リピート: OneShot/Duration が終わったら少し間を置いて再スポーンする(Loop は終わらないので対象外)。
            if (_wantPlaying && _repeat && !playing && _target != null)
            {
                var now = EditorApplication.timeSinceStartup;
                if (_repeatWaitStart < 0)
                {
                    _repeatWaitStart = now;
                }
                else if (now - _repeatWaitStart >= RepeatGapSec)
                {
                    _repeatWaitStart = -1;
                    PlayMain();
                    playing = true;
                }
            }

            var status = playing
                ? "● 再生中"
                : _wantPlaying && _repeat ? "↻ リピート待機" : "■ 停止中";
            if (_statusLabel.text != status)
            {
                _statusLabel.text = status;
                _playButton.text = playing ? "▶ 再生(やり直し)" : "▶ 再生";
            }
        }

        // ── UI 構築 ──

        private void CreateGUI()
        {
            BuildToolbar(rootVisualElement);

            // ウィンドウが小さい/セクションが増えても内容が見切れないよう、ルートをスクロール可能にする
            // ([09_editor_tools.md] §7 拡縮前提のUI規約)。
            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(scrollView);

            var root = scrollView;
            root.style.paddingLeft = 6;
            root.style.paddingRight = 6;
            root.style.paddingTop = 6;

            _targetField = new ObjectField("対象アセット") { objectType = typeof(VfxData) };
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as VfxData));
            root.Add(_targetField);

            _uiModeLabel = new Label { style = { opacity = 0.75f, marginBottom = 4, whiteSpace = WhiteSpace.Normal } };
            root.Add(_uiModeLabel);

            _sceneHelp = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            root.Add(_sceneHelp);

            _attachField = new ObjectField("スポーン先(シーン内・任意)")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                tooltip = "Anchor(ボーン名/AnchorPoint)の検索起点。キャラクターや AnchorRig を指定。未指定なら Anchor 定義のワールド座標に出る",
            };
            _attachField.SetValueWithoutNotify(_attachTarget);
            _attachField.RegisterValueChangedCallback(evt =>
            {
                _attachTarget = evt.newValue as GameObject;
                RefreshBoneMenu();
                RefreshAnchorStatus();
                RestartMainIfPlaying();
            });
            root.Add(_attachField);

            BuildPlaySection(root);
            BuildSettingsSection(root);
            BuildAnchorSection(root);
            BuildParamsSection(root);
            BuildEventsSection(root);
            BuildMultiSlotSection(root);
            BuildValidationSection(root);

            if (_target == null && !_lockTarget && Selection.activeObject is VfxData selected)
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

            _lockToggle = new ToolbarToggle { text = "🔒 対象を固定", value = _lockTarget, tooltip = "ON: Project ウィンドウの選択に追従しない" };
            _lockToggle.RegisterValueChangedCallback(evt => _lockTarget = evt.newValue);
            toolbar.Add(_lockToggle);

            toolbar.Add(new ToolbarSpacer());

            toolbar.Add(PreviewPlacementButton.CreateToolbarButton(
                "確認用シーンを開く",
                "ライト/カメラ/Volume/床を備えた VFX 確認用シーンを開き(無ければ生成)、対象をそこで再生する",
                OpenPreviewScene));
            toolbar.Add(new ToolbarButton(OpenPrefab) { text = "Prefab を開く", tooltip = "VfxData.Prefab をプレハブモードで開く" });
            toolbar.Add(new ToolbarButton(PingTarget) { text = "Project で表示", tooltip = "対象アセットを Project ウィンドウでハイライト" });
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(DDrive.Editor.Inspector.NewAssetToolbarButton.CreateToolbarButton(typeof(VfxEditorWindow)));

            root.Add(toolbar);
        }

        private void BuildPlaySection(VisualElement root)
        {
            var playRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2, marginTop = 4, alignItems = Align.Center } };
            _playButton = new Button(() => PlayMain(focus: true)) { text = "▶ 再生" };
            playRow.Add(_playButton);
            playRow.Add(new Button(StopMain) { text = "■ 停止" });

            _repeatToggle = new Toggle("リピート") { value = _repeat, tooltip = "OneShot/Duration の VFX が終わったら自動で再スポーンする(調整中に何度も押さなくてよい)" };
            _repeatToggle.style.marginLeft = 8;
            _repeatToggle.RegisterValueChangedCallback(evt =>
            {
                _repeat = evt.newValue;
                _repeatWaitStart = -1;
            });
            playRow.Add(_repeatToggle);

            _statusLabel = new Label("■ 停止中") { style = { marginLeft = 12, opacity = 0.8f } };
            playRow.Add(_statusLabel);
            root.Add(playRow);

            _speedSlider = new Slider("速度", 0.1f, 2f) { value = _speed, showInputField = true };
            _speedSlider.RegisterValueChangedCallback(evt =>
            {
                _speed = evt.newValue;
                _driver.Speed = _speed;
            });
            root.Add(_speedSlider);
        }

        // Inspector に戻らずに済むよう、Prefab/寿命/描画設定をこのウィンドウで編集できるようにする
        // (SerializedObject バインドなので Undo・Prefab 変更検知は Unity 標準に乗る)。
        private void BuildSettingsSection(VisualElement root)
        {
            _settingsFoldout = new Foldout { text = "基本設定(Prefab / 寿命 / 描画)", value = true };

            // Prefab 差し替え時は再生中の実体が古いままになるので、再スポーン + 検証し直す
            // (子の PropertyField からバブルしてくるので、ここで 1 回だけ登録する)。
            _settingsFoldout.RegisterCallback<SerializedPropertyChangeEvent>(evt =>
            {
                if (evt.changedProperty.propertyPath == "Prefab")
                {
                    RestartMainIfPlaying();
                }

                RefreshValidation();
            });
            root.Add(_settingsFoldout);
        }

        private void RebuildSettingsUi()
        {
            _settingsFoldout.Clear();
            if (_serializedTarget == null)
            {
                _settingsFoldout.Add(new Label("対象アセットを選択してください") { style = { opacity = 0.6f } });
                return;
            }

            AddBoundProperty(_settingsFoldout, "Prefab", "Prefab");
            AddBoundProperty(_settingsFoldout, "LifeMode", "寿命モード");
            AddBoundProperty(_settingsFoldout, "Duration", "継続秒数(Duration)");
            AddBoundProperty(_settingsFoldout, "FadeOutSec", "停止時フェード秒");
            AddBoundProperty(_settingsFoldout, "Render", "描画モード");

            var layerField = new LayerField("レイヤー(RenderLayer)") { tooltip = "スポーン物の GameObject.layer。UIOverlay の場合は VfxUI レイヤーを選ぶ" };
            layerField.BindProperty(_serializedTarget.FindProperty("RenderLayer"));
            _settingsFoldout.Add(layerField);

            _settingsFoldout.Add(BuildLightLayerField());
            AddBoundProperty(_settingsFoldout, "Flags", "共通フラグ(Pool / Pause / Net)");
        }

        // LightLayerMask(uint)を URP の Rendering Layer 名付きマスクで編集する。0 = Prefab の設定を上書きしない。
        private VisualElement BuildLightLayerField()
        {
            var names = RenderingLayerMask.GetDefinedRenderingLayerNames();
            var choices = new System.Collections.Generic.List<string>(names.Length);
            for (var i = 0; i < names.Length; i++)
            {
                // MaskField は選択肢の index = ビット位置。未定義レイヤーも位置を保つため空欄にせず一意な名前を入れる。
                choices.Add(string.IsNullOrEmpty(names[i]) ? $"(未定義 {i})" : names[i]);
            }

            var current = _target.LightLayerMask == uint.MaxValue ? -1 : unchecked((int)_target.LightLayerMask);
            var field = new MaskField("ライトレイヤー(LightLayerMask)", choices, current)
            {
                tooltip = "Rendering Layer Mask。Nothing(0) = Prefab の Renderer 設定を上書きしない",
            };
            field.RegisterValueChangedCallback(evt =>
            {
                if (_target == null)
                {
                    return;
                }

                Undo.RecordObject(_target, "Change VFX Light Layer");
                _target.LightLayerMask = evt.newValue == -1 ? uint.MaxValue : unchecked((uint)evt.newValue);
                EditorUtility.SetDirty(_target);
                _serializedTarget?.Update();
                RestartMainIfPlaying();
            });
            return field;
        }

        private void AddBoundProperty(VisualElement parent, string propertyPath, string label)
        {
            var prop = _serializedTarget.FindProperty(propertyPath);
            if (prop == null)
            {
                return;
            }

            var field = new PropertyField(prop, label);
            field.Bind(_serializedTarget);
            parent.Add(field);
        }

        // ── 対象管理 ──

        private void SetTarget(VfxData data)
        {
            StopMain();
            _target = data;
            _targetField?.SetValueWithoutNotify(data);
            RefreshTargetUi();
        }

        // 破棄済みの対象を指した SerializedObject を使い回さない(使うと Update()/FindProperty で
        // 例外になり、呼び出し側の更新処理が途中で止まる。[39] U-13)。
        private void EnsureSerializedTarget()
        {
            if (_target == null)
            {
                _serializedTarget = null;
                return;
            }

            if (_serializedTarget == null || _serializedTarget.targetObject == null)
            {
                _serializedTarget = new SerializedObject(_target);
                return;
            }

            _serializedTarget.Update();
        }

        private void RefreshTargetUi()
        {
            if (_targetField == null)
            {
                return; // CreateGUI 前
            }

            _serializedTarget = _target != null ? new SerializedObject(_target) : null;
            _targetField.SetValueWithoutNotify(_target);

            _uiModeLabel.text = _target != null && _target.Render == VfxRenderMode.UIOverlay
                ? "この VFX は Render=UIOverlay です。プレビューはそのまま表示されますが、実機では VfxUiSetup(Tools > D-Drive > Generate)で確保したレイヤーの UI カメラで合成されます。"
                : string.Empty;

            RebuildSettingsUi();
            RefreshAnchorUi();
            RefreshBoneMenu();
            RebuildParamsUi();
            RebuildEventsUi();
            RefreshValidation();
            SceneView.RepaintAll();
        }

        private void RefreshSceneHelp()
        {
            if (_sceneHelp == null)
            {
                return;
            }

            // プレハブモード中はステージのシーンへスポーンする。ステージは専用のライティング・カメラで描画されるため
            // カメラ/ライトの警告は出さず、代わりに再生先を案内する。
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                _sceneHelp.style.display = DisplayStyle.Flex;
                _sceneHelp.messageType = HelpBoxMessageType.Info;
                var stageName = System.IO.Path.GetFileNameWithoutExtension(stage.assetPath);
                if (_driver != null && _driver.IsInPlaceTarget(_target))
                {
                    _sceneHelp.text = $"プレハブモード '{stageName}' はこの VFX の Prefab 自身なので、ステージ内の実体をその場で再生します(別のインスタンスは出しません)。" +
                                      "Inspector で ParticleSystem を編集しながら確認できます。Anchor・パラメータの即時反映は対象外です。" +
                                      "ParticleSystem を選択中は Unity 標準のプレビューが進めます。";
                    return;
                }

                _sceneHelp.text = $"プレハブモード '{stageName}' の中で再生します。" +
                                  "スポーン物はプレハブには保存されません。プレハブの編集内容は保存(Ctrl+S)すると再生中の実体に反映されます。";
                return;
            }

            _sceneHelp.messageType = HelpBoxMessageType.Warning;
            var scene = SceneManager.GetActiveScene();
            var hasCamera = Camera.main != null || FindFirstObjectByType<Camera>() != null;
            var hasLight = FindFirstObjectByType<Light>() != null;

            if (hasCamera && hasLight)
            {
                _sceneHelp.style.display = DisplayStyle.None;
                return;
            }

            _sceneHelp.style.display = DisplayStyle.Flex;
            _sceneHelp.text = $"開いているシーン '{scene.name}' に{(hasCamera ? string.Empty : "カメラ")}{(!hasCamera && !hasLight ? "・" : string.Empty)}{(hasLight ? string.Empty : "ライト")}がありません。" +
                              "見た目の確認には上部の「確認用シーンを開く」で VFX 確認用シーンを開いてください。";
        }

        private void OpenPrefab()
        {
            if (_target != null && _target.Prefab != null)
            {
                AssetDatabase.OpenAsset(_target.Prefab);
            }
        }

        private void PingTarget()
        {
            if (_target != null)
            {
                EditorGUIUtility.PingObject(_target);
            }
        }

        // ── 再生制御(メイン対象) ──

        // U-5(2026-09-17): 左クリック = 確認用シーンを開いてそこで再生 / 右クリック = このシーンで再生・本配置。
        private void OpenPreviewScene(PreviewPlaceMode mode)
        {
            StopMain();
            if (!PreviewPlacement.PrepareScene(mode, VfxPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            if (PreviewPlacement.IsPersistent(mode))
            {
                if (_target == null)
                {
                    Debug.LogWarning("[DDrive] 対象 VfxData を選んでください。");
                    return;
                }

                // 本配置は VfxManager が追跡しない実体(Prefab リンク付き)にする。
                PreviewPlacement.PlacePrefabPersistent(_target.Prefab, Vector3.zero, Quaternion.identity);
                return;
            }

            PlayMain(focus: true);
        }

        // focus=false 既定。リピート再生(OnEditorUpdate)からも呼ばれるため、SceneView を寄せ直すのは
        // ユーザーが押したとき(▶ / 確認用シーンを開く)だけにする(U-5、2026-09-17)。
        private void PlayMain() => PlayMain(focus: false);

        private void PlayMain(bool focus)
        {
            if (_target == null)
            {
                return;
            }

            StopMainInternal();
            _wantPlaying = true;
            _repeatWaitStart = -1;
            _mainHandle = _driver.Play(_target, _attachTarget != null ? _attachTarget.transform : null);
            if (focus)
            {
                PreviewPlacement.Focus(_driver.Manager.GetGameObject(_mainHandle));
            }

            RefreshAnchorStatus();
        }

        private void StopMain()
        {
            _wantPlaying = false;
            _repeatWaitStart = -1;
            StopMainInternal();
        }

        private void StopMainInternal()
        {
            if (_driver != null && _driver.IsPlaying(_mainHandle))
            {
                _driver.Stop(_mainHandle);
            }

            _mainHandle = Handle<VfxMarker>.Invalid;
        }

        // Anchor の Path/Space・スポーン先・Prefab など「再スポーンしないと反映されない」変更のとき、
        // 再生中なら同じ条件で撮り直す(デザイナーが停止→再生を押し直す手間をなくす)。
        private void RestartMainIfPlaying()
        {
            if (_wantPlaying && _driver != null && _driver.IsPlaying(_mainHandle))
            {
                _driver.Kill(_mainHandle);
                PlayMain();
            }
        }
    }
}
