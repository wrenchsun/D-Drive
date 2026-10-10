using System;
using DDrive.Foundation.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.EditorPrototypes
{
    // 案内付きの 1 欄: [● 変更済み目印] [PropertyField] [↺ 既定に戻す] + 一言説明。
    internal sealed class GuidedField : VisualElement
    {
        private static readonly Color ModifiedColor = new Color(1f, 0.65f, 0.15f);

        private readonly SerializedObject _so;
        private readonly SerializedObject _defaultSo;
        private readonly bool _showReset;
        private readonly Action _changed;
        private readonly Label _marker;
        private readonly Button _reset;

        public FieldGuideEntry Entry { get; }
        public bool IsModified { get; private set; }

        // 外部(検索・段階表示)が付ける表示条件。Apply で 1 つにまとめる。
        public bool HiddenByFilter { get; set; }

        public GuidedField(SerializedObject so, SerializedObject defaultSo, FieldGuideEntry entry, bool showReset, bool tierBadge, Action changed)
        {
            _so = so;
            _defaultSo = defaultSo;
            _showReset = showReset;
            _changed = changed;
            Entry = entry;

            style.marginBottom = 4;

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.FlexStart } };
            Add(row);

            _marker = new Label(string.Empty) { tooltip = "既定値と違います" };
            _marker.style.width = 14;
            _marker.style.flexShrink = 0;
            _marker.style.color = ModifiedColor;
            _marker.style.unityTextAlign = TextAnchor.UpperCenter;
            row.Add(_marker);

            var prop = so.FindProperty(entry.Field);
            VisualElement input;
            if (prop == null)
            {
                input = new Label($"(欄 {entry.Field} が見つかりません)") { style = { opacity = 0.6f } };
            }
            else if (entry.Field == "RenderLayer")
            {
                var layer = new LayerField(entry.Label);
                layer.BindProperty(prop);
                input = layer;
            }
            else
            {
                var pf = new PropertyField(prop, entry.Label);
                pf.BindProperty(prop);
                input = pf;
            }

            input.style.flexGrow = 1;
            input.style.flexShrink = 1;
            input.style.minWidth = 0;
            row.Add(input);

            _reset = new Button(ResetToDefault) { text = "↺", tooltip = "既定値に戻す" };
            _reset.style.flexShrink = 0;
            _reset.style.display = DisplayStyle.None;
            row.Add(_reset);

            var hintText = entry.Hint ?? string.Empty;
            if (tierBadge)
            {
                hintText = (entry.Tier == FieldTier.Required ? "【必須】" : entry.Tier == FieldTier.Common ? "【よく使う】" : "【詳細】") + hintText;
            }

            var hint = new Label(hintText);
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.opacity = 0.6f;
            hint.style.fontSize = 10;
            hint.style.marginLeft = 14;
            Add(hint);
        }

        public void Apply(bool visible) =>
            style.display = visible && !HiddenByFilter ? DisplayStyle.Flex : DisplayStyle.None;

        // 既定値(CreateInstance の値)と比べて目印・戻すボタンを更新する。
        public void UpdateState()
        {
            var prop = _so.FindProperty(Entry.Field);
            var def = _defaultSo?.FindProperty(Entry.Field);
            IsModified = prop != null && def != null && !SerializedProperty.DataEquals(prop, def);
            _marker.text = IsModified ? "●" : string.Empty;
            _reset.style.display = IsModified && _showReset ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ResetToDefault()
        {
            var target = _so.targetObject;
            if (target == null || _defaultSo == null)
            {
                return;
            }

            Undo.RecordObject(target, "既定に戻す");
            _so.Update();
            _so.CopyFromSerializedProperty(_defaultSo.FindProperty(Entry.Field));
            _so.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            _changed?.Invoke();
        }
    }

    internal static class FieldGuideUi
    {
        // FieldGuideEntry.Field から FindProperty して案内付きの欄を作る共通ヘルパー。
        public static GuidedField MakeField(SerializedObject so, SerializedObject defaultSo, FieldGuideEntry entry, bool showResetButton, bool tierBadge = false, Action changed = null)
        {
            var f = new GuidedField(so, defaultSo, entry, showResetButton, tierBadge, changed);
            f.UpdateState();
            return f;
        }

        // 読み取り専用の管理情報(Id / Version / Author / UpdatedAt)。
        public static VisualElement MakeReadOnlyInfo(AssetDataBase data)
        {
            var box = new VisualElement { style = { marginTop = 2, marginBottom = 4 } };
            if (data == null)
            {
                return box;
            }

            box.Add(InfoLabel("Id", data.Id.ToString()));
            box.Add(InfoLabel("Version", data.Version.ToString()));
            box.Add(InfoLabel("最終更新者", string.IsNullOrEmpty(data.Author) ? "-" : data.Author));
            box.Add(InfoLabel("最終更新日時", string.IsNullOrEmpty(data.UpdatedAt) ? "-" : data.UpdatedAt));
            return box;
        }

        private static Label InfoLabel(string key, string value)
        {
            var l = new Label($"{key}: {value}");
            l.style.opacity = 0.6f;
            l.style.fontSize = 10;
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        public static VisualElement Card(string title, out VisualElement content, VisualElement headerExtra = null)
        {
            var card = new VisualElement();
            card.style.borderTopWidth = card.style.borderBottomWidth = card.style.borderLeftWidth = card.style.borderRightWidth = 1;
            var c = new Color(0.5f, 0.5f, 0.5f, 0.45f);
            card.style.borderTopColor = card.style.borderBottomColor = card.style.borderLeftColor = card.style.borderRightColor = c;
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius = card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 4;
            card.style.paddingLeft = card.style.paddingRight = 6;
            card.style.paddingTop = card.style.paddingBottom = 4;
            card.style.marginBottom = 6;

            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var t = new Label(title) { style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1, flexShrink = 1, whiteSpace = WhiteSpace.Normal } };
            header.Add(t);
            if (headerExtra != null)
            {
                header.Add(headerExtra);
            }

            card.Add(header);
            content = new VisualElement { style = { marginTop = 2 } };
            card.Add(content);
            return card;
        }
    }
}
