using System.Collections.Generic;
using DDrive.Runtime.Material;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Materials
{
    // FC-11(2026-10-03): Material Editor の「Passes / Keywords」欄。
    //   パス: 選択中シェーダーの LightMode 一覧をチェックボックスで並べ、チェック = 無効化(DisabledPasses に入れる)。
    //   キーワード: 自由入力 + シェーダーが宣言しているキーワードの候補から追加(EnabledKeywords)。
    // 書き込みは Undo.RecordObject + SetDirty。描画の確認はここでは行わない(確認用シーン / SceneView で行う)。
    public sealed class MaterialPassKeywordSection : Foldout
    {
        private readonly VisualElement _passes = new VisualElement();
        private readonly VisualElement _keywords = new VisualElement();
        private readonly List<string> _lightModes = new List<string>();
        private readonly List<string> _declared = new List<string>();
        private MaterialData _data;
        private string _pendingKeyword = string.Empty;

        public MaterialPassKeywordSection()
        {
            text = "Passes / Keywords";
            value = true;
            tooltip = "パスの無効化(LightMode 名)とシェーダーキーワードの有効化。Common / Specific の適用後に反映される。";
            Add(_passes);
            Add(_keywords);
        }

        public void Bind(MaterialData data)
        {
            _data = data;
            Refresh();
        }

        public void Refresh()
        {
            _passes.Clear();
            _keywords.Clear();
            if (_data == null)
            {
                return;
            }

            _lightModes.Clear();
            _declared.Clear();
            MaterialShaderInfo.CollectLightModes(_data.Shader, _lightModes);
            MaterialShaderInfo.CollectKeywords(_data.Shader, _declared);

            _passes.Add(new Label("無効にするパス(チェック = 無効。LightMode 名)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 2 } });
            if (_data.Shader == null)
            {
                _passes.Add(new Label("Shader が未設定のため一覧を出せません(既定シェーダーのパスは Shader を指定すると選べます)。") { style = { whiteSpace = WhiteSpace.Normal } });
            }

            // シェーダーのパス + 既に指定済みだがシェーダーに無い名前(外せるように出す)。
            var names = new List<string>(_lightModes);
            if (_data.DisabledPasses != null)
            {
                foreach (var pass in _data.DisabledPasses)
                {
                    if (!string.IsNullOrEmpty(pass) && !ContainsIgnoreCase(names, pass))
                    {
                        names.Add(pass);
                    }
                }
            }

            foreach (var name in names)
            {
                var captured = name;
                var inShader = ContainsIgnoreCase(_lightModes, captured);
                var toggle = new Toggle(inShader ? captured : captured + "(シェーダーに無い)")
                {
                    tooltip = "チェックすると Material.SetShaderPassEnabled(\"" + captured + "\", false) で無効にする",
                };
                toggle.SetValueWithoutNotify(ToList(_data.DisabledPasses).Exists(x => string.Equals(x, captured, System.StringComparison.OrdinalIgnoreCase)));
                toggle.RegisterValueChangedCallback(evt => SetPassDisabled(captured, evt.newValue));
                _passes.Add(toggle);
            }

            _keywords.Add(new Label("有効にするキーワード") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4 } });
            if (_data.EnabledKeywords != null)
            {
                foreach (var keyword in _data.EnabledKeywords)
                {
                    if (string.IsNullOrEmpty(keyword))
                    {
                        continue;
                    }

                    var captured = keyword;
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                    row.Add(new Label(captured) { style = { flexGrow = 1 } });
                    row.Add(new Button(() => RemoveKeyword(captured)) { text = "削除" });
                    _keywords.Add(row);
                }
            }

            var addRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var field = new TextField { tooltip = "キーワード名(例: _MY_FEATURE)", style = { flexGrow = 1 } };
            field.SetValueWithoutNotify(_pendingKeyword);
            field.RegisterValueChangedCallback(evt => _pendingKeyword = evt.newValue);
            addRow.Add(field);
            addRow.Add(new Button(() => AddKeyword(_pendingKeyword)) { text = "追加" });
            _keywords.Add(addRow);

            if (_declared.Count > 0)
            {
                var candidates = new List<string> { "(候補から追加)" };
                candidates.AddRange(_declared);
                var popup = new PopupField<string>(candidates, 0) { tooltip = "シェーダーが宣言しているキーワード" };
                popup.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue != candidates[0])
                    {
                        AddKeyword(evt.newValue);
                    }
                });
                _keywords.Add(popup);
            }
        }

        private void SetPassDisabled(string pass, bool disabled)
        {
            var list = ToList(_data.DisabledPasses);
            var index = list.FindIndex(x => string.Equals(x, pass, System.StringComparison.OrdinalIgnoreCase));
            if (disabled == (index >= 0))
            {
                return;
            }

            Undo.RecordObject(_data, "Material DisabledPasses");
            if (disabled)
            {
                list.Add(pass);
            }
            else
            {
                list.RemoveAt(index);
            }

            _data.DisabledPasses = list.ToArray();
            EditorUtility.SetDirty(_data);
        }

        private void AddKeyword(string keyword)
        {
            keyword = keyword?.Trim();
            if (string.IsNullOrEmpty(keyword))
            {
                return;
            }

            var list = ToList(_data.EnabledKeywords);
            if (list.Contains(keyword))
            {
                return;
            }

            Undo.RecordObject(_data, "Material EnabledKeywords");
            list.Add(keyword);
            _data.EnabledKeywords = list.ToArray();
            EditorUtility.SetDirty(_data);
            _pendingKeyword = string.Empty;
            Refresh();
        }

        private void RemoveKeyword(string keyword)
        {
            var list = ToList(_data.EnabledKeywords);
            if (!list.Remove(keyword))
            {
                return;
            }

            Undo.RecordObject(_data, "Material EnabledKeywords");
            _data.EnabledKeywords = list.ToArray();
            EditorUtility.SetDirty(_data);
            Refresh();
        }

        // LightMode 名は大文字小文字を区別しない(Material.SetShaderPassEnabled と同じ)。
        private static bool ContainsIgnoreCase(List<string> list, string name)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> ToList(string[] array) => array != null ? new List<string>(array) : new List<string>();
    }
}
