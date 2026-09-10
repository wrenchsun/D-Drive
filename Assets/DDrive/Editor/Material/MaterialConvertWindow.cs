using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Menu;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Material;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Materials
{
    // [06_material_texture.md] A-2「相互変換機能」— 変換エディタ(チケット 3-6、2026-09-10)。
    // MaterialData(ShaderA) を別シェーダーへ変換する。共通データは維持し、固有パラメータは ShaderConversionTable で付け替え、
    // 結果(維持 / 対応 / 破棄)を差分プレビューとして表示してから「新規 Data として作成」か「この Data を変換」(Undo 対応)を行う。
    // 変換前後の見た目比較(球の並列表示)は 3-9 で MaterialEditorWindow 側に付ける。
    [DDrive.Editor.Inspector.DataEditor(typeof(MaterialData), "シェーダー変換", Order = 10)]
    public sealed class MaterialConvertWindow : EditorWindow
    {
        private MaterialData _source;
        private Shader _target;
        private MaterialConverter.Result _result;
        private readonly List<ShaderConversionTable> _tables = new();

        private ScrollView _root;
        private ObjectField _sourceField;
        private ObjectField _shaderField;
        private Label _summary;
        private VisualElement _diff;
        private Button _createButton;
        private Button _applyButton;

        [MenuItem(DDriveMenu.Editors + "Material 変換")]
        public static void OpenFromMenu() => Open(Selection.activeObject as MaterialData);

        public static void Open(MaterialData target)
        {
            var window = GetWindow<MaterialConvertWindow>("Material 変換");
            window.minSize = new Vector2(480, 360);
            if (target != null)
            {
                window.SetSource(target);
            }
        }

        public void CreateGUI()
        {
            _root = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            rootVisualElement.Add(_root);

            _sourceField = new ObjectField("変換元 MaterialData") { objectType = typeof(MaterialData), allowSceneObjects = false };
            _sourceField.RegisterValueChangedCallback(evt => SetSource(evt.newValue as MaterialData));
            _root.Add(_sourceField);

            _shaderField = new ObjectField("変換先 Shader") { objectType = typeof(Shader), allowSceneObjects = false };
            _shaderField.RegisterValueChangedCallback(evt =>
            {
                _target = evt.newValue as Shader;
                Refresh();
            });
            _root.Add(_shaderField);

            _root.Add(new Button(ReloadTables) { text = "変換テーブルを再読み込み", tooltip = "プロジェクト内の ShaderConversionTable を集め直す" });

            _summary = new Label { style = { marginTop = 6, marginBottom = 6, whiteSpace = WhiteSpace.Normal } };
            _root.Add(_summary);

            _diff = new VisualElement();
            _root.Add(_diff);

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8 } };
            _createButton = new Button(CreateConverted) { text = "新規 MaterialData として作成", tooltip = "変換元は変えずに、同じカテゴリへ変換後の Data を作る" };
            _applyButton = new Button(ApplyInPlace) { text = "この Data を変換(Undo 可)", tooltip = "変換元の Shader と Specific を書き換える" };
            buttons.Add(_createButton);
            buttons.Add(_applyButton);
            _root.Add(buttons);

            ReloadTables();
            if (_source != null)
            {
                SetSource(_source);
            }
            else if (Selection.activeObject is MaterialData selected)
            {
                SetSource(selected);
            }
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject is MaterialData selected && selected != _source)
            {
                SetSource(selected);
            }
        }

        private void SetSource(MaterialData source)
        {
            _source = source;
            if (_root == null)
            {
                return;
            }

            _sourceField.SetValueWithoutNotify(source);
            Refresh();
        }

        private void ReloadTables()
        {
            _tables.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(ShaderConversionTable)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Tests/"))
                {
                    continue;
                }

                var table = AssetDatabase.LoadAssetAtPath<ShaderConversionTable>(path);
                if (table != null)
                {
                    _tables.Add(table);
                }
            }

            Refresh();
        }

        private void Refresh()
        {
            if (_root == null)
            {
                return;
            }

            _diff.Clear();
            _result = null;
            var ready = _source != null && _target != null;
            _createButton.SetEnabled(ready);
            _applyButton.SetEnabled(ready && _source.Shader != _target);
            if (!ready)
            {
                _summary.text = _source == null ? "変換元の MaterialData を選択してください" : "変換先の Shader を選択してください";
                return;
            }

            _result = MaterialConverter.Convert(_source, _target, _tables);
            var fromName = _source.Shader != null ? _source.Shader.name : "(既定 Lit)";
            _summary.text = $"{fromName} → {_target.name}\n" +
                            $"共通データ(Albedo / Normal / Mask / Emission / Blend)はそのまま維持。固有: 対応 {_result.MappedCount} / 維持 {_result.KeptCount} / " +
                            $"破棄 {_result.DroppedCount} / 意図的に破棄 {_result.DiscardedCount}" +
                            (_result.UsedTable ? "  (変換テーブル使用)" : $"  (この組の変換テーブル無し。{_tables.Count} 件の Table を確認)");

            if (_result.Entries.Count == 0)
            {
                _diff.Add(new Label("固有パラメータはありません") { style = { marginLeft = 8 } });
                return;
            }

            foreach (var entry in _result.Entries)
            {
                var (mark, color, text) = entry.Outcome switch
                {
                    MaterialConverter.Outcome.Mapped => ("→", new Color(0.5f, 0.9f, 0.5f), $"{entry.FromProperty} → {entry.ToProperty}  {Describe(entry.Value)}"),
                    MaterialConverter.Outcome.Kept => ("=", new Color(0.7f, 0.8f, 1f), $"{entry.FromProperty}  {Describe(entry.Value)}(同名で維持)"),
                    MaterialConverter.Outcome.Discarded => ("−", new Color(0.7f, 0.7f, 0.7f), $"{entry.FromProperty}  (テーブルで破棄指定)"),
                    _ => ("✕", new Color(1f, 0.5f, 0.5f), $"{entry.FromProperty}  {Describe(entry.Value)}(変換先に無いため破棄されます)"),
                };
                var row = new Label($"{mark} {text}") { style = { marginLeft = 8, color = color, whiteSpace = WhiteSpace.Normal } };
                _diff.Add(row);
            }
        }

        private static string Describe(ParamValue value) => value.Type switch
        {
            ParamValueType.Float => value.FloatValue.ToString("0.###"),
            ParamValueType.Int => value.IntValue.ToString(),
            ParamValueType.Bool => value.BoolValue ? "true" : "false",
            ParamValueType.Color => $"#{ColorUtility.ToHtmlStringRGBA(value.ColorValue)}",
            ParamValueType.Vector => value.VectorValue.ToString(),
            ParamValueType.Object => value.ObjectValue != null ? value.ObjectValue.name : "(null)",
            _ => value.Type.ToString(),
        };

        private void CreateConverted()
        {
            if (_source == null || _result == null)
            {
                return;
            }

            var baseName = string.IsNullOrEmpty(_source.DisplayName) ? _source.name : _source.DisplayName;
            var identifier = AssetNamingService.ToIdentifier(baseName + " " + _target.name.Substring(_target.name.LastIndexOf('/') + 1), "Mat");
            var source = _source;
            var result = _result;
            var created = AssetCreationService.Create(typeof(MaterialData), AssetType.Material, baseName + " (" + _target.name + ")", _source.Category, identifier,
                data => MaterialConverter.ApplyTo((MaterialData)data, source, result));
            if (created != null)
            {
                EditorGUIUtility.PingObject(created);
                Selection.activeObject = created;
            }
        }

        private void ApplyInPlace()
        {
            if (_source == null || _result == null)
            {
                return;
            }

            Undo.RecordObject(_source, "Convert Material Shader");
            MaterialConverter.ApplyTo(_source, null, _result);
            EditorUtility.SetDirty(_source);
            Refresh();
        }
    }
}
