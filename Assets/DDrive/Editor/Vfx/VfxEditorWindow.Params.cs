using System.Collections.Generic;
using System.Text;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Editor.Validation;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Vfx
{
    // パラメータ(即時反映コントロール + 定義編集) / イベント編集 / 複数同時再生 / 検証結果。
    public sealed partial class VfxEditorWindow
    {
        private const int MaxSlots = 8;

        private sealed class SlotState
        {
            public VfxData Data;
            public Handle<VfxMarker> Handle = Handle<VfxMarker>.Invalid;
            public ObjectField Field;
            public Button ToggleButton;
        }

        private readonly List<SlotState> _slots = new();

        private Foldout _paramsFoldout;
        private VisualElement _paramsLiveContainer;
        private VisualElement _paramsDefinitionContainer;
        private string _paramsSignature;

        private Foldout _eventsFoldout;
        private DataValidationSection _validationSection;

        // ── パラメータ ──

        private void BuildParamsSection(VisualElement root)
        {
            _paramsFoldout = new Foldout { text = "パラメータ(再生中は即時反映)", value = true };

            _paramsLiveContainer = new VisualElement();
            _paramsFoldout.Add(_paramsLiveContainer);

            var definition = new Foldout { text = "定義の追加・削除(Label / Type / TargetProperty / Anim)", value = false };
            _paramsDefinitionContainer = new VisualElement();
            definition.Add(_paramsDefinitionContainer);
            _paramsFoldout.Add(definition);

            root.Add(_paramsFoldout);
        }

        private void RebuildParamsUi()
        {
            if (_paramsLiveContainer == null)
            {
                return;
            }

            _paramsLiveContainer.Clear();
            _paramsDefinitionContainer.Clear();
            _paramsSignature = ComputeParamsSignature(_target);

            if (_target == null)
            {
                return;
            }

            if (_serializedTarget != null)
            {
                var paramsProp = _serializedTarget.FindProperty("Params");
                if (paramsProp != null)
                {
                    var field = new PropertyField(paramsProp, "Params");
                    field.Bind(_serializedTarget);
                    // 定義(個数/ラベル/型)が変わったときだけ即時反映コントロールを作り直す
                    // (Default 値の編集ごとに作り直すとフォーカスが飛ぶ)。
                    field.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
                    {
                        RebuildParamsUiIfChanged();
                        RefreshValidation();
                    });
                    _paramsDefinitionContainer.Add(field);
                }
            }

            if (_target.Params == null || _target.Params.Length == 0)
            {
                _paramsLiveContainer.Add(new Label("公開パラメータはありません。下の「定義の追加・削除」で Label と TargetProperty(シェーダープロパティ名)を登録すると、ここにスライダーが出ます。")
                {
                    style = { opacity = 0.6f, whiteSpace = WhiteSpace.Normal },
                });
                return;
            }

            for (var i = 0; i < _target.Params.Length; i++)
            {
                _paramsLiveContainer.Add(BuildParamControl(_target.Params[i], i));
            }
        }

        private void RebuildParamsUiIfChanged()
        {
            if (_paramsLiveContainer == null)
            {
                return;
            }

            if (ComputeParamsSignature(_target) != _paramsSignature)
            {
                RebuildParamsUi();
            }
        }

        private static string ComputeParamsSignature(VfxData data)
        {
            if (data == null || data.Params == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (var p in data.Params)
            {
                sb.Append(p.Label).Append('|').Append((int)p.Type).Append('|').Append(p.TargetProperty).Append(';');
            }

            return sb.ToString();
        }

        private VisualElement BuildParamControl(VfxParam param, int index)
        {
            var label = string.IsNullOrEmpty(param.Label) ? $"(Label 未設定 #{index})" : param.Label;
            var tooltip = string.IsNullOrEmpty(param.TargetProperty) ? "TargetProperty が未設定のため反映されません" : $"→ {param.TargetProperty}";

            switch (param.Type)
            {
                case VfxParamType.Float:
                {
                    var field = new FloatField(label) { value = param.Default.FloatValue, tooltip = tooltip };
                    field.RegisterValueChangedCallback(evt => OnParamChanged(index, ParamValue.Of(evt.newValue)));
                    return field;
                }

                case VfxParamType.Int:
                {
                    var field = new IntegerField(label) { value = param.Default.IntValue, tooltip = tooltip };
                    field.RegisterValueChangedCallback(evt => OnParamChanged(index, ParamValue.Of(evt.newValue)));
                    return field;
                }

                case VfxParamType.Color:
                {
                    var field = new ColorField(label) { value = param.Default.ColorValue, tooltip = tooltip };
                    field.RegisterValueChangedCallback(evt => OnParamChanged(index, ParamValue.Of(evt.newValue)));
                    return field;
                }

                case VfxParamType.Vector:
                {
                    var field = new Vector4Field(label) { value = param.Default.VectorValue, tooltip = tooltip };
                    field.RegisterValueChangedCallback(evt =>
                        OnParamChanged(index, new ParamValue { Type = ParamValueType.Vector, VectorValue = evt.newValue }));
                    return field;
                }

                case VfxParamType.Texture:
                {
                    var field = new ObjectField(label) { objectType = typeof(Texture), value = param.Default.ObjectValue, tooltip = tooltip };
                    field.RegisterValueChangedCallback(evt =>
                        OnParamChanged(index, new ParamValue { Type = ParamValueType.Object, ObjectValue = evt.newValue }));
                    return field;
                }

                default:
                    return new Label($"{label}({param.Type}): ライブプレビュー未対応(MaterialPropertyBlock 非対応型)") { style = { opacity = 0.6f } };
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
            _serializedTarget?.Update();

            if (_driver.IsPlaying(_mainHandle))
            {
                _driver.Manager.SetParam(_mainHandle, _target.Params[index].Label, value);
            }
        }

        // ── イベント編集(OnSpawn/OnLoop/OnDestroy → SE再生等) ──

        private void BuildEventsSection(VisualElement root)
        {
            _eventsFoldout = new Foldout { text = "イベント(OnSpawn/OnLoop/OnDestroy 等)", value = false };
            root.Add(_eventsFoldout);
        }

        private void RebuildEventsUi()
        {
            if (_eventsFoldout == null)
            {
                return;
            }

            _eventsFoldout.Clear();

            var eventsProp = _serializedTarget?.FindProperty("Events");
            if (eventsProp == null)
            {
                return;
            }

            var field = new PropertyField(eventsProp);
            field.Bind(_serializedTarget);
            _eventsFoldout.Add(field);
        }

        // ── 複数同時再生(最大8スロット) ──

        private void BuildMultiSlotSection(VisualElement root)
        {
            var foldout = new Foldout { text = $"複数同時再生(最大 {MaxSlots}。打撃+火花+煙の重なり確認)", value = false };
            _slots.Clear();

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
            if (_driver.IsPlaying(slot.Handle))
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

        private void ResetSlotsUi()
        {
            foreach (var slot in _slots)
            {
                slot.Handle = Handle<VfxMarker>.Invalid;
                if (slot.ToggleButton != null)
                {
                    slot.ToggleButton.text = "▶";
                }
            }
        }

        // ── 検証(共通の個別検証セクション。AssetBrowser の一括検証と同じ Validator を対象 1 件に対して実行) ──
        // 2026-09-17(U-13): 独自実装から DataValidationSection に置き換えた([09] §11)。

        private void BuildValidationSection(VisualElement root)
        {
            _validationSection = new DataValidationSection();
            root.Add(_validationSection);
        }

        private void RefreshValidation() => _validationSection?.Bind(_target);
    }
}
