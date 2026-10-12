using System;
using System.Collections.Generic;
using DDrive.Editor.Anim;
using DDrive.Editor.Ux;
using DDrive.Runtime.Material;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Materials
{
    // 固有(Specific)・シェーダーとの同期・パス / キーワードの欄(docs/1010 UX-8)。
    internal sealed partial class MaterialEditor
    {
        private Label _specificLabel;
        private Button _removeConflictsButton;
        private MaterialPassKeywordView _passKeyword;
        private int _specStatusSig = int.MinValue;

        // ── Specific(調整つまみ) ──

        private UxRow CreateSpecificRow(FieldGuideEntry e)
        {
            var data = Target;
            var panel = new TuneKnobs(new MaterialTuneSource(data), So,
                () => VisualEdited(false, false),
                () =>
                {
                    So?.Update();
                    RunValidation();
                    RefreshAll(false);
                    _previewDirty = true;
                });
            _knobs = panel;
            var row = new TuneKnobsRow(So, DefaultSo, new[] { e },
                "シェーダー専用の値を 1 つ = 1 行で回します。分類のチップ(共通チャンネル / 描画ステート / 接頭辞ごと / その他)で絞れます。値はこの Data に保存されます。",
                panel, RowChanged);
            row.ExtraMatch = q => panel.AnyMatch(q);
            return row;
        }

        // シェーダーとの同期(足りない固有を足す / 共通の値を上書きするものを外す)と、その状態。
        private UxRow CreateSpecificToolsRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            line.Add(MakeButton("シェーダーから固有を同期", null, SyncSpecificFromShader,
                "シェーダーの固有プロパティ(共通チャンネル名の規約に該当しないもの)を既定値で追加する。既存の値は保持。シェーダーを変えたときは自動で行う"));
            _removeConflictsButton = MakeButton("共通チャンネルの重複を削除", null, RemoveSpecificConflicts,
                "Specific に紛れている共通チャンネル名 / 描画ステート名を取り除く(そのままだと Common の値を上書きしてしまう)");
            line.Add(_removeConflictsButton);
            box.Add(line);
            _specificLabel = new Label();
            _specificLabel.AddToClassList("ux-dim");
            _specificLabel.AddToClassList("ux-note");
            box.Add(_specificLabel);
            UpdateSpecificStatus(null);
            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, SyncSpecificStatus);
        }

        private void SyncSpecificFromShader()
        {
            if (Target == null)
            {
                return;
            }

            var report = MaterialSpecificSync.Sync(Target);
            UpdateSpecificStatus(report);
            AfterDataWrite();
            if (report.Changed && _primaryPreview != null)
            {
                RebuildPreview();
            }
        }

        // Common を上書きしてしまう項目を Specific から取り除く。
        private void RemoveSpecificConflicts()
        {
            if (Target == null)
            {
                return;
            }

            var removed = MaterialSpecificSync.RemoveConflicts(Target);
            UpdateSpecificStatus(null);
            if (removed > 0)
            {
                SetLabel(_specificLabel, $"Specific から共通チャンネル名 {removed} 件を削除しました");
                AfterDataWrite();
            }
        }

        // Specific に共通チャンネル名が紛れていないか(Common を黙って上書きする)。
        private static int CountSpecificConflicts(MaterialData mat)
        {
            if (mat.Specific == null || mat.Shader == null)
            {
                return 0;
            }

            var count = 0;
            for (var i = 0; i < mat.Specific.Length; i++)
            {
                if (MaterialSpecificResolver.IsConflicting(mat.Shader, mat.Specific[i].Property))
                {
                    count++;
                }
            }

            return count;
        }

        // 150ms ごとに呼ばれる。シェーダーと Specific の中身が変わったときだけ文言を作り直す。
        private void SyncSpecificStatus()
        {
            if (SpecSig() != _specStatusSig)
            {
                UpdateSpecificStatus(null);
            }
        }

        // シェーダーと Specific(名前の並び)の署名。
        private int SpecSig()
        {
            var sig = 17;
            if (Target == null)
            {
                return sig;
            }

            sig = sig * 31 + (Target.Shader != null ? Target.Shader.GetInstanceID() : 0);
            var sp = Target.Specific;
            sig = sig * 31 + (sp != null ? sp.Length : -1);
            for (var i = 0; sp != null && i < sp.Length; i++)
            {
                sig = sig * 31 + (sp[i].Property != null ? sp[i].Property.GetHashCode() : 0);
            }

            return sig;
        }

        private void UpdateSpecificStatus(MaterialSpecificResolver.MergeReport report)
        {
            if (_specificLabel == null || Target == null)
            {
                return;
            }

            var mat = Target;
            _specStatusSig = SpecSig();

            if (mat.Shader == null)
            {
                SetLabel(_specificLabel, "Shader 未設定(既定の Lit で生成)");
                _removeConflictsButton?.Show(false);
                return;
            }

            var conflicts = CountSpecificConflicts(mat);
            _removeConflictsButton?.Show(conflicts > 0);

            if (report != null && report.Changed)
            {
                SetLabel(_specificLabel, MaterialSpecificSync.Describe(report));
                return;
            }

            var unregistered = MaterialSpecificResolver.FindUnregistered(mat.Specific, mat.Shader);
            var text = unregistered.Count == 0
                ? $"固有 {mat.Specific?.Length ?? 0} 件(シェーダーと同期済み)"
                : $"未登録の固有 {unregistered.Count} 件: {string.Join(", ", unregistered)}";
            if (conflicts > 0)
            {
                text += $" / Common を上書きする項目 {conflicts} 件";
            }

            SetLabel(_specificLabel, text);
        }

        // ── パスとキーワード ──

        private UxRow CreatePassKeywordRow(FieldGuideEntry e)
        {
            var view = new MaterialPassKeywordView(Target, () =>
            {
                So?.Update();
                _previewDirty = true;
                RowChanged();
            });
            _passKeyword = view;
            var entries = new[] { e, Guide.Find(nameof(MaterialData.DisabledPasses)), Guide.Find(nameof(MaterialData.EnabledKeywords)) };
            var row = new UxVisualRow(So, DefaultSo, entries, e.Label, e.Hint, e.Tier, view, RowChanged);
            row.NoFold = true;
            return row;
        }
    }

    // パスの無効化とキーワードの有効化(FC-11。旧 MaterialPassKeywordSection と同じ動作を共通テーマで)。
    //   パス: 選択中シェーダーの LightMode 一覧をチェックボックスで並べ、チェック = 無効化(DisabledPasses に入れる)。
    //   キーワード: 自由入力 + シェーダーが宣言しているキーワードの候補から追加(EnabledKeywords)。
    // 書き込みは Undo.RecordObject + SetDirty。描画の確認はここでは行わない(サムネイル / 確認用シーンで行う)。
    internal sealed class MaterialPassKeywordView : VisualElement, IUxSyncable
    {
        private readonly MaterialData _data;
        private readonly Action _changed;
        private readonly VisualElement _passes = new VisualElement();
        private readonly VisualElement _keywords = new VisualElement();
        private readonly List<string> _lightModes = new List<string>();
        private readonly List<string> _declared = new List<string>();
        private string _pendingKeyword = string.Empty;
        private string _signature = null;

        public MaterialPassKeywordView(MaterialData data, Action changed)
        {
            _data = data;
            _changed = changed;
            AddToClassList("ux-pk");
            Add(_passes);
            Add(_keywords);
            Rebuild();
        }

        // 外からの変更(Inspector・Undo・シェーダーの差し替え)で一覧が変わったときだけ作り直す。
        public void Sync()
        {
            if (BuildSignature() != _signature)
            {
                Rebuild();
            }
        }

        private string BuildSignature()
        {
            if (_data == null)
            {
                return string.Empty;
            }

            return (_data.Shader != null ? _data.Shader.GetInstanceID() : 0) + "|"
                + string.Join(",", _data.DisabledPasses ?? Array.Empty<string>()) + "|"
                + string.Join(",", _data.EnabledKeywords ?? Array.Empty<string>());
        }

        private void Rebuild()
        {
            _signature = BuildSignature();
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

            var passTitle = new Label("無効にするパス(チェック = 無効。LightMode 名)");
            passTitle.AddToClassList("ux-pk__title");
            _passes.Add(passTitle);
            if (_data.Shader == null)
            {
                var note = new Label("Shader が未設定のため一覧を出せません(既定シェーダーのパスは Shader を指定すると選べます)。");
                note.AddToClassList("ux-dim");
                note.AddToClassList("ux-note");
                _passes.Add(note);
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

            var passList = new VisualElement();
            passList.AddToClassList("ux-pk__passes");
            foreach (var name in names)
            {
                var captured = name;
                var inShader = ContainsIgnoreCase(_lightModes, captured);
                var toggle = new Toggle
                {
                    text = inShader ? captured : captured + "(シェーダーに無い)",
                    tooltip = "チェックすると Material.SetShaderPassEnabled(\"" + captured + "\", false) で無効にする",
                };
                toggle.AddToClassList("ux-pk__toggle");
                toggle.SetValueWithoutNotify(ToList(_data.DisabledPasses).Exists(x => string.Equals(x, captured, StringComparison.OrdinalIgnoreCase)));
                toggle.RegisterValueChangedCallback(evt => SetPassDisabled(captured, evt.newValue));
                passList.Add(toggle);
            }

            _passes.Add(passList);

            var kwTitle = new Label("有効にするキーワード");
            kwTitle.AddToClassList("ux-pk__title");
            _keywords.Add(kwTitle);
            if (_data.EnabledKeywords != null)
            {
                foreach (var keyword in _data.EnabledKeywords)
                {
                    if (string.IsNullOrEmpty(keyword))
                    {
                        continue;
                    }

                    var captured = keyword;
                    var row = new VisualElement();
                    row.AddToClassList("ux-pk__kw");
                    var label = new Label(captured);
                    label.AddToClassList("ux-pk__kwname");
                    row.Add(label);
                    var remove = new Button(() => RemoveKeyword(captured)) { text = "削除", tooltip = "このキーワードを外す" };
                    remove.AddToClassList("ux-btn");
                    remove.AddToClassList("ux-btn--small");
                    row.Add(remove);
                    _keywords.Add(row);
                }
            }

            var addRow = new VisualElement();
            addRow.AddToClassList("ux-toolsline");
            var field = new TextField { tooltip = "キーワード名(例: _MY_FEATURE)" };
            field.AddToClassList("ux-pk__input");
            field.SetValueWithoutNotify(_pendingKeyword);
            field.RegisterValueChangedCallback(evt => _pendingKeyword = evt.newValue);
            addRow.Add(field);
            var add = new Button(() => AddKeyword(_pendingKeyword)) { text = "追加", tooltip = "入力したキーワードを足す" };
            add.AddToClassList("ux-btn");
            addRow.Add(add);
            _keywords.Add(addRow);

            if (_declared.Count > 0)
            {
                var candidates = new List<string> { "(候補から追加)" };
                candidates.AddRange(_declared);
                var popup = new PopupField<string>(candidates, 0) { tooltip = "シェーダーが宣言しているキーワード" };
                popup.AddToClassList("ux-pk__input");
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
            var index = list.FindIndex(x => string.Equals(x, pass, StringComparison.OrdinalIgnoreCase));
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
            _signature = BuildSignature();
            _changed?.Invoke();
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
            Rebuild();
            _changed?.Invoke();
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
            Rebuild();
            _changed?.Invoke();
        }

        // LightMode 名は大文字小文字を区別しない(Material.SetShaderPassEnabled と同じ)。
        private static bool ContainsIgnoreCase(List<string> list, string name)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> ToList(string[] array) => array != null ? new List<string>(array) : new List<string>();
    }
}
