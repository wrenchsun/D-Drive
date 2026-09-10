using System;
using System.Collections.Generic;
using DDrive.Runtime.Anim;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anim
{
    // [05_model_animation.md] B-4 ブレンド確認(2026-09-10 高級化)。3 つの確認手段を 1 つの Foldout にまとめる:
    //  1. 遷移シーケンス: 対象 → Step1 → Step2 … を、遷移ごとの CrossFade 秒と「前の Clip のどの位置で切り替えるか」で再生(ループ可)
    //  2. レイヤー同時再生: 別 Layer の AnimData を対象と一緒に再生し、Controller の各レイヤーの重みをスライダーで動かす(AvatarMask 名を表示)
    //  3. Blend Tree パラメータ: Controller の float パラメータを 2D パッドとスライダーで動かす(EditMode でも AnimManager が Animator.Update を回すので反映される)
    // どれも実 AnimManager / Animator に対する操作だけで、Data は書き換えない。
    public sealed partial class AnimEditorWindow
    {
        [Serializable]
        private struct BlendStep
        {
            [Tooltip("次に再生するアニメーション")]
            public AnimData Anim;

            [Tooltip("この Step へ切り替えるときの CrossFade 秒")]
            [Range(0f, 1f)] public float CrossFade;

            [Tooltip("前の Clip のどの位置(0〜1)で切り替えるか。1 以上 = 前の Clip が終わってから(Loop の Clip は 1 未満にする)")]
            [Range(0f, 1.5f)] public float SwitchAt;
        }

        private const float PadSize = 160f;

        [SerializeField] private List<BlendStep> _blendSteps = new();
        [SerializeField] private bool _sequenceLoop;
        [SerializeField] private List<AnimData> _layerAnims = new();
        [SerializeField] private string _padParamX = string.Empty;
        [SerializeField] private string _padParamY = string.Empty;
        [SerializeField] private bool _padUnitRange; // true: 0〜1 / false: -1〜1

        private int _seqIndex = -1; // -1 = 停止中。0 = 対象を再生中(次は Step[0])。k = Step[k-1] を再生中
        private SerializedObject _selfSerialized;
        private Label _seqStatusLabel;
        private VisualElement _layerWeightsContainer;
        private VisualElement _paramsContainer;
        private DropdownField _padXField;
        private DropdownField _padYField;
        private IMGUIContainer _padContainer;
        private readonly List<(string name, Slider slider)> _paramSliders = new();

        // ── UI ──

        private void BuildBlendSection(VisualElement root)
        {
            _selfSerialized = new SerializedObject(this);
            var foldout = new Foldout { text = "ブレンド確認(遷移シーケンス / レイヤー同時再生 / Blend Tree パラメータ)", value = false };

            // 1. 遷移シーケンス
            var seq = new Foldout { text = "遷移シーケンス(対象 → Step1 → Step2 …)", value = true };
            var steps = new PropertyField(_selfSerialized.FindProperty(nameof(_blendSteps)), "Steps");
            steps.Bind(_selfSerialized);
            seq.Add(steps);
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            row.Add(new Button(StartSequence) { text = "▶ シーケンスを再生" });
            row.Add(new Button(Stop) { text = "■ 停止" });
            var loop = new Toggle("ループ") { value = _sequenceLoop, tooltip = "最後の Step が終わったら対象から繰り返す" };
            loop.RegisterValueChangedCallback(evt => _sequenceLoop = evt.newValue);
            row.Add(loop);
            _seqStatusLabel = new Label { style = { marginLeft = 8, opacity = 0.8f } };
            row.Add(_seqStatusLabel);
            seq.Add(row);
            seq.Add(new Label("各 Step の SwitchAt は「前の Clip のどの位置で切り替えるか」(0.5 = 半分)。1 以上なら前の Clip が終わってから。Step が 1 つなら従来の A → B と同じ。") { style = { opacity = 0.6f, whiteSpace = WhiteSpace.Normal } });
            foldout.Add(seq);

            // 2. レイヤー同時再生
            var layers = new Foldout { text = "レイヤー同時再生(別 Layer の Anim を重ねる)", value = true };
            var layerAnims = new PropertyField(_selfSerialized.FindProperty(nameof(_layerAnims)), "一緒に再生する Anim(Layer は各 AnimData の設定)");
            layerAnims.Bind(_selfSerialized);
            layers.Add(layerAnims);
            layers.Add(new Button(PlayWithLayers) { text = "▶ 対象と一緒に再生" });
            layers.Add(new Label("Controller のレイヤー重み(再生中に動かして AvatarMask の効き方を確認):") { style = { opacity = 0.7f, marginTop = 4 } });
            _layerWeightsContainer = new VisualElement();
            layers.Add(_layerWeightsContainer);
            foldout.Add(layers);

            // 3. Blend Tree パラメータ
            var pad = new Foldout { text = "Blend Tree パラメータ(Controller の float を 2D パッド / スライダーで操作)", value = true };
            var padRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.FlexStart } };
            _padContainer = new IMGUIContainer(DrawPad);
            _padContainer.style.width = PadSize + 8f;
            _padContainer.style.height = PadSize + 8f;
            padRow.Add(_padContainer);
            var padSide = new VisualElement { style = { flexGrow = 1f, marginLeft = 6 } };
            _padXField = new DropdownField("横軸(X)");
            _padXField.RegisterValueChangedCallback(evt => { _padParamX = evt.newValue ?? string.Empty; _padContainer.MarkDirtyRepaint(); });
            _padYField = new DropdownField("縦軸(Y)");
            _padYField.RegisterValueChangedCallback(evt => { _padParamY = evt.newValue ?? string.Empty; _padContainer.MarkDirtyRepaint(); });
            padSide.Add(_padXField);
            padSide.Add(_padYField);
            var unit = new Toggle("範囲 0〜1(OFF: -1〜1)") { value = _padUnitRange };
            unit.RegisterValueChangedCallback(evt => { _padUnitRange = evt.newValue; RefreshParamSliderRanges(); _padContainer.MarkDirtyRepaint(); });
            padSide.Add(unit);
            padSide.Add(new Label("パッドのドラッグで X/Y を同時に、下のスライダーで個別に設定。値は Animator に直接入るのでデータは変わりません。") { style = { opacity = 0.6f, whiteSpace = WhiteSpace.Normal } });
            padRow.Add(padSide);
            pad.Add(padRow);
            _paramsContainer = new VisualElement();
            pad.Add(_paramsContainer);
            foldout.Add(pad);

            root.Add(foldout);
        }

        // 対象 Animator が変わったとき(RefreshModelInfo から)にレイヤー / パラメータの UI を作り直す。
        private void RefreshBlendUi(Animator animator)
        {
            if (_layerWeightsContainer == null || _paramsContainer == null)
            {
                return;
            }

            _layerWeightsContainer.Clear();
            _paramsContainer.Clear();
            _paramSliders.Clear();
            var floatNames = new List<string> { string.Empty };

            if (animator == null || animator.runtimeAnimatorController == null)
            {
                _layerWeightsContainer.Add(new Label(animator == null ? "対象がありません。" : "Controller が無いためレイヤー / パラメータはありません。") { style = { opacity = 0.6f } });
                _padXField.choices = floatNames;
                _padYField.choices = floatNames;
                return;
            }

            var controller = ResolveController(animator.runtimeAnimatorController);
            for (var i = 0; i < animator.layerCount; i++)
            {
                var layer = i;
                var mask = controller != null && i < controller.layers.Length && controller.layers[i].avatarMask != null ? controller.layers[i].avatarMask.name : "なし";
                var slider = new Slider($"[{i}] {animator.GetLayerName(i)}  (Mask: {mask})", 0f, 1f) { value = animator.GetLayerWeight(i), showInputField = true };
                slider.SetEnabled(i > 0); // Layer 0 は常に 1
                slider.RegisterValueChangedCallback(evt =>
                {
                    if (_scene?.Current != null)
                    {
                        _scene.Current.SetLayerWeight(layer, evt.newValue);
                        SceneView.RepaintAll();
                    }
                });
                _layerWeightsContainer.Add(slider);
            }

            foreach (var p in animator.parameters)
            {
                var name = p.name;
                switch (p.type)
                {
                    case AnimatorControllerParameterType.Float:
                    {
                        floatNames.Add(name);
                        var (min, max) = PadRange();
                        var current = animator.GetFloat(name);
                        var slider = new Slider(name, Mathf.Min(min, current), Mathf.Max(max, current)) { value = current, showInputField = true };
                        slider.RegisterValueChangedCallback(evt => SetParam(name, evt.newValue));
                        _paramSliders.Add((name, slider));
                        _paramsContainer.Add(slider);
                        break;
                    }
                    case AnimatorControllerParameterType.Int:
                    {
                        var field = new IntegerField(name) { value = animator.GetInteger(name) };
                        field.RegisterValueChangedCallback(evt => { if (_scene?.Current != null) { _scene.Current.SetInteger(name, evt.newValue); SceneView.RepaintAll(); } });
                        _paramsContainer.Add(field);
                        break;
                    }
                    case AnimatorControllerParameterType.Bool:
                    {
                        var toggle = new Toggle(name) { value = animator.GetBool(name) };
                        toggle.RegisterValueChangedCallback(evt => { if (_scene?.Current != null) { _scene.Current.SetBool(name, evt.newValue); SceneView.RepaintAll(); } });
                        _paramsContainer.Add(toggle);
                        break;
                    }
                    case AnimatorControllerParameterType.Trigger:
                        _paramsContainer.Add(new Button(() => { if (_scene?.Current != null) { _scene.Current.SetTrigger(name); SceneView.RepaintAll(); } }) { text = $"Trigger: {name}" });
                        break;
                }
            }

            if (floatNames.Count == 1)
            {
                _paramsContainer.Add(new Label("float パラメータがありません(Blend Tree の軸は Controller の float パラメータです)。") { style = { opacity = 0.6f } });
            }

            _padXField.choices = floatNames;
            _padYField.choices = floatNames;
            if (!floatNames.Contains(_padParamX)) _padParamX = floatNames.Count > 1 ? floatNames[1] : string.Empty;
            if (!floatNames.Contains(_padParamY)) _padParamY = floatNames.Count > 2 ? floatNames[2] : string.Empty;
            _padXField.SetValueWithoutNotify(_padParamX);
            _padYField.SetValueWithoutNotify(_padParamY);
            _padContainer.MarkDirtyRepaint();
        }

        private static AnimatorController ResolveController(RuntimeAnimatorController runtime)
        {
            while (runtime is AnimatorOverrideController over)
            {
                runtime = over.runtimeAnimatorController;
            }

            return runtime as AnimatorController;
        }

        private (float min, float max) PadRange() => _padUnitRange ? (0f, 1f) : (-1f, 1f);

        private void RefreshParamSliderRanges()
        {
            var (min, max) = PadRange();
            foreach (var (_, slider) in _paramSliders)
            {
                slider.lowValue = Mathf.Min(min, slider.value);
                slider.highValue = Mathf.Max(max, slider.value);
            }
        }

        private void SetParam(string name, float value)
        {
            var animator = _scene?.Current;
            if (animator == null || string.IsNullOrEmpty(name))
            {
                return;
            }

            animator.SetFloat(name, value);
            foreach (var (n, slider) in _paramSliders)
            {
                if (n == name && !Mathf.Approximately(slider.value, value))
                {
                    slider.SetValueWithoutNotify(value);
                }
            }

            _padContainer?.MarkDirtyRepaint();
            SceneView.RepaintAll();
        }

        // 2D パッド: 横 = X パラメータ、縦 = Y パラメータ。ドラッグで両方を同時に設定する。
        private void DrawPad()
        {
            var rect = GUILayoutUtility.GetRect(PadSize, PadSize);
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.center.y, rect.width, 1f), new Color(1f, 1f, 1f, 0.2f));
            EditorGUI.DrawRect(new Rect(rect.center.x, rect.y, 1f, rect.height), new Color(1f, 1f, 1f, 0.2f));

            var animator = _scene?.Current;
            var hasX = animator != null && !string.IsNullOrEmpty(_padParamX);
            var hasY = animator != null && !string.IsNullOrEmpty(_padParamY);
            if (!hasX && !hasY)
            {
                GUI.Label(rect, "X / Y のパラメータを選択", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var (min, max) = PadRange();
            var x = hasX ? Mathf.InverseLerp(min, max, animator.GetFloat(_padParamX)) : 0.5f;
            var y = hasY ? Mathf.InverseLerp(min, max, animator.GetFloat(_padParamY)) : 0.5f;
            var px = rect.x + rect.width * x;
            var py = rect.y + rect.height * (1f - y);
            EditorGUI.DrawRect(new Rect(px - 4f, py - 4f, 8f, 8f), new Color(1f, 0.8f, 0.3f));
            GUI.Label(new Rect(rect.x + 2f, rect.y + rect.height - 16f, rect.width - 4f, 14f),
                $"{(hasX ? _padParamX + "=" + animator.GetFloat(_padParamX).ToString("0.00") : "")}  {(hasY ? _padParamY + "=" + animator.GetFloat(_padParamY).ToString("0.00") : "")}",
                EditorStyles.miniLabel);

            var evt = Event.current;
            if ((evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag) && rect.Contains(evt.mousePosition))
            {
                var nx = Mathf.Clamp01((evt.mousePosition.x - rect.x) / rect.width);
                var ny = 1f - Mathf.Clamp01((evt.mousePosition.y - rect.y) / rect.height);
                if (hasX) SetParam(_padParamX, Mathf.Lerp(min, max, nx));
                if (hasY) SetParam(_padParamY, Mathf.Lerp(min, max, ny));
                evt.Use();
            }
        }

        // ── 遷移シーケンス ──

        private void StartSequence()
        {
            if (_target == null || _scene == null)
            {
                return;
            }

            var animator = EnsureSceneTarget();
            if (animator == null)
            {
                AppendLog("⚠ 対象がありません。「確認用シーンを開く」「モデル Prefab を開く」を押してください");
                return;
            }

            _scene.StopSpawned();
            _animHandle = _scene.Play(_target, animator);
            _seqIndex = 0;
            AppendLog($"▶ シーケンス開始: '{_target.DisplayName ?? _target.name}'");
            RefreshSequenceStatus();
        }

        // OnEditorUpdate から毎フレーム。前の Clip が SwitchAt に達したら次の Step へ CrossFade。
        private void TickSequence(AnimManager anim, ref bool playing)
        {
            if (_seqIndex < 0 || _scene == null || _scene.Current == null)
            {
                return;
            }

            if (_seqIndex < _blendSteps.Count)
            {
                var step = _blendSteps[_seqIndex];
                if (step.Anim == null)
                {
                    _seqIndex++;
                    return;
                }

                var t = anim.GetNormalizedTime(_animHandle);
                var due = !playing || (step.SwitchAt < 1f && t >= step.SwitchAt);
                if (due)
                {
                    _animHandle = _scene.Play(step.Anim, _scene.Current, step.CrossFade);
                    playing = true;
                    AppendLog($"→ Step{_seqIndex + 1} '{step.Anim.DisplayName ?? step.Anim.name}' へ CrossFade({step.CrossFade:0.##}s、切替 {(step.SwitchAt < 1f ? $"{step.SwitchAt:P0}" : "終了時")})");
                    _seqIndex++;
                }
            }
            else if (!playing)
            {
                if (_sequenceLoop)
                {
                    StartSequence();
                    playing = true;
                }
                else
                {
                    _seqIndex = -1;
                    AppendLog("■ シーケンス終了");
                }
            }

            RefreshSequenceStatus();
        }

        private void RefreshSequenceStatus()
        {
            if (_seqStatusLabel == null)
            {
                return;
            }

            var text = _seqIndex < 0 ? string.Empty
                : _seqIndex == 0 ? $"対象を再生中 → 次: Step1 / {_blendSteps.Count}"
                : _seqIndex <= _blendSteps.Count ? $"Step{_seqIndex} / {_blendSteps.Count} を再生中"
                : "最後の Step を再生中(終わると" + (_sequenceLoop ? "ループ)" : "停止)");
            if (_seqStatusLabel.text != text)
            {
                _seqStatusLabel.text = text;
            }
        }

        // ── レイヤー同時再生 ──

        private void PlayWithLayers()
        {
            if (_target == null || _scene == null)
            {
                return;
            }

            var animator = EnsureSceneTarget();
            if (animator == null)
            {
                AppendLog("⚠ 対象がありません");
                return;
            }

            _seqIndex = -1;
            _scene.StopSpawned();
            _animHandle = _scene.Play(_target, animator);
            AppendLog($"▶ '{_target.DisplayName ?? _target.name}' (Layer {_target.Layer})");
            foreach (var data in _layerAnims)
            {
                if (data == null)
                {
                    continue;
                }

                if (data.Layer == _target.Layer)
                {
                    AppendLog($"⚠ '{data.DisplayName ?? data.name}' は対象と同じ Layer {data.Layer} なので対象を中断します(Data の Layer を変えてください)");
                }
                else if (animator.runtimeAnimatorController != null && data.Layer >= animator.layerCount)
                {
                    AppendLog($"⚠ '{data.DisplayName ?? data.name}' の Layer {data.Layer} は Controller にありません(レイヤー数 {animator.layerCount})。時間追跡とイベントだけ動きます");
                }

                _scene.Play(data, animator);
                AppendLog($"  + '{data.DisplayName ?? data.name}' (Layer {data.Layer})");
            }

            RefreshBlendUi(animator);
        }
    }
}
