using System;
using System.Collections.Generic;
using DDrive.Foundation.Validation;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ux
{
    // 1 欄(試験版 D / E の PdRow を移植): [左端の変更バー] [段バッジ + ラベル(固定幅)] [入力] [↺] + 一言説明 + インライン検証。
    // 複数のフィールドをまとめて 1 つのビジュアル入力にする欄(長さ・位置・レイヤー)にも使う(entries が複数)。
    internal class UxRow : VisualElement
    {
        private readonly SerializedObject _so;
        private readonly SerializedObject _defaultSo;
        private readonly Action _changed;
        private readonly Label _label;
        private readonly Label _hint;
        private readonly VisualElement _issues;
        private readonly string _labelText;
        private readonly string _hintText;

        public string Label => _labelText;

        public IReadOnlyList<FieldGuideEntry> Entries { get; }
        public FieldTier Tier { get; }
        public bool IsModified { get; private set; }

        // 同じフィールドを別の見せ方でもう一度出す欄(Anchor の全設定)。変更件数・一括リセットでは数えない。
        public bool IsDuplicate { get; set; }
        public bool HasIssues { get; private set; }

        // 未調整の折りたたみの対象にしない欄(調整つまみなど)。
        public bool NoFold { get; set; }

        // 「すべて既定に戻す」の対象にしない欄(要素の一覧など。全部消えてしまう欄)。行の ↺ も出さない。
        public bool NoBulkReset
        {
            get => _noBulkReset;
            set
            {
                _noBulkReset = value;
                _reset?.Show(!value && _so != null);
            }
        }

        // 検索で、表の語(ラベル・説明・検索語)のほかに一致とみなす判定(一覧の中の要素名など。null = 使わない)。
        public Func<string, bool> ExtraMatch { get; set; }

        // 欄の下に出す検査の最大件数(一覧の欄で、行ごとの印に任せて件数を絞る。超えた分は「ほか N 件」)。
        public int MaxIssues { get; set; } = int.MaxValue;

        private bool _noBulkReset;
        private Button _reset;

        // Error / Warning がある(Info だけなら false)。モードで隠れていても見せる条件に使う。
        public bool NeedsAttention { get; private set; }

        public UxRow(
            SerializedObject so,
            SerializedObject defaultSo,
            IReadOnlyList<FieldGuideEntry> entries,
            string label,
            string hint,
            FieldTier tier,
            bool stacked,
            VisualElement input,
            Action changed)
        {
            _so = so;
            _defaultSo = defaultSo;
            _changed = changed;
            _labelText = label;
            _hintText = hint ?? string.Empty;
            Entries = entries;
            Tier = tier;

            AddToClassList("ux-row");
            if (stacked)
            {
                AddToClassList("ux-row--stacked");
            }

            var bar = new VisualElement();
            bar.AddToClassList("ux-row__bar");
            bar.tooltip = "既定値と違います";
            Add(bar);

            var main = new VisualElement();
            main.AddToClassList("ux-row__main");
            Add(main);

            var line = new VisualElement();
            line.AddToClassList("ux-row__line");
            main.Add(line);

            var labelCol = new VisualElement();
            labelCol.AddToClassList("ux-row__labelcol");
            var badge = new Label(TierName(tier)) { tooltip = TierTooltip(tier) };
            badge.AddToClassList("ux-badge");
            badge.AddToClassList(tier == FieldTier.Required ? "ux-badge--req" : tier == FieldTier.Common ? "ux-badge--com" : "ux-badge--adv");
            labelCol.Add(badge);
            _label = new Label(label) { tooltip = label };
            _label.AddToClassList("ux-row__label");
            labelCol.Add(_label);
            line.Add(labelCol);

            var inputRow = new VisualElement();
            inputRow.AddToClassList("ux-row__inputrow");
            input.AddToClassList("ux-row__input");
            inputRow.Add(input);
            var reset = new Button(ResetClicked) { text = "↺", tooltip = "既定値に戻す" };
            _reset = reset;
            reset.AddToClassList("ux-btn");
            reset.AddToClassList("ux-btn--icon");
            reset.AddToClassList("ux-row__reset");
            if (so == null)
            {
                reset.Show(false); // 対応する Data の欄を持たない補助欄
            }
            inputRow.Add(reset);
            line.Add(inputRow);

            _hint = new Label(_hintText);
            _hint.AddToClassList("ux-dim");
            _hint.AddToClassList("ux-row__hint");
            main.Add(_hint);

            _issues = new VisualElement();
            _issues.AddToClassList("ux-issues");
            main.Add(_issues);

            UpdateState();
        }

        public static string TierName(FieldTier t) => t == FieldTier.Required ? "必須" : t == FieldTier.Common ? "よく使う" : "詳細";

        private static string TierTooltip(FieldTier t) =>
            t == FieldTier.Required ? "必須: 空だとエラーになる欄" : t == FieldTier.Common ? "よく使う: 普段触る欄" : "詳細: 既定のままで大丈夫な欄";

        public static VisualElement MakeInput(SerializedObject so, string field) => GuidedField.MakeInput(so, field);

        public static bool IsCompound(SerializedObject so, string field) => GuidedField.IsCompound(so, field);

        public virtual void OnModeChanged(int mode) { }

        // 外部変更を取り込む(ビジュアル入力の再描画など)。
        public virtual void Sync() { }

        public void UpdateState()
        {
            if (_so == null)
            {
                return;
            }

            var modified = false;
            for (var i = 0; i < Entries.Count; i++)
            {
                var prop = _so.FindProperty(Entries[i].Field);
                var def = _defaultSo?.FindProperty(Entries[i].Field);
                if (prop == null || def == null)
                {
                    continue;
                }

                var check = Entries[i].DefaultCheck;
                var same = check != null ? check(prop, def) : SerializedProperty.DataEquals(prop, def);
                if (!same)
                {
                    modified = true;
                    break;
                }
            }

            IsModified = modified;
            EnableInClassList("ux-row--mod", modified);
        }

        public bool Matches(string query)
        {
            for (var i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Matches(query))
                {
                    return true;
                }
            }

            return ExtraMatch != null && ExtraMatch(query);
        }

        // 一致した語をラベル / 説明の中で強調する(リッチテキストの <mark>)。検索語だけに一致したときは説明の末尾に出す。
        public void Highlight(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || query.IndexOfAny(new[] { '<', '>' }) >= 0)
            {
                _label.text = _labelText;
                _hint.text = _hintText;
                return;
            }

            var q = query.Trim();
            _label.text = Mark(_labelText, q);
            var hint = Mark(_hintText, q);
            if (_label.text == _labelText && hint == _hintText)
            {
                var kw = FindKeyword(q);
                if (kw != null)
                {
                    hint += $"  · 検索語: <mark=#FFC84A66><b>{kw}</b></mark>";
                }
            }

            _hint.text = hint;
        }

        private string FindKeyword(string q)
        {
            for (var i = 0; i < Entries.Count; i++)
            {
                var kws = Entries[i].Keywords;
                if (kws == null)
                {
                    continue;
                }

                for (var k = 0; k < kws.Length; k++)
                {
                    if (kws[k].IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return kws[k];
                    }
                }
            }

            return null;
        }

        private static string Mark(string text, string q)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var idx = text.IndexOf(q, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return text;
            }

            return text.Substring(0, idx) + "<mark=#FFC84A66><b>" + text.Substring(idx, q.Length) + "</b></mark>" + text.Substring(idx + q.Length);
        }

        // 既定値へ戻す(Undo / 保存通知は呼び出し側)。
        public void ApplyDefault()
        {
            if (_so == null)
            {
                return;
            }

            for (var i = 0; i < Entries.Count; i++)
            {
                var def = _defaultSo?.FindProperty(Entries[i].Field);
                if (def != null)
                {
                    _so.CopyFromSerializedProperty(def);
                }
            }
        }

        private void ResetClicked()
        {
            var target = _so.targetObject;
            if (target == null || _defaultSo == null)
            {
                return;
            }

            Undo.RecordObject(target, "既定に戻す");
            _so.Update();
            ApplyDefault();
            _so.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            _changed?.Invoke();
        }

        // ── インライン検証(欄の下の 1 行 + 修正ボタン) ──

        private string _issueKey = string.Empty;

        public void SetIssues(List<ValidationResult> results)
        {
            HasIssues = results != null && results.Count > 0;
            NeedsAttention = false;
            for (var i = 0; HasIssues && i < results.Count; i++)
            {
                if (results[i].Severity != ValidationSeverity.Info)
                {
                    NeedsAttention = true;
                }
            }

            var key = HasIssues ? IssueKey(results) : string.Empty;
            if (key == _issueKey)
            {
                return; // 内容が同じなら作り直さない(ホバー・クリック中のボタンを壊さない)
            }

            _issueKey = key;
            _issues.Clear();
            if (!HasIssues)
            {
                return;
            }

            for (var i = 0; i < results.Count && i < MaxIssues; i++)
            {
                _issues.Add(MakeIssue(results[i], _changed));
            }

            if (results.Count > MaxIssues)
            {
                var more = new Label($"ほか {results.Count - MaxIssues} 件の検査結果があります(該当する行の ⚠ にマウスを合わせると内容が出ます)");
                more.AddToClassList("ux-dim");
                more.AddToClassList("ux-note");
                _issues.Add(more);
            }
        }

        public static string IssueKey(List<ValidationResult> results)
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < results.Count; i++)
            {
                sb.Append((int)results[i].Severity).Append(':').Append(results[i].Message).Append(results[i].FixAction != null ? "+" : "-").Append('|');
            }

            return sb.ToString();
        }

        public static VisualElement MakeIssue(ValidationResult r, Action changed)
        {
            var row = new VisualElement();
            row.AddToClassList("ux-issue");
            var sev = r.Severity == ValidationSeverity.Error ? "err" : r.Severity == ValidationSeverity.Warning ? "warn" : "info";
            row.AddToClassList("ux-issue--" + sev);
            var icon = new Label(r.Severity == ValidationSeverity.Error ? "✕" : r.Severity == ValidationSeverity.Warning ? "⚠" : "i");
            icon.AddToClassList("ux-issue__icon");
            row.Add(icon);
            var msg = new Label(r.Message) { tooltip = r.Message };
            msg.AddToClassList("ux-issue__msg");
            row.Add(msg);
            if (r.FixAction != null)
            {
                var fix = r.FixAction;
                var b = new Button(() =>
                {
                    try
                    {
                        fix();
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[D-Drive] 検証の修正に失敗しました: {e.Message}");
                    }

                    changed?.Invoke();
                }) { text = "修正" };
                b.AddToClassList("ux-btn");
                b.AddToClassList("ux-btn--small");
                row.Add(b);
            }

            return row;
        }
    }

    // 視覚部品(DurationBar / AnchorPad など)を 1 つの入力として載せる欄。複数のフィールドをまとめて扱える(entries が複数)。
    internal sealed class UxVisualRow : UxRow
    {
        private readonly IUxSyncable _visual;

        public UxVisualRow(SerializedObject so, SerializedObject defaultSo, IReadOnlyList<FieldGuideEntry> entries, string label, string hint, FieldTier tier, VisualElement visual, Action changed)
            : base(so, defaultSo, entries, label, hint, tier, true, visual, changed)
        {
            _visual = visual as IUxSyncable;
        }

        public override void Sync() => _visual?.Sync();
    }

    // 案内付きの 1 欄の入力部品を作る共通ヘルパー(ラベルは行側が出すので空にする)。
    internal static class GuidedField
    {
        // PropertyField。RenderLayer だけ LayerField(レイヤー名で選ばせる)。
        public static VisualElement MakeInput(SerializedObject so, string field)
        {
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                var missing = new Label($"(欄 {field} が見つかりません)");
                missing.AddToClassList("ux-missing");
                return missing;
            }

            if (field == "RenderLayer")
            {
                var layer = new LayerField(string.Empty);
                layer.BindProperty(prop);
                return layer;
            }

            var pf = new PropertyField(prop, string.Empty);
            pf.BindProperty(prop);
            return pf;
        }

        // 入れ子・配列の欄は入力を縦に積む(stacked)。
        public static bool IsCompound(SerializedObject so, string field)
        {
            var prop = so.FindProperty(field);
            return prop != null && (prop.propertyType == SerializedPropertyType.Generic || prop.isArray) && prop.propertyType != SerializedPropertyType.String;
        }
    }
}
