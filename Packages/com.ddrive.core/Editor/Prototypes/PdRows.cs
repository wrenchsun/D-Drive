using System;
using System.Collections.Generic;
using DDrive.Foundation.Validation;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.EditorPrototypes
{
    // D の 1 欄: [左端の変更バー] [段バッジ + ラベル(固定幅)] [入力] [↺] + 一言説明 + インライン検証。
    // 複数のフィールドをまとめて 1 つのビジュアル入力にする欄(長さ・位置・レイヤー)にも使う(entries が複数)。
    internal class PdRow : VisualElement
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

        // E: 未調整の折りたたみの対象にしない欄(調整つまみ)。
        public bool NoFold { get; set; }

        // Error / Warning がある(Info だけなら false)。モードで隠れていても見せる条件に使う。
        public bool NeedsAttention { get; private set; }

        public PdRow(
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

            AddToClassList("pd-row");
            if (stacked)
            {
                AddToClassList("pd-row--stacked");
            }

            var bar = new VisualElement();
            bar.AddToClassList("pd-row__bar");
            bar.tooltip = "既定値と違います";
            Add(bar);

            var main = new VisualElement();
            main.AddToClassList("pd-row__main");
            Add(main);

            var line = new VisualElement();
            line.AddToClassList("pd-row__line");
            main.Add(line);

            var labelCol = new VisualElement();
            labelCol.AddToClassList("pd-row__labelcol");
            var badge = new Label(TierName(tier)) { tooltip = TierTooltip(tier) };
            badge.AddToClassList("pd-badge");
            badge.AddToClassList(tier == FieldTier.Required ? "pd-badge--req" : tier == FieldTier.Common ? "pd-badge--com" : "pd-badge--adv");
            labelCol.Add(badge);
            _label = new Label(label) { tooltip = label };
            _label.AddToClassList("pd-row__label");
            labelCol.Add(_label);
            line.Add(labelCol);

            var inputRow = new VisualElement();
            inputRow.AddToClassList("pd-row__inputrow");
            input.AddToClassList("pd-row__input");
            inputRow.Add(input);
            var reset = new Button(ResetClicked) { text = "↺", tooltip = "既定値に戻す" };
            reset.AddToClassList("pd-btn");
            reset.AddToClassList("pd-btn--icon");
            reset.AddToClassList("pd-row__reset");
            if (so == null)
            {
                reset.style.display = DisplayStyle.None; // E: 対応する Data の欄を持たない補助欄
            }
            inputRow.Add(reset);
            line.Add(inputRow);

            _hint = new Label(_hintText);
            _hint.AddToClassList("pd-dim");
            _hint.AddToClassList("pd-row__hint");
            main.Add(_hint);

            _issues = new VisualElement();
            _issues.AddToClassList("pd-issues");
            main.Add(_issues);

            UpdateState();
        }

        public static string TierName(FieldTier t) => t == FieldTier.Required ? "必須" : t == FieldTier.Common ? "よく使う" : "詳細";

        private static string TierTooltip(FieldTier t) =>
            t == FieldTier.Required ? "必須: 空だとエラーになる欄" : t == FieldTier.Common ? "よく使う: 普段触る欄" : "詳細: 既定のままで大丈夫な欄";

        // 1 欄用の入力(PropertyField。RenderLayer だけ LayerField)。ラベルは行側に出すので空にする。
        public static VisualElement MakeInput(SerializedObject so, string field)
        {
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                var missing = new Label($"(欄 {field} が見つかりません)");
                missing.AddToClassList("pd-missing");
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

        public static bool IsCompound(SerializedObject so, string field)
        {
            var prop = so.FindProperty(field);
            return prop != null && (prop.propertyType == SerializedPropertyType.Generic || prop.isArray) && prop.propertyType != SerializedPropertyType.String;
        }

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
                if (prop != null && def != null && !SerializedProperty.DataEquals(prop, def))
                {
                    modified = true;
                    break;
                }
            }

            IsModified = modified;
            EnableInClassList("pd-row--mod", modified);
        }

        public bool Matches(string query)
        {
            for (var i = 0; i < Entries.Count; i++)
            {
                if (FieldGuide.Matches(Entries[i], query))
                {
                    return true;
                }
            }

            return false;
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

            for (var i = 0; i < results.Count; i++)
            {
                _issues.Add(MakeIssue(results[i], _changed));
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
            row.AddToClassList("pd-issue");
            var sev = r.Severity == ValidationSeverity.Error ? "err" : r.Severity == ValidationSeverity.Warning ? "warn" : "info";
            row.AddToClassList("pd-issue--" + sev);
            var icon = new Label(r.Severity == ValidationSeverity.Error ? "✕" : r.Severity == ValidationSeverity.Warning ? "⚠" : "i");
            icon.AddToClassList("pd-issue__icon");
            row.Add(icon);
            var msg = new Label(r.Message) { tooltip = r.Message };
            msg.AddToClassList("pd-issue__msg");
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
                        Debug.LogWarning($"[Prototype D] 検証の修正に失敗しました: {e.Message}");
                    }

                    changed?.Invoke();
                }) { text = "修正" };
                b.AddToClassList("pd-btn");
                b.AddToClassList("pd-btn--small");
                row.Add(b);
            }

            return row;
        }
    }

    // 長さの欄: DurationBar + 数値入力。
    internal sealed class PdLifeRow : PdRow
    {
        private readonly DurationBar _bar;

        public PdLifeRow(SerializedObject so, SerializedObject defaultSo, IReadOnlyList<FieldGuideEntry> entries, string hint, DurationBar bar, Action changed)
            : base(so, defaultSo, entries, "長さ・消え方", hint, FieldTier.Common, true, BuildInput(so, bar), changed)
        {
            _bar = bar;
        }

        private static VisualElement BuildInput(SerializedObject so, DurationBar bar)
        {
            var wrap = new VisualElement();
            wrap.Add(bar);
            var nums = new VisualElement();
            nums.AddToClassList("pd-durbar__nums");
            nums.Add(NumberField(so, "Duration", "続く秒数"));
            nums.Add(NumberField(so, "FadeOutSec", "余韻(秒)"));
            wrap.Add(nums);
            return wrap;
        }

        private static VisualElement NumberField(SerializedObject so, string field, string label)
        {
            var prop = so.FindProperty(field);
            var pf = new PropertyField(prop, label);
            pf.BindProperty(prop);
            return pf;
        }

        public override void Sync() => _bar.Sync();
    }

    // 位置の欄: AnchorPad。
    internal sealed class PdAnchorRow : PdRow
    {
        private readonly AnchorPad _pad;

        public PdAnchorRow(SerializedObject so, SerializedObject defaultSo, IReadOnlyList<FieldGuideEntry> entries, string hint, AnchorPad pad, Action changed)
            : base(so, defaultSo, entries, "出る位置", hint, FieldTier.Common, true, pad, changed)
        {
            _pad = pad;
        }

        public override void Sync() => _pad.Sync();
    }

    // 描画先の欄: Render をセグメント(3D / UI)、RenderLayer をドロップダウンで横並び(RenderLayer は詳細モードのみ)。
    internal sealed class PdRenderRow : PdRow
    {
        private static readonly string[] Names = { "3D ワールド", "UI の上" };

        private readonly SerializedObject _so;
        private readonly Button[] _buttons = new Button[2];
        private readonly VisualElement _layer;
        private readonly Action _changed;
        private readonly Label _uiNote;
        private bool _layerAlways;

        // E: 表示レイヤーをモードに関係なく常に出す(3D のときは薄く表示し、理由を tooltip で示す)。
        public bool LayerAlways
        {
            get => _layerAlways;
            set
            {
                _layerAlways = value;
                _layer.style.display = DisplayStyle.Flex;
                Refresh();
            }
        }

        public PdRenderRow(SerializedObject so, SerializedObject defaultSo, IReadOnlyList<FieldGuideEntry> entries, string hint, Action changed)
            : base(so, defaultSo, entries, "表示先", hint, FieldTier.Common, false, Build(so, out var buttons, out var layer, changed), changed)
        {
            _so = so;
            _changed = changed;
            _buttons = buttons;
            _layer = layer;
            _uiNote = this.Q<Label>("pe-uinote");
            for (var i = 0; i < 2; i++)
            {
                var index = i;
                _buttons[i].clicked += () => SetRender(index);
            }

            Refresh();
        }

        private static VisualElement Build(SerializedObject so, out Button[] buttons, out VisualElement layer, Action changed)
        {
            var row = new VisualElement();
            row.AddToClassList("pd-layerrow");
            var seg = new VisualElement();
            seg.AddToClassList("pd-seg");
            buttons = new Button[2];
            for (var i = 0; i < 2; i++)
            {
                buttons[i] = new Button { text = Names[i] };
                buttons[i].AddToClassList("pd-seg__btn");
                seg.Add(buttons[i]);
            }

            row.Add(seg);
            layer = PdRow.MakeInput(so, "RenderLayer");
            layer.AddToClassList("pd-layerrow__layer");
            layer.tooltip = "表示レイヤー(カメラの映す / 映さない用)";
            row.Add(layer);
            var note = new Label("UI の上に出すときは、表示レイヤーに VfxUI レイヤーを選びます(実機では UI カメラで合成されます)。") { name = "pe-uinote" };
            note.AddToClassList("pd-dim");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.display = DisplayStyle.None;
            var wrap = new VisualElement();
            wrap.Add(row);
            wrap.Add(note);
            return wrap;
        }

        private void SetRender(int index)
        {
            var target = _so.targetObject;
            if (target == null)
            {
                return;
            }

            Undo.RecordObject(target, "表示先を変更");
            _so.Update();
            _so.FindProperty("Render").enumValueIndex = index;
            _so.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            Refresh();
            _changed?.Invoke();
        }

        private void Refresh()
        {
            var index = _so.FindProperty("Render").enumValueIndex;
            for (var i = 0; i < 2; i++)
            {
                _buttons[i].EnableInClassList("pd-seg__btn--on", i == index);
            }

            if (_layerAlways && _layer != null)
            {
                var ui = index == 1;
                _layer.style.opacity = ui ? 1f : 0.55f;
                _layer.tooltip = ui
                    ? "表示レイヤー(UI の上に出すときは VfxUI レイヤーを選ぶ)"
                    : "表示レイヤー(スポーン物の Layer。表示先が「UI の上」のときに主に使います。3D ワールドでも設定できます)";
                if (_uiNote != null)
                {
                    _uiNote.style.display = ui ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }
        }

        public override void Sync() => Refresh();

        public override void OnModeChanged(int mode) => _layer.style.display = _layerAlways || mode >= 2 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // カード: 角丸の枠 + 見出し(14px 太字)+ 一言説明 + 右上「？」。表示できる欄が無いときは既定のままで大丈夫の案内。
    internal sealed class PdCard : VisualElement
    {
        private readonly VisualElement _body;
        private readonly Label _empty;
        private readonly Label _desc;
        private readonly string _descText;

        public List<PdRow> Rows { get; } = new List<PdRow>();
        public VisualElement Body => _body;

        public PdCard(string title, string desc, Action help)
        {
            AddToClassList("pd-card");

            var head = new VisualElement();
            head.AddToClassList("pd-card__head");
            var titles = new VisualElement();
            titles.AddToClassList("pd-card__titles");
            var t = new Label(title);
            t.AddToClassList("pd-card__title");
            titles.Add(t);
            _descText = desc;
            _desc = new Label(desc);
            _desc.AddToClassList("pd-dim");
            _desc.AddToClassList("pd-card__desc");
            titles.Add(_desc);
            head.Add(titles);
            var q = new Button(help) { text = "?", tooltip = "マニュアルを開く" };
            q.AddToClassList("pd-btn");
            q.AddToClassList("pd-btn--icon");
            head.Add(q);
            Add(head);

            _body = new VisualElement();
            _body.AddToClassList("pd-card__body");
            Add(_body);

            _empty = new Label("この設定は既定のままで大丈夫です");
            _empty.AddToClassList("pd-card__empty");
            _empty.style.display = DisplayStyle.None;
            Add(_empty);
        }

        public void AddRow(PdRow row)
        {
            Rows.Add(row);
            _body.Add(row);
        }

        // ── E: 未調整の設定の折りたたみ ──

        private VisualElement _foldBox;
        private VisualElement _foldBody;
        private Button _foldHead;
        private bool _foldOpen;
        private int _foldShown;

        private void EnsureFold()
        {
            if (_foldBox != null)
            {
                return;
            }

            _foldBox = new VisualElement();
            _foldBox.AddToClassList("pe-fold");
            _foldHead = new Button(() =>
            {
                _foldOpen = !_foldOpen;
                UpdateFold();
            });
            _foldHead.AddToClassList("pe-fold__head");
            _foldBox.Add(_foldHead);
            _foldBody = new VisualElement();
            _foldBody.AddToClassList("pe-fold__body");
            _foldBox.Add(_foldBody);
            Insert(IndexOf(_body) + 1, _foldBox);
            _foldBox.style.display = DisplayStyle.None;
        }

        // row を折りたたみ側(folded)か通常側へ移す。
        public void SetFolded(PdRow row, bool folded)
        {
            if (folded)
            {
                EnsureFold();
                if (row.parent != _foldBody)
                {
                    _foldBody.Add(row);
                }
            }
            else if (_foldBody != null && row.parent == _foldBody)
            {
                _body.Add(row);
                // 元の並びに戻す。
                for (var i = 0; i < Rows.Count; i++)
                {
                    if (Rows[i].parent == _body)
                    {
                        Rows[i].BringToFront();
                    }
                }

                for (var i = Rows.Count - 1; i >= 0; i--)
                {
                    if (Rows[i].parent == _body)
                    {
                        Rows[i].SendToBack();
                    }
                }
            }
        }

        public bool IsFolded(PdRow row) => _foldBody != null && row.parent == _foldBody;

        // 折りたたみ内で表示対象になっている欄の数(重複欄を除く)。0 なら見出しごと隠す。
        public void SetFoldShown(int shown)
        {
            _foldShown = shown;
            UpdateFold();
        }

        private void UpdateFold()
        {
            if (_foldBox == null)
            {
                return;
            }

            _foldBox.style.display = _foldShown > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _foldBody.style.display = _foldOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _foldHead.text = (_foldOpen ? "▾ " : "▸ ") + "未調整の設定（" + _foldShown + "）";
            _foldHead.tooltip = "既定値のままの設定です。クリックで開きます";
        }

        // visibleRows: 表示する欄の数。filtering = 検索 / 変更済みだけで絞っている。
        public void SetVisibility(int visibleRows, bool filtering)
        {
            if (visibleRows > 0)
            {
                style.display = DisplayStyle.Flex;
                _body.style.display = DisplayStyle.Flex;
                _empty.style.display = DisplayStyle.None;
                RemoveFromClassList("pd-card--empty");
                return;
            }

            if (filtering)
            {
                style.display = DisplayStyle.None;
                return;
            }

            style.display = DisplayStyle.Flex;
            _body.style.display = DisplayStyle.None;
            _empty.style.display = DisplayStyle.Flex;
            AddToClassList("pd-card--empty");
        }
    }
}
