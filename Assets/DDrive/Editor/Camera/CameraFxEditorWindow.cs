using System.Collections.Generic;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Foundation.Data;
using DDrive.Foundation.Values;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Haptics;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace DDrive.Editor.CameraFx
{
    // [16_camera_haptics.md] §C-2(5-2c) — ShakeEditor / HapticsEditor(1 ウィンドウで両方を扱う。
    // AudioEditorWindow が SeData/BgmData を 1 ウィンドウで扱うのと同じ設計)。
    //
    // - 波形編集そのものは ValueDefDrawer([17] §5)の PropertyField をそのまま使う(シェイク専用・
    //   振動専用のカーブエディタは作らない、docs/16 C-2 に明記)。読み取り専用の波形プレビューだけ
    //   WaveformGraphGui で重ね描きする
    // - カメラ実揺れプレビューは SceneCameraShakePreviewDriver、Test on Pad は
    //   EditorHapticsPreviewDriver が実 Manager を駆動する(ADR-4)。ウィンドウ自身は SceneView /
    //   Game ビューに何も描かない(CLAUDE.md §0-7)
    // - プリセット 10 種は CameraFxPresets(Undo 対応)
    [DDrive.Editor.Inspector.DataEditor(typeof(CameraShakeData), "Shake Editor で開く")]
    [DDrive.Editor.Inspector.DataEditor(typeof(HapticsData), "Haptics Editor で開く")]
    public sealed class CameraFxEditorWindow : EditorWindow
    {
        private const int WaveformSampleCount = 64;
        private const float WaveformHeight = 110f;

        [SerializeField] private AssetDataBase _target;
        [SerializeField] private bool _lockTarget;

        private SceneCameraShakePreviewDriver _shakeDriver;
        private EditorHapticsPreviewDriver _hapticsDriver;
        private SerializedObject _serializedTarget;

        private ObjectField _targetField;
        private Label _statusLabel;
        private HelpBox _sceneHelp;
        private HelpBox _padHelp;
        private VisualElement _shakeSection;
        private VisualElement _hapticsSection;
        private IMGUIContainer _shakeWaveform;
        private IMGUIContainer _hapticsWaveform;

        [MenuItem(DDriveMenu.Editors + "Shake / Haptics")]
        public static void OpenFromMenu() => Open(Selection.activeObject as AssetDataBase);

        public static void Open(AssetDataBase target)
        {
            var window = GetWindow<CameraFxEditorWindow>("Shake / Haptics Editor");
            window.minSize = new Vector2(520, 420);
            if (target is CameraShakeData or HapticsData)
            {
                window.SetTarget(target);
            }
        }

        // ── ライフサイクル ──

        private void OnEnable()
        {
            _shakeDriver = new SceneCameraShakePreviewDriver();
            _hapticsDriver = new EditorHapticsPreviewDriver();
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= OnUndoRedo;
            _shakeDriver?.Dispose();
            _shakeDriver = null;
            _hapticsDriver?.Dispose();
            _hapticsDriver = null;
        }

        private void OnSelectionChange()
        {
            if (!_lockTarget && Selection.activeObject is AssetDataBase selected && selected != _target && selected is CameraShakeData or HapticsData)
            {
                SetTarget(selected);
            }
        }

        private void OnUndoRedo()
        {
            _serializedTarget?.Update();
            RefreshWaveforms();
        }

        private void OnEditorUpdate()
        {
            if (_statusLabel == null || _target == null)
            {
                return;
            }

            if (_target is CameraShakeData)
            {
                var cameraInfo = _shakeDriver.HasCamera ? string.Empty : "(Camera.main が見つかりません)";
                _statusLabel.text = $"合成中の Instance: {_shakeDriver.ActiveCount} {cameraInfo}";
                _sceneHelp.style.display = _shakeDriver.HasCamera ? DisplayStyle.None : DisplayStyle.Flex;
            }
            else if (_target is HapticsData)
            {
                var connected = Gamepad.current != null;
                _statusLabel.text = connected ? $"パッド接続中: {Gamepad.current.displayName}" : "パッド未接続";
                _padHelp.style.display = connected ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        // ── UI 構築 ──

        private void CreateGUI()
        {
            BuildToolbar(rootVisualElement);

            // ウィンドウが小さい/セクションが増えても内容が見切れないよう、ルートをスクロール可能にする([09] §7)。
            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(scrollView);
            var root = scrollView;
            root.style.paddingLeft = 6;
            root.style.paddingRight = 6;
            root.style.paddingTop = 6;

            _targetField = new ObjectField("対象アセット(Shake / Haptics)") { objectType = typeof(AssetDataBase) };
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as AssetDataBase));
            root.Add(_targetField);

            _statusLabel = new Label { style = { opacity = 0.8f, marginBottom = 4 } };
            root.Add(_statusLabel);

            _sceneHelp = new HelpBox(
                "開いているシーンに Camera.main がありません。上部の「確認用シーンを開く」でカメラを用意してください。",
                HelpBoxMessageType.Warning);
            root.Add(_sceneHelp);

            _padHelp = new HelpBox(
                "ゲームパッドが接続されていません。波形の確認のみ行えます(接続すると Test on Pad で振動を確認できます)。",
                HelpBoxMessageType.Info);
            root.Add(_padHelp);

            _shakeSection = BuildShakeSection();
            root.Add(_shakeSection);

            _hapticsSection = BuildHapticsSection();
            root.Add(_hapticsSection);

            if (_target == null && !_lockTarget && Selection.activeObject is AssetDataBase selected && selected is CameraShakeData or HapticsData)
            {
                SetTarget(selected);
            }
            else
            {
                RefreshTargetUi();
            }
        }

        private void BuildToolbar(VisualElement root)
        {
            var toolbar = new Toolbar();

            var lockToggle = new ToolbarToggle { text = "🔒 対象を固定", value = _lockTarget, tooltip = "ON: Project ウィンドウの選択に追従しない" };
            lockToggle.RegisterValueChangedCallback(evt => _lockTarget = evt.newValue);
            toolbar.Add(lockToggle);

            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(new ToolbarButton(CameraShakePreviewSceneSetup.OpenOrCreate)
            {
                text = "確認用シーンを開く",
                tooltip = "ライト/カメラ/床を備えた揺れ・振動確認用シーンを開く(無ければ生成)",
            });
            toolbar.Add(new ToolbarButton(PingTarget) { text = "Project で表示" });
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(DDrive.Editor.Inspector.NewAssetToolbarButton.CreateToolbarButton(typeof(CameraFxEditorWindow)));

            root.Add(toolbar);
        }

        private void PingTarget()
        {
            if (_target != null)
            {
                EditorGUIUtility.PingObject(_target);
            }
        }

        // ── Shake セクション ──

        private VisualElement BuildShakeSection()
        {
            var section = new VisualElement();

            var playRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, marginBottom = 4 } };
            playRow.Add(new Button(PlayShake) { text = "▶ Shake 再生(連打可)", tooltip = "実カメラを揺らす(連打すると Trauma 合成で多重発火の挙動を確認できる)" });
            playRow.Add(new Button(() => _shakeDriver.StopAll()) { text = "■ 全停止(フェード)" });
            section.Add(playRow);

            _shakeWaveform = WaveformGraphGui.Create(WaveformHeight, BuildShakeCurves, ShakeEmptyMessage);
            section.Add(_shakeWaveform);

            var fieldsFoldout = new Foldout { text = "パラメータ", value = true };
            section.Add(fieldsFoldout);
            section.userData = fieldsFoldout; // RebuildShakeFieldsUi から参照する

            var presetsFoldout = new Foldout { text = "プリセット(10種)", value = false };
            var presetsRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            foreach (var kind in CameraFxPresets.All)
            {
                var captured = kind;
                var button = new Button(() => ApplyShakePreset(captured)) { text = CameraFxPresets.Label(captured) };
                button.style.marginRight = 2;
                button.style.marginBottom = 2;
                presetsRow.Add(button);
            }

            presetsFoldout.Add(presetsRow);
            section.Add(presetsFoldout);

            return section;
        }

        private string ShakeEmptyMessage() => _target == null ? "対象を選択してください" : "Envelope の尺が 0 です(波形なし)";

        private void RebuildShakeFieldsUi()
        {
            var foldout = (Foldout)_shakeSection.userData;
            foldout.Clear();
            if (_serializedTarget == null || _target is not CameraShakeData)
            {
                return;
            }

            AddBoundProperty(foldout, "Pattern", "波形(Pattern)");
            AddBoundProperty(foldout, "PosAmplitude", "位置の揺れ幅(PosAmplitude)");
            AddBoundProperty(foldout, "RotAmplitude", "回転の揺れ幅(RotAmplitude)");
            AddBoundProperty(foldout, "Frequency", "周波数(Frequency)");
            AddBoundProperty(foldout, "Envelope", "減衰(Envelope)");
            AddBoundProperty(foldout, "Space", "空間(Space)");
            AddBoundProperty(foldout, "TraumaWeight", "合成の寄与度(TraumaWeight)");
            AddBoundProperty(foldout, "MaxStack", "同時許容数(MaxStack)");
        }

        private void PlayShake()
        {
            if (_target is CameraShakeData shake)
            {
                _shakeDriver.Play(shake);
            }
        }

        private void ApplyShakePreset(CameraFxPresetKind kind)
        {
            if (_target is not CameraShakeData shake)
            {
                return;
            }

            CameraFxPresets.ApplyShakePreset(shake, kind);
            _serializedTarget.Update();
            RefreshWaveforms();
        }

        private IReadOnlyList<WaveformGraphGui.Curve> BuildShakeCurves()
        {
            if (_target is not CameraShakeData shake)
            {
                return null;
            }

            var duration = shake.Envelope.Duration;
            if (duration <= 0f)
            {
                return null;
            }

            var envelope = new float[WaveformSampleCount];
            var posRaw = new float[WaveformSampleCount];
            var rotRaw = new float[WaveformSampleCount];
            var posPeak = 0f;
            var rotPeak = 0f;

            for (var i = 0; i < WaveformSampleCount; i++)
            {
                var t = i / (float)(WaveformSampleCount - 1) * duration;
                var envelopeValue = Mathf.Clamp01(shake.Envelope.EvaluateAt(t));
                envelope[i] = envelopeValue;

                var freq = Mathf.Max(0f, shake.Frequency.EvaluateAt(t));
                var shapeFactor = ShapeFactor(shake.Pattern, t, freq);
                posRaw[i] = envelopeValue * shake.PosAmplitude.magnitude * shapeFactor;
                rotRaw[i] = envelopeValue * shake.RotAmplitude.magnitude * shapeFactor;
                posPeak = Mathf.Max(posPeak, Mathf.Abs(posRaw[i]));
                rotPeak = Mathf.Max(rotPeak, Mathf.Abs(rotRaw[i]));
            }

            return new[]
            {
                new WaveformGraphGui.Curve("Envelope", new Color(0.9f, 0.9f, 0.9f), envelope),
                new WaveformGraphGui.Curve($"Pos(峰値 {posPeak:0.00}m)", new Color(0.35f, 0.65f, 1f), Normalize(posRaw)),
                new WaveformGraphGui.Curve($"Rot(峰値 {rotPeak:0.00}deg)", new Color(1f, 0.55f, 0.35f), Normalize(rotRaw)),
            };
        }

        // Impulse/CustomCurve は方向性のある一撃(振動しない)、PerlinNoise/DecaySine は波打つ様子を
        // 参考表示として重ねる(実際の乱数位相はインスタンスごとにずれるため厳密な再現ではない)。
        private static float ShapeFactor(ShakePattern pattern, float t, float freq) => pattern switch
        {
            ShakePattern.PerlinNoise => Mathf.PerlinNoise(0.123f, t * freq) * 2f - 1f,
            ShakePattern.DecaySine => Mathf.Sin(t * freq * Mathf.PI * 2f),
            _ => 1f,
        };

        // ── Haptics セクション ──

        private VisualElement BuildHapticsSection()
        {
            var section = new VisualElement();

            var playRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, marginBottom = 4 } };
            playRow.Add(new Button(PlayHaptic) { text = "▶ Test on Pad", tooltip = "接続中のゲームパッドを直接振動させる(Editor 再生外でも動く)" });
            playRow.Add(new Button(() => _hapticsDriver.ResetAndStop()) { text = "■ 停止" });
            section.Add(playRow);

            _hapticsWaveform = WaveformGraphGui.Create(WaveformHeight, BuildHapticsCurves, HapticsEmptyMessage);
            section.Add(_hapticsWaveform);

            var fieldsFoldout = new Foldout { text = "パラメータ", value = true };
            section.Add(fieldsFoldout);
            section.userData = fieldsFoldout;

            var presetsFoldout = new Foldout { text = "プリセット(10種)", value = false };
            var presetsRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            foreach (var kind in CameraFxPresets.All)
            {
                var captured = kind;
                var button = new Button(() => ApplyHapticsPreset(captured)) { text = CameraFxPresets.Label(captured) };
                button.style.marginRight = 2;
                button.style.marginBottom = 2;
                presetsRow.Add(button);
            }

            presetsFoldout.Add(presetsRow);
            section.Add(presetsFoldout);

            return section;
        }

        private string HapticsEmptyMessage() => _target == null ? "対象を選択してください" : "LowFreq / HighFreq の尺が 0 です(波形なし)";

        private void RebuildHapticsFieldsUi()
        {
            var foldout = (Foldout)_hapticsSection.userData;
            foldout.Clear();
            if (_serializedTarget == null || _target is not HapticsData)
            {
                return;
            }

            AddBoundProperty(foldout, "LowFreq", "低周波(LowFreq)");
            AddBoundProperty(foldout, "HighFreq", "高周波(HighFreq)");
            AddBoundProperty(foldout, "Priority", "優先度(Priority)");
            AddBoundProperty(foldout, "LocalPlayerOnly", "自分のみ(LocalPlayerOnly)");
            AddBoundProperty(foldout, "Extensions", "拡張(Extensions)");
        }

        private void PlayHaptic()
        {
            if (_target is HapticsData haptic)
            {
                _hapticsDriver.Play(haptic);
            }
        }

        private void ApplyHapticsPreset(CameraFxPresetKind kind)
        {
            if (_target is not HapticsData haptic)
            {
                return;
            }

            CameraFxPresets.ApplyHapticsPreset(haptic, kind);
            _serializedTarget.Update();
            RefreshWaveforms();
        }

        private IReadOnlyList<WaveformGraphGui.Curve> BuildHapticsCurves()
        {
            if (_target is not HapticsData haptic)
            {
                return null;
            }

            var duration = Mathf.Max(haptic.LowFreq.Duration, haptic.HighFreq.Duration);
            if (duration <= 0f)
            {
                return null;
            }

            var low = new float[WaveformSampleCount];
            var high = new float[WaveformSampleCount];
            for (var i = 0; i < WaveformSampleCount; i++)
            {
                var t = i / (float)(WaveformSampleCount - 1) * duration;
                low[i] = Mathf.Clamp01(haptic.LowFreq.EvaluateAt(t));
                high[i] = Mathf.Clamp01(haptic.HighFreq.EvaluateAt(t));
            }

            return new[]
            {
                new WaveformGraphGui.Curve("Low", new Color(0.35f, 0.65f, 1f), low),
                new WaveformGraphGui.Curve("High", new Color(1f, 0.55f, 0.35f), high),
            };
        }

        // ── 対象管理 ──

        public void SetTarget(AssetDataBase data)
        {
            if (data != null && data is not (CameraShakeData or HapticsData))
            {
                Debug.LogWarning("[DDrive] Shake / Haptics Editor は CameraShakeData / HapticsData のみ対象にできます。");
                return;
            }

            _target = data;
            _targetField?.SetValueWithoutNotify(data);
            RefreshTargetUi();
        }

        private void RefreshTargetUi()
        {
            if (_targetField == null)
            {
                return; // CreateGUI 前
            }

            _serializedTarget = _target != null ? new SerializedObject(_target) : null;
            _targetField.SetValueWithoutNotify(_target);

            var isShake = _target is CameraShakeData;
            var isHaptic = _target is HapticsData;
            _shakeSection.style.display = isShake ? DisplayStyle.Flex : DisplayStyle.None;
            _hapticsSection.style.display = isHaptic ? DisplayStyle.Flex : DisplayStyle.None;
            _sceneHelp.style.display = DisplayStyle.None;
            _padHelp.style.display = DisplayStyle.None;
            _statusLabel.text = _target == null ? "対象アセット(CameraShakeData または HapticsData)を選択してください" : string.Empty;

            RebuildShakeFieldsUi();
            RebuildHapticsFieldsUi();
            RefreshWaveforms();
        }

        private void RefreshWaveforms()
        {
            _shakeWaveform?.MarkDirtyRepaint();
            _hapticsWaveform?.MarkDirtyRepaint();
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
            field.RegisterCallback<SerializedPropertyChangeEvent>(_ => RefreshWaveforms());
            parent.Add(field);
        }

        private static float[] Normalize(float[] raw)
        {
            var max = 0f;
            foreach (var v in raw)
            {
                max = Mathf.Max(max, Mathf.Abs(v));
            }

            if (max <= 1e-6f)
            {
                return raw;
            }

            var result = new float[raw.Length];
            for (var i = 0; i < raw.Length; i++)
            {
                result[i] = raw[i] / max;
            }

            return result;
        }
    }
}
