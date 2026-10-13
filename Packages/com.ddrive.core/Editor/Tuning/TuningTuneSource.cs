using System;
using System.Collections.Generic;
using System.Text;
using DDrive.Editor.Ux;
using DDrive.Foundation.Data;
using DDrive.Runtime.Tuning;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Tuning
{
    // TuningTable.Entries を調整つまみ(TuneKnobs)に供給する(docs/1010 UX-15)。
    // 1 キー = 1 つまみ。分類のチップ = キーの "<機能>/" 接頭辞(TuningCategoryGrouper。名前順・(未分類)は最後)、
    // 並びはカテゴリ順 → キー順(元の Entries の並びは変えない。つまみの番号 → Entries の添字は ArrayIndex で引く)。
    // 値は Type で決まる: Float / Int = Min ≠ Max ならスライダー、そうでなければ数値欄 / Bool = トグル / String = 文字欄 / Enum = 選択肢(EnumOptions)。
    // 追加・削除は無い(キーは仕様書が正本。docs/32 §5.3)。⚙ では説明・単位・範囲・Enum の選択肢を編集できる。
    // 再生中の値の変更は TuningTable.RebuildIndex() で読み出し側へ即時に反映し、起動時に値を読んで保持する機能への通知(Tuning.Rebind)は押されるまで行わない(M-2c)。
    internal sealed class TuningTuneSource : ITuneSource
    {
        private static readonly string[] Fields = { "Description", "Unit", "Min", "Max", "EnumOptions" };
        private static readonly string[] NoKinds = new string[0];

        private readonly TuningTable _table;
        private int[] _order = Array.Empty<int>();
        private int[] _category = Array.Empty<int>();
        private List<string> _names = new List<string>();
        private string _sig;
        private int _entryCount = -1;

        // 再生中に値を書き換えたとき(読み出し側へ反映したあと)に呼ぶ。ウィンドウが「再読込が必要な変更」を数える。
        public Action<string> LiveEdited;
        private int _lastSet = -1;

        public TuningTuneSource(TuningTable table)
        {
            _table = table;
            OwnerKey = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(table));
            Recompute();
        }

        public UnityEngine.Object Owner => _table;
        public string OwnerKey { get; }

        public int Count
        {
            get
            {
                EnsureFresh();
                return _order.Length;
            }
        }

        private TuningEntry Entry(int index)
        {
            EnsureFresh();
            return _table.Entries[_order[index]];
        }

        // Entries の数が変わっていたら並びを作り直す(外部の変更 = 仕様書同期・Undo)。
        private void EnsureFresh()
        {
            var n = _table.Entries != null ? _table.Entries.Length : 0;
            if (n != _entryCount)
            {
                Recompute();
            }
        }

        public string Label(int index) => TuningCategoryGrouper.DisplayName(Entry(index).Key);

        // 反映先 = キーそのもの(名前のツールチップに「→ Player/MoveSpeedMax」と出る)。空なら「反映されません」の表示。
        public string TargetName(int index) => Entry(index).Key ?? string.Empty;

        public TuneValueKind KindOf(int index)
        {
            switch (Entry(index).Type)
            {
                case TuningValueType.Float: return TuneValueKind.Float;
                case TuningValueType.Int: return TuneValueKind.Int;
                case TuningValueType.Bool: return TuneValueKind.Bool;
                case TuningValueType.Enum: return TuneValueKind.Enum;
                default: return TuneValueKind.String;
            }
        }

        public ParamValue Value(int index)
        {
            var e = Entry(index);
            switch (e.Type)
            {
                case TuningValueType.Float: return ParamValue.Of(e.ValueFloat);
                case TuningValueType.Int: return ParamValue.Of(e.ValueInt);
                case TuningValueType.Bool: return ParamValue.Of(e.ValueBool);
                default: return ParamValue.Of(e.ValueString ?? string.Empty);
            }
        }

        public void SetValue(int index, ParamValue value)
        {
            EnsureFresh();
            var i = _order[index];
            _lastSet = index;
            switch (_table.Entries[i].Type)
            {
                case TuningValueType.Float:
                    _table.Entries[i].ValueFloat = value.FloatValue;
                    break;
                case TuningValueType.Int:
                    _table.Entries[i].ValueInt = value.IntValue;
                    break;
                case TuningValueType.Bool:
                    _table.Entries[i].ValueBool = value.BoolValue;
                    break;
                default:
                    _table.Entries[i].ValueString = value.StringValue ?? string.Empty;
                    break;
            }
        }

        public string ArrayPath => "Entries";
        public IReadOnlyList<string> DetailFields => Fields;
        public IReadOnlyList<string> AddKinds => NoKinds;
        public bool WideNames => true;
        public bool CategoryEditable => false;
        public bool CanAddRemove => false;

        public int ArrayIndex(int index)
        {
            EnsureFresh();
            return index >= 0 && index < _order.Length ? _order[index] : -1;
        }

        public TuneAddPlan PlanAdd(int kind) => null;

        public void Append(int kind, TuneCandidate candidate) { }

        public void AppendFree() { }

        public void RemoveAt(int index) { }

        // 再生中: 読み出し側の索引を作り直す(Tuning.Get* が次の呼び出しから新しい値を返す)。通知(Tuning.Rebind)は別。
        public void ApplyLive(string label, ParamValue value)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            _table.RebuildIndex();
            var key = _lastSet >= 0 && _lastSet < _order.Length ? _table.Entries[_order[_lastSet]].Key : label;
            LiveEdited?.Invoke(key);
        }

        public bool TryRange(int index, out float min, out float max)
        {
            var e = Entry(index);
            min = e.Min;
            max = e.Max;
            return (e.Type == TuningValueType.Float || e.Type == TuningValueType.Int) && e.Min != e.Max;
        }

        public bool UseNumberField(int index)
        {
            var e = Entry(index);
            return (e.Type == TuningValueType.Float || e.Type == TuningValueType.Int) && e.Min == e.Max;
        }

        public string[] EnumOptions(int index) => Entry(index).EnumOptions;

        public string UnitOf(int index) => Entry(index).Unit;

        // 説明は 1 行に畳む(改行は空白に)。全文は ⚙ の説明欄と、行のツールチップ。
        public string Summary(int index)
        {
            var d = Entry(index).Description;
            return string.IsNullOrEmpty(d) ? null : d.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        }

        public string SearchText(int index)
        {
            var e = Entry(index);
            return (e.Description ?? string.Empty) + " " + CategoryNames[InferCategory(index)] + " " + (e.Unit ?? string.Empty);
        }

        // ── 分類 ──

        public IReadOnlyList<string> CategoryNames
        {
            get
            {
                EnsureFresh();
                return _names;
            }
        }

        public int InferCategory(int index)
        {
            EnsureFresh();
            return index >= 0 && index < _category.Length ? _category[index] : 0;
        }

        // 並び・型・範囲・選択肢が変わったか(変わると TuneKnobs が作り直す)。値そのものは含めない。
        public string Signature()
        {
            EnsureFresh();
            var sb = new StringBuilder();
            var entries = _table.Entries;
            for (var i = 0; entries != null && i < entries.Length; i++)
            {
                sb.Append(entries[i].Key).Append(':').Append((int)entries[i].Type).Append(':').Append(entries[i].Min).Append(':').Append(entries[i].Max);
                if (entries[i].EnumOptions != null)
                {
                    sb.Append(':').Append(string.Join("|", entries[i].EnumOptions));
                }

                sb.Append(';');
            }

            var sig = sb.ToString();
            if (sig != _sig)
            {
                _sig = sig;
                Recompute();
            }

            return sig;
        }

        private void Recompute()
        {
            var entries = _table.Entries ?? Array.Empty<TuningEntry>();
            _entryCount = entries.Length;
            var groups = TuningCategoryGrouper.Group(entries);
            var order = new List<int>(entries.Length);
            var category = new List<int>(entries.Length);
            var names = new List<string>(groups.Count);
            for (var g = 0; g < groups.Count; g++)
            {
                names.Add(groups[g].Name);
                for (var j = 0; j < groups[g].EntryIndices.Count; j++)
                {
                    order.Add(groups[g].EntryIndices[j]);
                    category.Add(g);
                }
            }

            if (names.Count == 0)
            {
                names.Add(TuningCategoryGrouper.Uncategorized);
            }

            _order = order.ToArray();
            _category = category.ToArray();
            _names = names;
        }
    }
}
