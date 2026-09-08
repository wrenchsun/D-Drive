using System.Collections.Generic;
using DDrive.Editor.Audio;
using DDrive.Editor.Menu;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Vfx
{
    // [04_vfx.md] §5(2026-07-28 改定) — VFX 専用エディタ(2-4)。
    // プレビューは独自ビューポートではなく「開いているシーンへ直接スポーン → SceneView で確認」方式。
    // ライティング/ポストプロセス/Skybox はシーン側の設定がそのまま適用されるため、
    // 確認専用シーンを開いた状態で調整する運用を想定する(SceneVfxPreviewDriver 参照)。
    // Anchor 編集(2D パッド + ボーン/AnchorPoint 選択) / 複数同時再生(最大8) /
    // パラメータ即時反映 / イベント編集 / UIモード確認。
    public sealed class VfxEditorWindow : EditorWindow
    {
        private const int MaxSlots = 8;
        private const float AnchorPadHeight = 160f;

        private sealed class SlotState
        {
            public VfxData Data;
            public Handle<VfxMarker> Handle;
            public ObjectField Field;
            public Button ToggleButton;
        }

        private VfxData _target;
        private SceneVfxPreviewDriver _driver;
        private Handle<VfxMarker> _mainHandle;
        private SerializedObject _serializedTarget;

        private readonly List<SlotState> _slots = new();

        private ObjectField _targetField;
        private VisualElement _paramsContainer;
        private Label _uiModeLabel;

        // スポーン位置/Anchor 解決の基準になるシーン内オブジェクト(キャラクターや AnchorRig)。
        private GameObject _attachTarget;

        private EnumField _anchorSpaceField;
        private TextField _anchorPathField;
        private Slider _anchorHeightSlider;
        private Slider _anchorFacingSlider;
        private Toggle _anchorFollowRotToggle;

        private ToolbarMenu _boneDropdown;

        private Vector2 _anchorPadOffset; // X/Z(メートル)。パッド上の 2D 表現
        private const float AnchorPadRange = 5f;
        private bool _draggingAnchorPad;

        [MenuItem(DDriveMenu.Editors + "VFX")]
        public static void OpenFromMenu() => Open(Selection.activeObject as VfxData);

        public static void Open(VfxData target)
        {
            var window = GetWindow<VfxEditorWindow>("VFX Editor");
            window.minSize = new Vector2(520, 380);
            if (target != null)
            {
                window.SetTarget(target);
            }
        }

        private void OnEnable()
        {
            _driver = new SceneVfxPreviewDriver();
        }

        private void OnDisable()
        {
            _driver?.Dispose();
            _driver = null;
        }

        private void CreateGUI()
        {
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

            root.Add(new HelpBox(
                "再生すると開いているシーンに直接スポーンし、SceneView で確認します(シーンには保存されません)。" +
                "ライティング・ポストプロセスはシーン側の設定がそのまま適用されるため、確認専用シーンを開いて調整してください。",
                HelpBoxMessageType.Info));

            var attachField = new ObjectField("スポーン先(シーン内・任意)")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                tooltip = "Anchor(ボーン名/AnchorPoint)の検索起点。キャラクターや AnchorRig を指定。未指定なら Anchor 定義のワールド座標に出る",
            };
            attachField.RegisterValueChangedCallback(evt =>
            {
                _attachTarget = evt.newValue as GameObject;
                RefreshBoneMenu();
            });
            root.Add(attachField);

            var playRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6, marginTop = 4 } };
            playRow.Add(new Button(PlayMain) { text = "▶ 再生" });
            playRow.Add(new Button(StopMain) { text = "■ 停止" });
            root.Add(playRow);

            var speedSlider = new Slider("速度", 0.1f, 2f) { value = 1f };
            speedSlider.RegisterValueChangedCallback(evt => _driver.Speed = evt.newValue);
            root.Add(speedSlider);

            BuildMultiSlotSection(root);
            BuildAnchorSection(root);

            _paramsContainer = new VisualElement();
            root.Add(_paramsContainer);

            var eventsFoldout = new Foldout { text = "イベント(OnSpawn/OnLoop/OnDestroy 等)", value = false };
            root.Add(eventsFoldout);
            _eventsFoldout = eventsFoldout;

            if (_target == null && Selection.activeObject is VfxData selected)
            {
                SetTarget(selected);
            }
            else
            {
                RefreshTargetUi();
            }
        }

        private Foldout _eventsFoldout;

        private void SetTarget(VfxData data)
        {
            StopMain();
            _target = data;
            _targetField.SetValueWithoutNotify(data);
            RefreshTargetUi();
        }

        private void RefreshTargetUi()
        {
            _serializedTarget = _target != null ? new SerializedObject(_target) : null;

            _uiModeLabel.text = _target != null && _target.Render == VfxRenderMode.UIOverlay
                ? "この VFX は Render=UIOverlay です。プレビューはそのまま表示されますが、実機では VfxUiSetup(Tools > D-Drive > Generate)で確保したレイヤーの UI カメラで合成されます。"
                : string.Empty;

            RebuildParamsUi();
            RebuildEventsUi();
            RefreshAnchorUi();
        }

        // Anchor 編集 UI がまだ構築されていない(初回 CreateGUI 実行前)場合は何もしない。
        private void RefreshAnchorUi()
        {
            if (_anchorSpaceField == null)
            {
                return;
            }

            var anchor = _target != null ? _target.Anchor : default;
            _anchorPadOffset = new Vector2(anchor.LocalOffset.x, anchor.LocalOffset.z);

            _anchorSpaceField.SetValueWithoutNotify(anchor.Space);
            _anchorPathField.SetValueWithoutNotify(anchor.Path);
            _anchorHeightSlider.SetValueWithoutNotify(anchor.LocalOffset.y);
            _anchorFacingSlider.SetValueWithoutNotify(anchor.LocalEuler.y);
            _anchorFollowRotToggle.SetValueWithoutNotify(anchor.FollowRotation);

            var enabled = _target != null;
            _anchorSpaceField.SetEnabled(enabled);
            _anchorPathField.SetEnabled(enabled);
            _anchorHeightSlider.SetEnabled(enabled);
            _anchorFacingSlider.SetEnabled(enabled);
            _anchorFollowRotToggle.SetEnabled(enabled);
        }

        // ── 複数同時再生(最大8スロット) ──
        private void BuildMultiSlotSection(VisualElement root)
        {
            var foldout = new Foldout { text = $"複数同時再生(最大 {MaxSlots})", value = false };

            for (var i = 0; i < MaxSlots; i++)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                var slot = new SlotState();

                slot.Field = new ObjectField { objectType = typeof(VfxData), style = { flexGrow = 1f } };
                row.Add(slot.Field);

                slot.ToggleButton = new Button(() => ToggleSlot(slot)) { text = "▶" };
                slot.ToggleButton.style.width = 32;
                row.Add(slot.ToggleButton);

                foldout.Add(row);
                _slots.Add(slot);
            }

            root.Add(foldout);
        }

        private void ToggleSlot(SlotState slot)
        {
            if (_driver.Manager.IsPlaying(slot.Handle))
            {
                _driver.Stop(slot.Handle);
                slot.Handle = Handle<VfxMarker>.Invalid;
                slot.ToggleButton.text = "▶";
                return;
            }

            slot.Data = slot.Field.value as VfxData;
            if (slot.Data == null)
            {
                return;
            }

            slot.Handle = _driver.Play(slot.Data, _attachTarget != null ? _attachTarget.transform : null);
            slot.ToggleButton.text = "■";
        }

        // ── Anchor 編集(2D パッドで X/Z オフセット、スライダーで高さ/向き) ──
        private void BuildAnchorSection(VisualElement root)
        {
            var foldout = new Foldout { text = "Anchor 編集", value = false };

            _anchorSpaceField = new EnumField("Space", AnchorSpace.World);
            _anchorSpaceField.RegisterValueChangedCallback(evt => ApplyAnchorChange(a =>
            {
                a.Space = (AnchorSpace)evt.newValue;
                return a;
            }));
            foldout.Add(_anchorSpaceField);

            var pathRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _anchorPathField = new TextField("Path(ボーン/オブジェクト名)") { style = { flexGrow = 1f } };
            _anchorPathField.RegisterValueChangedCallback(evt => ApplyAnchorChange(a =>
            {
                a.Path = evt.newValue;
                return a;
            }));
            pathRow.Add(_anchorPathField);

            _boneDropdown = new ToolbarMenu { text = "ボーン一覧から選択" };
            pathRow.Add(_boneDropdown);
            foldout.Add(pathRow);
            RefreshBoneMenu();

            _anchorHeightSlider = new Slider("高さオフセット(Y)", -3f, 3f);
            _anchorHeightSlider.RegisterValueChangedCallback(evt => ApplyAnchorChange(a =>
            {
                var offset = a.LocalOffset;
                offset.y = evt.newValue;
                a.LocalOffset = offset;
                return a;
            }));
            foldout.Add(_anchorHeightSlider);

            _anchorFacingSlider = new Slider("向き(Euler Y)", -180f, 180f);
            _anchorFacingSlider.RegisterValueChangedCallback(evt => ApplyAnchorChange(a =>
            {
                var euler = a.LocalEuler;
                euler.y = evt.newValue;
                a.LocalEuler = euler;
                return a;
            }));
            foldout.Add(_anchorFacingSlider);

            _anchorFollowRotToggle = new Toggle("回転追従(FollowRotation)");
            _anchorFollowRotToggle.RegisterValueChangedCallback(evt => ApplyAnchorChange(a =>
            {
                a.FollowRotation = evt.newValue;
                return a;
            }));
            foldout.Add(_anchorFollowRotToggle);

            var padContainer = new IMGUIContainer(DrawAnchorPad);
            padContainer.style.height = AnchorPadHeight;
            foldout.Add(padContainer);

            root.Add(foldout);
        }

        // ToolbarMenu はクリック時に「その時点の menu の中身」を開くだけなので、選択元の
        // スポーン先が変わるたびに中身を作り直す(クリック時イベントではなく変化時に更新)。
        // AnchorPoint(シーン配置型アンカー)を持つ場合はそれを先頭に「★」付きで出す。
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

            foreach (var point in _attachTarget.GetComponentsInChildren<DDrive.Runtime.Anchoring.AnchorPoint>(true))
            {
                var name = point.name;
                _boneDropdown.menu.AppendAction($"★ {name}", _ => _anchorPathField.value = name);
            }

            foreach (var t in _attachTarget.GetComponentsInChildren<Transform>(true))
            {
                var name = t.name;
                _boneDropdown.menu.AppendAction(name, _ => _anchorPathField.value = name);
            }
        }

        private void ApplyAnchorChange(System.Func<AnchorDef, AnchorDef> mutate)
        {
            if (_target == null)
            {
                return;
            }

            Undo.RecordObject(_target, "Change VFX Anchor");
            _target.Anchor = mutate(_target.Anchor);
            EditorUtility.SetDirty(_target);
        }

        private void DrawAnchorPad()
        {
            var rect = GUILayoutUtility.GetRect(100, AnchorPadHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.center.y - 0.5f, rect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            EditorGUI.DrawRect(new Rect(rect.center.x - 0.5f, rect.y, 1f, rect.height), new Color(1f, 1f, 1f, 0.08f));

            if (_target == null)
            {
                GUI.Label(rect, "対象アセットが未選択です", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var originPx = rect.center;
            EditorGUI.DrawRect(new Rect(originPx.x - 4f, originPx.y - 4f, 8f, 8f), new Color(0.95f, 0.25f, 0.2f));

            var pointPx = ListenerPadMath.MetersToPixel(_anchorPadOffset, rect, AnchorPadRange, AnchorPadRange);
            EditorGUI.DrawRect(new Rect(pointPx.x - 5f, pointPx.y - 5f, 10f, 10f), new Color(0.35f, 0.85f, 0.65f));

            GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 16f),
                $"X: {_anchorPadOffset.x:F2}m   Z: {_anchorPadOffset.y:F2}m(上から見た配置。ドラッグで移動)",
                EditorStyles.miniLabel);

            var evt = Event.current;
            switch (evt.type)
            {
                case EventType.MouseDown when rect.Contains(evt.mousePosition):
                    _draggingAnchorPad = true;
                    MoveAnchorPad(rect, evt.mousePosition);
                    evt.Use();
                    break;

                case EventType.MouseDrag when _draggingAnchorPad:
                    MoveAnchorPad(rect, evt.mousePosition);
                    evt.Use();
                    break;

                case EventType.MouseUp when _draggingAnchorPad:
                    _draggingAnchorPad = false;
                    evt.Use();
                    break;
            }
        }

        private void MoveAnchorPad(Rect rect, Vector2 mousePx)
        {
            var meters = ListenerPadMath.PixelToMeters(mousePx, rect, AnchorPadRange, AnchorPadRange);
            _anchorPadOffset = ListenerPadMath.ClampToRange(meters, AnchorPadRange, AnchorPadRange);

            ApplyAnchorChange(a =>
            {
                var offset = a.LocalOffset;
                offset.x = _anchorPadOffset.x;
                offset.z = _anchorPadOffset.y;
                a.LocalOffset = offset;
                return a;
            });
        }

        // ── パラメータ即時反映 ──
        private void RebuildParamsUi()
        {
            _paramsContainer.Clear();

            if (_target == null || _target.Params == null || _target.Params.Length == 0)
            {
                return;
            }

            var foldout = new Foldout { text = "パラメータ(再生中は即時反映)", value = true };

            for (var i = 0; i < _target.Params.Length; i++)
            {
                var index = i;
                foldout.Add(BuildParamControl(_target.Params[index], index));
            }

            _paramsContainer.Add(foldout);
        }

        private VisualElement BuildParamControl(VfxParam param, int index)
        {
            switch (param.Type)
            {
                case VfxParamType.Float:
                {
                    var field = new FloatField(param.Label) { value = param.Default.FloatValue };
                    field.RegisterValueChangedCallback(evt => OnParamChanged(index, ParamValue.Of(evt.newValue)));
                    return field;
                }

                case VfxParamType.Int:
                {
                    var field = new IntegerField(param.Label) { value = param.Default.IntValue };
                    field.RegisterValueChangedCallback(evt => OnParamChanged(index, ParamValue.Of(evt.newValue)));
                    return field;
                }

                case VfxParamType.Color:
                {
                    var field = new ColorField(param.Label) { value = param.Default.ColorValue };
                    field.RegisterValueChangedCallback(evt => OnParamChanged(index, ParamValue.Of(evt.newValue)));
                    return field;
                }

                case VfxParamType.Vector:
                {
                    var field = new Vector4Field(param.Label) { value = param.Default.VectorValue };
                    field.RegisterValueChangedCallback(evt =>
                        OnParamChanged(index, new ParamValue { Type = ParamValueType.Vector, VectorValue = evt.newValue }));
                    return field;
                }

                case VfxParamType.Texture:
                {
                    var field = new ObjectField(param.Label) { objectType = typeof(Texture) };
                    field.RegisterValueChangedCallback(evt =>
                    {
                        var v = new ParamValue { Type = ParamValueType.Object, ObjectValue = evt.newValue };
                        OnParamChanged(index, v);
                    });
                    return field;
                }

                default:
                    return new Label($"{param.Label}({param.Type}): ライブプレビュー未対応(MaterialPropertyBlock 非対応型)");
            }
        }

        private void OnParamChanged(int index, ParamValue value)
        {
            if (_target == null || _target.Params == null || index >= _target.Params.Length)
            {
                return;
            }

            Undo.RecordObject(_target, "Change VFX Param Default");
            _target.Params[index].Default = value;
            EditorUtility.SetDirty(_target);

            if (_driver.Manager.IsPlaying(_mainHandle))
            {
                _driver.Manager.SetParam(_mainHandle, _target.Params[index].Label, value);
            }
        }

        // ── イベント編集(OnSpawn/OnLoop/OnDestroy → SE再生等) ──
        private void RebuildEventsUi()
        {
            _eventsFoldout.Clear();

            if (_serializedTarget == null)
            {
                return;
            }

            var eventsProp = _serializedTarget.FindProperty("Events");
            if (eventsProp == null)
            {
                return;
            }

            var field = new PropertyField(eventsProp);
            field.Bind(_serializedTarget);
            _eventsFoldout.Add(field);
        }

        // ── 再生制御(メイン対象) ──
        private void PlayMain()
        {
            if (_target == null)
            {
                return;
            }

            StopMain();
            _mainHandle = _driver.Play(_target, _attachTarget != null ? _attachTarget.transform : null);
        }

        private void StopMain()
        {
            if (_driver != null && _driver.Manager.IsPlaying(_mainHandle))
            {
                _driver.Stop(_mainHandle);
            }

            _mainHandle = Handle<VfxMarker>.Invalid;
        }
    }
}
