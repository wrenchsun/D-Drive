using System;
using System.Collections.Generic;
using System.Text;
using DDrive.Foundation.Validation;
using DDrive.Editor.Validation;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ux
{
    // カードと行の組み立て・インライン検証・モード / 検索 / 変更だけの絞り込み・未調整の折りたたみ・一括リセット。
    public abstract partial class UxEditorWindow<TData>
    {
        private readonly List<UxCard> _cards = new List<UxCard>();
        private readonly List<UxRow> _rows = new List<UxRow>();
        private readonly List<UxRow> _changeRows = new List<UxRow>();
        private readonly Dictionary<string, UxRow> _rowByField = new Dictionary<string, UxRow>();

        private UxCard _extraCard;
        private string _extraKey = string.Empty;
        private int _errors;
        private int _warnings;
        private int _signature;

        // ── カードと欄 ──

        private void BuildCards(VisualElement page)
        {
            foreach (var purpose in Guide.Purposes)
            {
                BeforeCard(purpose, page);

                var card = new UxCard(purpose, purpose == FieldGuide<TData>.AdminPurpose ? "一覧に出る名前・分類・メモ" : CardDescription(purpose),
                    UxCard.Help(ManualPage, ManualAnchor(purpose)));
                var isAdmin = purpose == FieldGuide<TData>.AdminPurpose;
                var ordered = new List<FieldGuideEntry>();
                foreach (var entry in Guide.Entries)
                {
                    if (entry.Purpose == purpose)
                    {
                        // 調整つまみはカードの先頭に出す。
                        ordered.Insert(entry.Kind == FieldRowKind.Knobs ? 0 : ordered.Count, entry);
                    }
                }

                foreach (var entry in ordered)
                {
                    var row = CreateRow(entry, card, isAdmin);
                    if (row != null)
                    {
                        AddRow(card, row, isAdmin);
                    }
                }

                if (isAdmin)
                {
                    card.Body.Add(MakeReadOnlyInfo());
                }

                _cards.Add(card);
                page.Add(card);
            }

            _extraCard = new UxCard("その他の検証", "どの欄にも当てはまらない検査結果", UxCard.Help(ManualPage, null));
            _extraCard.Show(false);
            page.Add(_extraCard);
        }

        // 欄を表す行を登録する(変更の件数・検証の結びつけ・一括リセットの対象になる)。
        protected void AddRow(UxCard card, UxRow row, bool isAdmin)
        {
            card.AddRow(row);
            _rows.Add(row);
            if (!row.IsDuplicate)
            {
                foreach (var e in row.Entries)
                {
                    _rowByField[e.Field] = row;
                }

                if (!isAdmin)
                {
                    _changeRows.Add(row);
                }
            }
        }

        // 種別が足すカード(例: Vfx の複数同時再生)を、検索・折りたたみの対象として登録して page に載せる。
        protected void AddCard(VisualElement page, UxCard card)
        {
            _cards.Add(card);
            page.Add(card);
        }

        // Data の欄を持たない補助の行(ツール・スロットなど)を載せる。絞り込み・検索・折りたたみの対象にはなるが、変更の件数・検証の結びつけには入らない。
        protected void AddAuxRow(UxCard card, UxRow row)
        {
            card.AddRow(row);
            _rows.Add(row);
        }

        // カードに載せず、変更件数と検証の結びつけにだけ登録する(別の行が見せている重複欄の本体)。
        protected void RegisterRowOnly(UxRow row, bool countAsChange)
        {
            _rows.Add(row);
            foreach (var e in row.Entries)
            {
                _rowByField[e.Field] = row;
            }

            if (countAsChange)
            {
                _changeRows.Add(row);
            }
        }

        // 通常の欄(PropertyField。入れ子・配列は縦積み)。ReadOnly は編集不可の表示にする。
        protected UxRow CreateDefaultRow(FieldGuideEntry e)
        {
            var compound = GuidedField.IsCompound(So, e.Field);
            var input = GuidedField.MakeInput(So, e.Field);
            if (e.Kind == FieldRowKind.ReadOnly)
            {
                input.SetEnabled(false);
            }

            return new UxRow(So, DefaultSo, new[] { e }, e.Label, e.Hint, e.Tier, compound, input, RowChanged);
        }

        private VisualElement MakeReadOnlyInfo()
        {
            var box = new VisualElement();
            box.AddToClassList("ux-card__empty");
            box.Add(new Label($"Id {Target.Id}   Version {Target.Version}   最終更新者 {(string.IsNullOrEmpty(Target.Author) ? "-" : Target.Author)}   最終更新日時 {(string.IsNullOrEmpty(Target.UpdatedAt) ? "-" : Target.UpdatedAt)}"));
            return box;
        }

        // ── 編集のあと ──

        // 欄の編集が SerializedProperty 変更として届いたとき(PropertyField 経由)。
        private void OnPropertyChanged(SerializedPropertyChangeEvent evt)
        {
            var path = evt.changedProperty?.propertyPath ?? string.Empty;
            OnPropertyEdited(path);
            OnDataEdited();
        }

        // 外部コード(行・つまみ・視覚部品)が Data を書いた後に呼ぶ(ドラッグ中でない確定時)。
        protected void RowChanged()
        {
            So?.Update();
            OnSideEffect();
            RunValidation();
            RefreshAll(false);
        }

        // 再生の撮り直しも要る変更(行の変更 + 再生中なら再生し直す)。
        protected void RowChangedRestart()
        {
            RowChanged();
            RestartIfPlaying();
        }

        // ビジュアル入力(バー・パッド)からの書き込み。ドラッグ中(final = false)は検証を省く。sideEffect = true なら OnSideEffect を呼ぶ。
        protected void VisualEdited(bool final, bool sideEffect)
        {
            So?.Update();
            if (sideEffect)
            {
                OnSideEffect();
            }

            if (final)
            {
                RunValidation();
            }

            RefreshAll(false);
        }

        // 検証・行の状態を最新にする(外から Data を書き換えたとき。Presentation 等から開かれたとき)。
        protected void RefreshAfterExternalWrite()
        {
            So?.Update();
            RunValidation();
            RefreshAll(true);
        }

        private void OnDataEdited()
        {
            if (Target == null || So == null || _rows.Count == 0)
            {
                return;
            }

            RunValidation();
            RefreshAll(true);
        }

        protected void RefreshAll(bool syncVisuals)
        {
            if (Target == null || So == null)
            {
                return;
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                _rows[i].UpdateState();
                if (syncVisuals)
                {
                    _rows[i].Sync();
                }
            }

            OnRowsRefreshed();
            UpdateChips();
            ApplyFilters();
        }

        // 行の状態を更新したあと(例: Vfx の AnchorPad の注記)。
        protected virtual void OnRowsRefreshed() { }

        // ── 検証(欄の下にインライン表示) ──

        protected void RunValidation()
        {
            if (Target == null || _rows.Count == 0)
            {
                return;
            }

            var results = DataValidationRunner.Run(Target, ValidationIncludesSameTypeAssets);
            AppendValidation(results);
            _errors = 0;
            _warnings = 0;
            var byRow = new Dictionary<UxRow, List<ValidationResult>>();
            var extra = new List<ValidationResult>();
            for (var i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (r.Severity == ValidationSeverity.Error)
                {
                    _errors++;
                }
                else if (r.Severity == ValidationSeverity.Warning)
                {
                    _warnings++;
                }

                var row = FindRowForResult(r);
                if (row == null)
                {
                    extra.Add(r);
                    continue;
                }

                if (!byRow.TryGetValue(row, out var list))
                {
                    list = new List<ValidationResult>();
                    byRow[row] = list;
                }

                list.Add(r);
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                byRow.TryGetValue(_rows[i], out var list);
                _rows[i].SetIssues(list);
                if (_rows[i].NeedsAttention)
                {
                    foreach (var card in _cards)
                    {
                        if (card.IsFolded(_rows[i]))
                        {
                            card.SetFolded(_rows[i], false);
                        }
                    }
                }
            }

            OnValidated(results);

            var extraKey = UxRow.IssueKey(extra);
            if (_extraCard != null && extraKey != _extraKey)
            {
                _extraKey = extraKey;
                _extraCard.Body.Clear();
                for (var i = 0; i < extra.Count; i++)
                {
                    _extraCard.Body.Add(UxRow.MakeIssue(extra[i], RowChanged));
                }
            }

            _extraCard?.Show(extra.Count > 0);
            UpdateChips();
        }

        // 他の Data との関係(入れ子・循環)を見る Validator のために、同じ型の Data すべてを検査の文脈に載せるか(Anchor / AnchorGroup。docs/1010 UX-11)。
        protected virtual bool ValidationIncludesSameTypeAssets => false;

        // 共通の検査(DataValidationRunner)に、種別が足したい検査結果を加える(例: 基底型の Validator も通したい派生型。docs/1010 UX-7)。
        protected virtual void AppendValidation(List<ValidationResult> results) { }

        // 検査のたびに全結果を受け取る(種別が持つ一覧の行ごとの印に使う)。
        protected virtual void OnValidated(IReadOnlyList<ValidationResult> results) { }

        // 結果を欄に結びつける。第一に ValidationResult.Field(Validator が欄名を持たせたもの)、
        // 無ければ旧来のメッセージ照合(Field を付けていない Validator の救済)。
        private UxRow FindRowForResult(ValidationResult r)
        {
            if (!string.IsNullOrEmpty(r.Field) && _rowByField.TryGetValue(r.Field, out var byField))
            {
                return byField;
            }

            return FindRowForMessage(r.Message);
        }

        // 先頭がフィールド名(例: "Params 'x' の…")ならその欄、次に代表語(ValidationTokens)、
        // 最後にメッセージ中で最初に出てくるフィールド名(同位置なら長い名前を優先)。
        private UxRow FindRowForMessage(string msg)
        {
            if (string.IsNullOrEmpty(msg))
            {
                return null;
            }

            string best = null;
            foreach (var kv in _rowByField)
            {
                if (msg.StartsWith(kv.Key, StringComparison.Ordinal) && kv.Key.Length > (best?.Length ?? 0))
                {
                    best = kv.Key;
                }
            }

            var tokens = ValidationTokens;
            if (best == null && tokens != null)
            {
                for (var i = 0; i < tokens.Count; i++)
                {
                    if (msg.IndexOf(tokens[i].Key, StringComparison.Ordinal) >= 0)
                    {
                        best = tokens[i].Value;
                        break;
                    }
                }
            }

            if (best == null)
            {
                var bestIdx = int.MaxValue;
                foreach (var kv in _rowByField)
                {
                    var idx = msg.IndexOf(kv.Key, StringComparison.Ordinal);
                    if (idx >= 0 && (idx < bestIdx || (idx == bestIdx && kv.Key.Length > (best?.Length ?? 0))))
                    {
                        best = kv.Key;
                        bestIdx = idx;
                    }
                }
            }

            return best != null && _rowByField.TryGetValue(best, out var row) ? row : null;
        }

        // ── 未調整の折りたたみ ──

        // 詳細モードで、既定値のままの欄をカードごとの「未調整の設定」へ移す。編集中に欄が動かないよう、
        // 振り分けは構築・モード切替・トグル・Undo / 一括リセットのときだけ行う。
        private void Reclassify()
        {
            var fold = _mode >= 2 && _hideUntouched && string.IsNullOrWhiteSpace(_query) && !_modifiedOnly;
            for (var c = 0; c < _cards.Count; c++)
            {
                var card = _cards[c];
                for (var i = 0; i < card.Rows.Count; i++)
                {
                    var row = card.Rows[i];
                    var f = fold && !row.NoFold && row.Tier != FieldTier.Required && !row.IsModified && !row.NeedsAttention && !row.HasIssues;
                    card.SetFolded(row, f);
                }
            }
        }

        // ── モード・検索・絞り込み ──

        private void ApplyFilters()
        {
            if (_modeButtons.Count == 0)
            {
                return;
            }

            for (var i = 0; i < _modeButtons.Count; i++)
            {
                _modeButtons[i].EnableInClassList("ux-seg__btn--on", i == _mode);
            }

            var q = (_query ?? string.Empty).Trim();
            var searching = q.Length > 0;
            var filtering = searching || _modifiedOnly;
            var hits = 0;
            for (var i = 0; i < _rows.Count; i++)
            {
                _rows[i].OnModeChanged(_mode);
                _rows[i].Highlight(searching ? q : null);
            }

            for (var c = 0; c < _cards.Count; c++)
            {
                var card = _cards[c];
                var visible = 0;
                var foldShown = 0;
                for (var i = 0; i < card.Rows.Count; i++)
                {
                    var row = card.Rows[i];
                    var show = searching ? row.Matches(q) : (int)row.Tier <= _mode || row.NeedsAttention;
                    if (_modifiedOnly && !row.IsModified)
                    {
                        show = false;
                    }

                    row.Show(show);
                    if (show && card.IsFolded(row))
                    {
                        if (!row.IsDuplicate)
                        {
                            foldShown++;
                        }

                        continue;
                    }

                    if (show)
                    {
                        visible++;
                        if (!row.IsDuplicate)
                        {
                            hits++;
                        }
                    }
                }

                card.SetFoldShown(foldShown);
                card.SetVisibility(foldShown > 0 ? visible + 1 : visible, filtering);
            }

            OnFiltersApplied(searching ? q : string.Empty);

            if (_hit != null)
            {
                var sb = new StringBuilder();
                if (filtering)
                {
                    sb.Append(hits).Append(" 件の設定が見つかりました");
                    if (searching && _mode < 2)
                    {
                        sb.Append("(検索中は表示モードに関わらず全部の設定から探します)");
                    }

                    if (_modifiedOnly)
                    {
                        sb.Append(" ・ 変更した設定だけを表示中");
                    }
                }

                _hit.text = sb.ToString();
                _hit.Show(filtering);
            }
        }

        // 検索・モードの絞り込みを反映したあと(種別が持つ一覧の行を query で絞り込む。空 = 検索していない)。
        protected virtual void OnFiltersApplied(string query) { }

        // ── チップ・一括リセット ──

        private void UpdateChips()
        {
            if (_title == null || Target == null)
            {
                return;
            }

            var display = string.IsNullOrEmpty(Target.DisplayName) ? Target.name : Target.DisplayName;
            SetText(_title, display);
            _title.tooltip = display;
            var id = $"{Target.name}  ·  ID {Target.Id}";
            SetText(_idLabel, id);
            _idLabel.tooltip = id;
            var cat = string.IsNullOrEmpty(Target.Category) ? "カテゴリ未設定" : Target.Category;
            SetText(_chipCategory, cat);
            _chipCategory.tooltip = "カテゴリ(管理情報)";

            string status;
            string cls;
            if (_errors > 0)
            {
                status = $"✕ エラー {_errors}";
                cls = "ux-chip--err";
            }
            else if (_warnings > 0)
            {
                status = $"⚠ 警告 {_warnings}";
                cls = "ux-chip--warn";
            }
            else
            {
                status = "✓ 検証 OK";
                cls = "ux-chip--ok";
            }

            SetText(_chipStatus, status);
            _chipStatus.EnableInClassList("ux-chip--ok", cls == "ux-chip--ok");
            _chipStatus.EnableInClassList("ux-chip--warn", cls == "ux-chip--warn");
            _chipStatus.EnableInClassList("ux-chip--err", cls == "ux-chip--err");
            _chipStatus.tooltip = "Validation の結果。該当する欄の下に表示されます";

            var count = 0;
            for (var i = 0; i < _changeRows.Count; i++)
            {
                if (_changeRows[i].IsModified)
                {
                    count++;
                }
            }

            SetText(_chipMod, count > 0 ? $"● 変更 {count} 件" : "変更なし");
            _chipMod.EnableInClassList("ux-chip--mod", count > 0);
            _chipMod.EnableInClassList("ux-chip--on", _modifiedOnly);
            _chipMod.tooltip = count > 0 ? "既定値と違う設定の数。クリックで「変更した設定だけ」を切り替え" : "既定値と違う設定はありません";
            _btnResetAll?.SetEnabled(count > 0);
            if (_barMod != null)
            {
                SetText(_barMod, count > 0 ? $"● 変更 {count} 件" : "変更なし");
                _barMod.EnableInClassList("ux-chip--mod", count > 0);
            }
        }

        private static void SetText(Label l, string text)
        {
            if (l != null && l.text != text)
            {
                l.text = text;
            }
        }

        private void ResetAll()
        {
            if (Target == null || So == null)
            {
                return;
            }

            var names = new List<string>();
            for (var i = 0; i < _changeRows.Count; i++)
            {
                if (_changeRows[i].IsModified && !_changeRows[i].NoBulkReset)
                {
                    names.Add(_changeRows[i].Label);
                }
            }

            if (names.Count == 0)
            {
                return;
            }

            var sb = new StringBuilder();
            sb.Append("既定値と違う ").Append(names.Count).Append(" 件の設定を、既定値に戻します。\n\n");
            for (var i = 0; i < names.Count && i < 8; i++)
            {
                sb.Append("・").Append(names[i]).Append('\n');
            }

            if (names.Count > 8)
            {
                sb.Append("・ほか ").Append(names.Count - 8).Append(" 件\n");
            }

            sb.Append("\n管理情報(名前・分類・メモなど)は対象外です。元に戻す(Ctrl+Z)で取り消せます。");
            if (!EditorUtility.DisplayDialog("すべて既定に戻す", sb.ToString(), "戻す", "キャンセル"))
            {
                return;
            }

            Undo.RecordObject(Target, "すべて既定に戻す");
            So.Update();
            for (var i = 0; i < _changeRows.Count; i++)
            {
                if (_changeRows[i].IsModified && !_changeRows[i].NoBulkReset)
                {
                    _changeRows[i].ApplyDefault();
                }
            }

            So.ApplyModifiedProperties();
            EditorUtility.SetDirty(Target);
            OnSideEffect();
            RunValidation();
            RefreshAll(true);
            Reclassify();
            ApplyFilters();
        }
    }
}
