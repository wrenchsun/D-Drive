using System;
using System.Collections.Generic;
using DDrive.Foundation.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ux
{
    // つまみの値の型。Other(カーブ / グラデーション)は Inspector 側で編集するので表示のみ。
    public enum TuneValueKind
    {
        Color,
        Float,
        Int,
        Vector,
        Texture,
        Other,

        // Tuning(docs/1010 UX-15)用。値は ParamValue の BoolValue / StringValue(Enum は選択肢の文字)。
        Bool,
        String,
        Enum,
    }

    // 「＋ つまみを追加」の候補 1 件。Name は反映先の名前(Vfx なら材質の TargetProperty)。
    public struct TuneCandidate
    {
        public string Name;
        public TuneValueKind Kind;
        public ParamValue Value;
    }

    // 追加の見込み。Free = 候補の列挙をせず自由入力の行を足す / Message = 足せない理由 / それ以外は Candidates から選ぶ。
    public sealed class TuneAddPlan
    {
        public bool Free;
        public string Message;
        public List<TuneCandidate> Candidates;
    }

    // つまみの供給元(docs/1010 §2 TuneKnobs)。種別ごとに実装する: Vfx の Params、Material の Specific、Canvas の ElementFx パラメータ、Tuning の値。
    // 値の読み書きの Undo / SetDirty / 再生中の即時反映は TuneKnobs が行い、供給元はデータの読み書きだけをする。
    public interface ITuneSource
    {
        UnityEngine.Object Owner { get; }

        // セッション内の分類の上書きなどで使う、Data ごとに一意な鍵(Data の GUID)。
        string OwnerKey { get; }

        int Count { get; }
        string Label(int index);

        // 反映先の名前(未設定なら「反映されません」の表示にする)。
        string TargetName(int index);

        TuneValueKind KindOf(int index);
        ParamValue Value(int index);
        void SetValue(int index, ParamValue value);

        // 並び・型が変わったかを見る鍵(変わると TuneKnobs が作り直す)。
        string Signature();

        // 分類(TuneCategories.Names の添字)の推定。
        int InferCategory(int index);

        // ⚙(定義の詳細)に出す欄: SerializedObject 上の配列のパスと、要素内のフィールド名。
        string ArrayPath { get; }
        IReadOnlyList<string> DetailFields { get; }

        // 「＋ つまみを追加」のパレット項目名と、その追加の見込み・追加の実行。
        IReadOnlyList<string> AddKinds { get; }
        TuneAddPlan PlanAdd(int kind);
        void Append(int kind, TuneCandidate candidate);
        void AppendFree();
        void RemoveAt(int index);

        // 再生中の実体へ即時反映する(Vfx は VfxManager.SetParam)。反映先が無い種別は何もしない。
        void ApplyLive(string label, ParamValue value);

        // 分類(チップ)の語彙。既定は共通の語彙(色 / サイズ / …)。供給元が独自の語彙を持つなら上書きする
        // (Material = 共通チャンネル / 描画ステート / 接頭辞ごと / その他)。InferCategory はこの並びの添字を返す。語彙は Signature が変わるまで変えない。
        IReadOnlyList<string> CategoryNames => TuneCategories.Names;

        // 名前の列を広げるか(Material のようにプロパティ名が長い供給元)。
        bool WideNames => false;

        // 範囲が決まっているつまみ(シェーダーの Range など)のスライダー範囲。決まっていなければ false(値から自動で決める)。
        bool TryRange(int index, out float min, out float max)
        {
            min = 0f;
            max = 1f;
            return false;
        }

        // ── Tuning(UX-15)向けの任意の拡張。既定のままなら Vfx / Material / Canvas の見た目は変わらない ──

        // Enum の選択肢(KindOf が Enum のとき)。
        string[] EnumOptions(int index) => null;

        // true: 範囲が無い数値をスライダーではなく数値欄にする(Tuning の Min == Max)。
        bool UseNumberField(int index) => false;

        // 分類のチップをクリックで付け替えられるか(Tuning は名前の接頭辞で決まるので false)。
        bool CategoryEditable => true;

        // 「＋ つまみを追加」と「このつまみを削除」を出すか(Tuning は仕様書が正本なので false)。
        bool CanAddRemove => true;

        // つまみの並び(index)と ArrayPath 上の要素の添字が違うときの対応(並べ替えて見せる供給元)。
        int ArrayIndex(int index) => index;

        // 検索に使う追加の語(説明・カテゴリなど)。
        string SearchText(int index) => null;

        // 部品の右に出す単位。
        string UnitOf(int index) => null;

        // 名前の下の薄い 1 行(説明の要約)。
        string Summary(int index) => null;
    }

    // つまみの分類(チップ)の共通の語彙と推定。
    public static class TuneCategories
    {
        // 0 色 / 1 サイズ / 2 速度 / 3 強さ / 4 不透明度 / 5 その他
        public static IReadOnlyList<string> Names => NameArray;

        private static readonly string[] NameArray = { "色", "サイズ", "速度", "強さ", "不透明度", "その他" };

        // 候補の絞り込み・分類の推定に使う語(添字は種類: 0 色 / 1 サイズ / 2 速度 / 3 強さ / 4 不透明度 / 5 寿命)。
        internal static readonly string[][] Words =
        {
            new string[0],
            new[] { "size", "scale", "radius", "width", "thick" },
            new[] { "speed", "rate", "velocity", "freq", "scroll" },
            new[] { "intensity", "strength", "power", "emission", "glow", "bright", "amount" },
            new[] { "alpha", "opacity", "transparen", "fade" },
            new[] { "life", "age", "duration" },
        };

        // 分類を値の型と Label / 反映先の語から推定する。
        public static int Infer(TuneValueKind kind, string label, string target)
        {
            if (kind == TuneValueKind.Color)
            {
                return 0;
            }

            var text = (label ?? string.Empty) + " " + (target ?? string.Empty);
            if (HasWord(text, new[] { "color", "colour", "tint", "色" }))
            {
                return 0;
            }

            if (HasWord(text, Words[4]) || HasWord(text, new[] { "不透明", "透明" }))
            {
                return 4;
            }

            if (HasWord(text, Words[1]) || HasWord(text, new[] { "サイズ", "大きさ" }))
            {
                return 1;
            }

            if (HasWord(text, Words[2]) || HasWord(text, new[] { "速" }))
            {
                return 2;
            }

            if (HasWord(text, Words[3]) || HasWord(text, new[] { "強" }))
            {
                return 3;
            }

            return 5;
        }

        internal static bool HasWord(string text, string[] words)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            for (var i = 0; i < words.Length; i++)
            {
                if (text.IndexOf(words[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    // 調整つまみの欄: 1 つまみ = 1 行(分類チップ / 名前 / 部品 / ↺ / ⚙)。
    public sealed class TuneKnobsRow : UxRow
    {
        private readonly TuneKnobs _panel;

        public TuneKnobsRow(SerializedObject so, SerializedObject defaultSo, IReadOnlyList<FieldGuideEntry> entries, string hint, TuneKnobs panel, Action changed)
            : base(so, defaultSo, entries, "調整つまみ", hint, FieldTier.Common, true, panel, changed)
        {
            _panel = panel;
            NoFold = true;
            AddToClassList("ux-tune");
        }

        public override void Sync() => _panel.Sync();
    }

    // 調整つまみのパネル(試験版 E の PeTunePanel を種別非依存にしたもの)。値は供給元(ITuneSource)へ書く
    // (Undo.RecordObject + SetDirty、1.2 秒以内の連続操作は 1 回の Undo にまとめる)。再生中は ApplyLive で即時反映。
    // 左端の色バーと ↺ は「このウィンドウで開いたときの値」との差。
    public sealed class TuneKnobs : VisualElement
    {
        private sealed class Knob
        {
            public int Index;
            public VisualElement Row;
            public Label Chip;
            public Label Name;
            public Label Unit;
            public Label Summary;
            public Button Reset;
            public Action<ParamValue> SetUi;
            public VisualElement Details;
            public bool DetailsBuilt;
            public bool Visible = true;
        }

        private readonly ITuneSource _src;
        private readonly SerializedObject _so;
        private readonly Action _edited;      // ドラッグ中も呼ぶ軽い通知
        private readonly Action _structural;  // 追加・削除など(検証を含む)
        public const int VirtualizeAbove = 20; // 表示するつまみがこれを超えたら ListView にする
        private readonly List<Knob> _knobs = new List<Knob>();
        private readonly List<ParamValue> _baseline = new List<ParamValue>();
        private readonly HashSet<int> _openDetails = new HashSet<int>();
        private readonly VisualElement _filterBar;
        private readonly VisualElement _list;
        private readonly Label _empty;
        private readonly List<Knob> _shown = new List<Knob>();
        private ListView _listView;
        private int _layoutSig = int.MinValue;
        private string _query = string.Empty;
        private readonly VisualElement _addPanel;
        private readonly Label _message;
        private int _filter = -1;
        private string _filterKey = string.Empty;
        private string _signature = string.Empty;
        private int _gIndex = -1;
        private int _gGroup;
        private double _gTime;

        public TuneKnobs(ITuneSource source, SerializedObject so, Action edited, Action structural)
        {
            _src = source;
            _so = so;
            _edited = edited;
            _structural = structural;
            AddToClassList("ux-panel");

            _filterBar = new VisualElement();
            _filterBar.AddToClassList("ux-filters");
            Add(_filterBar);

            _empty = new Label("つまみはまだありません。「＋ つまみを追加」から足すと、ここで回せます。");
            _empty.AddToClassList("ux-dim");
            _empty.AddToClassList("ux-knobs-empty");
            Add(_empty);

            _list = new VisualElement();
            _list.AddToClassList("ux-knobs");
            Add(_list);

            var addRow = new VisualElement();
            addRow.AddToClassList("ux-addrow");
            addRow.Show(_src.CanAddRemove);
            var add = new Button { text = "＋ つまみを追加" };
            add.AddToClassList("ux-btn");
            add.AddToClassList("ux-btn--primary");
            addRow.Add(add);
            Add(addRow);

            _addPanel = new VisualElement();
            _addPanel.AddToClassList("ux-palette");
            _addPanel.Show(false);
            var kinds = _src.AddKinds;
            for (var i = 0; i < kinds.Count; i++)
            {
                var kind = i;
                var b = new Button(() => AddKnob(kind, add)) { text = kinds[i] };
                b.AddToClassList("ux-btn");
                b.AddToClassList("ux-palette__btn");
                _addPanel.Add(b);
            }

            Add(_addPanel);

            _message = new Label();
            _message.AddToClassList("ux-message");
            _message.Show(false);
            Add(_message);

            add.clicked += () =>
            {
                _addPanel.Show(!_addPanel.IsShown());
                _message.text = string.Empty;
                _message.Show(false);
            };

            for (var i = 0; i < _src.Count; i++)
            {
                _baseline.Add(_src.Value(i));
            }

            Rebuild();
        }

        // ── 構築 ──

        private void Rebuild()
        {
            _signature = _src.Signature();
            _list.Clear();
            _knobs.Clear();
            _layoutSig = int.MinValue;
            _filterKey = string.Empty;
            EnableInClassList("ux-knobs--wide", _src.WideNames);
            var n = _src.Count;
            while (_baseline.Count > n)
            {
                _baseline.RemoveAt(_baseline.Count - 1);
            }

            while (_baseline.Count < n)
            {
                _baseline.Add(_src.Value(_baseline.Count));
            }

            _empty.Show(n == 0);
            for (var i = 0; i < n; i++)
            {
                _knobs.Add(BuildKnob(i));
            }

            RefreshChrome();
        }

        private Knob BuildKnob(int i)
        {
            var k = new Knob { Index = i };
            var row = new VisualElement();
            row.AddToClassList("ux-knob");
            k.Row = row;

            var bar = new VisualElement { tooltip = "最初に開いたときの値と違います" };
            bar.AddToClassList("ux-knob__bar");
            row.Add(bar);

            var main = new VisualElement();
            main.AddToClassList("ux-knob__main");
            row.Add(main);

            var line = new VisualElement();
            line.AddToClassList("ux-knob__line");
            main.Add(line);

            k.Chip = new Label { tooltip = "分類(クリックで変える)" };
            k.Chip.AddToClassList("ux-chip");
            k.Chip.AddToClassList("ux-cat");
            k.Chip.RegisterCallback<ClickEvent>(e =>
            {
                if (_src.CategoryEditable)
                {
                    OpenCategoryMenu(k);
                }
            });
            line.Add(k.Chip);

            k.Name = new Label();
            k.Name.AddToClassList("ux-knob__name");
            line.Add(k.Name);

            var control = BuildControl(k);
            control.AddToClassList("ux-knob__control");
            line.Add(control);

            k.Unit = new Label();
            k.Unit.AddToClassList("ux-dim");
            k.Unit.AddToClassList("ux-knob__unit");
            k.Unit.Show(false);
            line.Add(k.Unit);

            k.Reset = new Button(() => ResetKnob(k)) { text = "↺", tooltip = "最初に開いたときの値に戻す" };
            k.Reset.AddToClassList("ux-btn");
            k.Reset.AddToClassList("ux-btn--icon");
            k.Reset.AddToClassList("ux-knob__reset");
            line.Add(k.Reset);

            var gear = new Button { text = "⚙", tooltip = "定義の詳細(名前 / 型 / 反映先 など)" };
            gear.AddToClassList("ux-btn");
            gear.AddToClassList("ux-btn--icon");
            line.Add(gear);

            k.Summary = new Label();
            k.Summary.AddToClassList("ux-dim");
            k.Summary.AddToClassList("ux-knob__summary");
            k.Summary.Show(false);
            main.Add(k.Summary);

            k.Details = new VisualElement();
            main.Add(k.Details);
            if (_openDetails.Contains(i))
            {
                BuildDetails(k);
            }

            k.Details.Show(_openDetails.Contains(i));
            gear.clicked += () =>
            {
                var open = !k.Details.IsShown();
                if (open)
                {
                    BuildDetails(k);
                }

                k.Details.Show(open);
                _listView?.schedule.Execute(() => _listView.RefreshItems()).StartingIn(30);
                if (open)
                {
                    _openDetails.Add(k.Index);
                }
                else
                {
                    _openDetails.Remove(k.Index);
                }
            };

            UpdateKnobState(k);
            return k;
        }

        private VisualElement BuildControl(Knob k)
        {
            var i = k.Index;
            var cur = _src.Value(i);
            switch (_src.KindOf(i))
            {
                case TuneValueKind.Color:
                {
                    var c = cur.ColorValue;
                    var f = new ColorField { value = c, showAlpha = true, hdr = c.maxColorComponent > 1f };
                    f.RegisterValueChangedCallback(e => Write(i, ParamValue.Of(e.newValue)));
                    k.SetUi = v => f.SetValueWithoutNotify(v.ColorValue);
                    return f;
                }

                case TuneValueKind.Bool:
                {
                    var t = new Toggle { value = cur.BoolValue };
                    t.RegisterValueChangedCallback(e => Write(i, ParamValue.Of(e.newValue)));
                    k.SetUi = v => t.SetValueWithoutNotify(v.BoolValue);
                    return t;
                }

                case TuneValueKind.String:
                {
                    var f = new TextField { value = cur.StringValue ?? string.Empty };
                    f.RegisterValueChangedCallback(e => Write(i, ParamValue.Of(e.newValue ?? string.Empty)));
                    k.SetUi = v =>
                    {
                        var s = v.StringValue ?? string.Empty;
                        if (f.value != s)
                        {
                            f.SetValueWithoutNotify(s);
                        }
                    };
                    return f;
                }

                case TuneValueKind.Enum:
                {
                    var opts = new List<string>(_src.EnumOptions(i) ?? new string[0]);
                    var f = new DropdownField(opts, -1);
                    f.SetValueWithoutNotify(cur.StringValue ?? string.Empty);
                    f.RegisterValueChangedCallback(e => Write(i, ParamValue.Of(e.newValue ?? string.Empty)));
                    k.SetUi = v =>
                    {
                        var s = v.StringValue ?? string.Empty;
                        if (f.value != s)
                        {
                            f.SetValueWithoutNotify(s);
                        }
                    };
                    return f;
                }

                case TuneValueKind.Float when _src.UseNumberField(i):
                {
                    var f = new FloatField { value = cur.FloatValue };
                    f.RegisterValueChangedCallback(e => Write(i, ParamValue.Of(e.newValue)));
                    k.SetUi = v =>
                    {
                        if (!Mathf.Approximately(f.value, v.FloatValue))
                        {
                            f.SetValueWithoutNotify(v.FloatValue);
                        }
                    };
                    return f;
                }

                case TuneValueKind.Int when _src.UseNumberField(i):
                {
                    var f = new IntegerField { value = cur.IntValue };
                    f.RegisterValueChangedCallback(e => Write(i, ParamValue.Of(e.newValue)));
                    k.SetUi = v =>
                    {
                        if (f.value != v.IntValue)
                        {
                            f.SetValueWithoutNotify(v.IntValue);
                        }
                    };
                    return f;
                }

                case TuneValueKind.Float:
                {
                    var b = _baseline[i].FloatValue;
                    var hi = b > 1f ? b * 4f : 1f;
                    var lo = b < 0f ? b * 4f : 0f;
                    if (_src.TryRange(i, out var rangeLo, out var rangeHi) && rangeHi > rangeLo)
                    {
                        lo = Mathf.Min(rangeLo, b);
                        hi = Mathf.Max(rangeHi, b);
                    }

                    var s = new Slider(lo, hi) { value = cur.FloatValue, showInputField = true };
                    s.RegisterValueChangedCallback(e => Write(i, ParamValue.Of(e.newValue)));
                    k.SetUi = v =>
                    {
                        if (v.FloatValue > s.highValue)
                        {
                            s.highValue = v.FloatValue;
                        }

                        if (v.FloatValue < s.lowValue)
                        {
                            s.lowValue = v.FloatValue;
                        }

                        s.SetValueWithoutNotify(v.FloatValue);
                    };
                    return s;
                }

                case TuneValueKind.Int:
                {
                    var b = _baseline[i].IntValue;
                    var hi = Mathf.Max(10, b * 4);
                    var lo = Mathf.Min(0, b * 4);
                    if (_src.TryRange(i, out var intLo, out var intHi) && intHi > intLo)
                    {
                        lo = Mathf.Min(Mathf.RoundToInt(intLo), b);
                        hi = Mathf.Max(Mathf.RoundToInt(intHi), b);
                    }

                    var s = new SliderInt(lo, hi) { value = cur.IntValue, showInputField = true };
                    s.RegisterValueChangedCallback(e => Write(i, ParamValue.Of(e.newValue)));
                    k.SetUi = v =>
                    {
                        s.highValue = Mathf.Max(s.highValue, v.IntValue);
                        s.lowValue = Mathf.Min(s.lowValue, v.IntValue);
                        s.SetValueWithoutNotify(v.IntValue);
                    };
                    return s;
                }

                case TuneValueKind.Vector:
                {
                    var f = new Vector3Field { value = (Vector3)cur.VectorValue };
                    f.RegisterValueChangedCallback(e =>
                    {
                        var w = i < _src.Count ? _src.Value(i).VectorValue.w : 0f;
                        Write(i, new ParamValue { Type = ParamValueType.Vector, VectorValue = new Vector4(e.newValue.x, e.newValue.y, e.newValue.z, w) });
                    });
                    k.SetUi = v => f.SetValueWithoutNotify((Vector3)v.VectorValue);
                    return f;
                }

                case TuneValueKind.Texture:
                {
                    var f = new ObjectField { objectType = typeof(Texture), value = cur.ObjectValue };
                    f.RegisterValueChangedCallback(e => Write(i, new ParamValue { Type = ParamValueType.Object, ObjectValue = e.newValue }));
                    k.SetUi = v => f.SetValueWithoutNotify(v.ObjectValue);
                    return f;
                }

                default:
                {
                    var l = new Label("カーブ / グラデーション(Inspector で編集)");
                    l.AddToClassList("ux-dim");
                    return l;
                }
            }
        }

        // ⚙ の中身は最初に開いたときに作る(つまみが多いデータで PropertyField を全部作らない)。
        private void BuildDetails(Knob k)
        {
            if (k.DetailsBuilt)
            {
                return;
            }

            k.DetailsBuilt = true;
            var box = k.Details;
            box.AddToClassList("ux-details");
            var arr = _so.FindProperty(_src.ArrayPath);
            var elementIndex = _src.ArrayIndex(k.Index);
            if (arr == null || elementIndex < 0 || elementIndex >= arr.arraySize)
            {
                return;
            }

            var el = arr.GetArrayElementAtIndex(elementIndex);
            var fields = _src.DetailFields;
            for (var f = 0; f < fields.Count; f++)
            {
                var prop = el.FindPropertyRelative(fields[f]);
                if (prop == null)
                {
                    continue;
                }

                var pf = new PropertyField(prop);
                pf.BindProperty(prop);
                box.Add(pf);
            }

            if (_src.CanAddRemove)
            {
                var remove = new Button(() => RemoveKnob(k.Index)) { text = "このつまみを削除" };
                remove.AddToClassList("ux-btn");
                remove.AddToClassList("ux-btn--small");
                box.Add(remove);
            }
        }

        // ── 値の書き込み(Undo + SetDirty。再生中は即時反映) ──

        private void Write(int i, ParamValue v)
        {
            if (i < 0 || i >= _src.Count)
            {
                return;
            }

            var owner = _src.Owner;
            var now = EditorApplication.timeSinceStartup;
            if (i == _gIndex && now - _gTime < 1.2)
            {
                Undo.RecordObject(owner, "つまみを調整");
                Undo.CollapseUndoOperations(_gGroup);
            }
            else
            {
                Undo.IncrementCurrentGroup();
                _gGroup = Undo.GetCurrentGroup();
                Undo.RecordObject(owner, "つまみを調整");
                _gIndex = i;
            }

            _gTime = now;
            _src.SetValue(i, v);
            EditorUtility.SetDirty(owner);
            _so.Update();
            _src.ApplyLive(_src.Label(i), v);

            if (i < _knobs.Count)
            {
                UpdateKnobState(_knobs[i]);
            }

            _edited?.Invoke();
        }

        private void ResetKnob(Knob k)
        {
            if (k.Index >= _baseline.Count)
            {
                return;
            }

            _gIndex = -1;
            var v = _baseline[k.Index];
            Write(k.Index, v);
            k.SetUi?.Invoke(v);
            UpdateKnobState(k);
        }

        private static bool SameValue(ParamValue a, ParamValue b)
        {
            return Mathf.Approximately(a.FloatValue, b.FloatValue) && a.IntValue == b.IntValue && a.ColorValue == b.ColorValue
                && a.VectorValue == b.VectorValue && a.ObjectValue == b.ObjectValue
                && a.BoolValue == b.BoolValue && (a.StringValue ?? string.Empty) == (b.StringValue ?? string.Empty);
        }

        // 分類の語彙(供給元が決める)。
        private IReadOnlyList<string> Names => _src.CategoryNames ?? TuneCategories.Names;

        // 分類の添字。デザイナーがチップで付け替えた分類(セッション内、名前で覚える)を優先し、無ければ供給元の推定。
        private int CategoryOf(int i)
        {
            var names = Names;
            var saved = _src.CategoryEditable ? SessionState.GetString(CatKey(_src.Label(i)), string.Empty) : string.Empty;
            if (saved.Length > 0)
            {
                for (var c = 0; c < names.Count; c++)
                {
                    if (names[c] == saved)
                    {
                        return c;
                    }
                }
            }

            return Mathf.Clamp(_src.InferCategory(i), 0, Mathf.Max(0, names.Count - 1));
        }

        private string CatKey(string label) => "DDrive.Ux.cat." + _src.OwnerKey + "." + label;

        private const int ChipColors = 6; // ux-cat--0..5 の色。語彙がこれより多いときは繰り返す

        private void UpdateKnobState(Knob k)
        {
            if (k.Index >= _src.Count)
            {
                return;
            }

            var names = Names;
            var cat = CategoryOf(k.Index);
            var chip = names.Count > 0 ? names[cat] : string.Empty;
            if (k.Chip.text != chip)
            {
                k.Chip.text = chip;
                k.Chip.tooltip = _src.CategoryEditable ? chip + "(クリックで分類を変える)" : "カテゴリ: " + chip;
            }

            for (var c = 0; c < ChipColors; c++)
            {
                k.Chip.EnableInClassList("ux-cat--" + c, c == cat % ChipColors);
            }

            var rawLabel = _src.Label(k.Index);
            var label = string.IsNullOrEmpty(rawLabel) ? "(名前未設定)" : rawLabel;
            if (k.Name.text != label)
            {
                k.Name.text = label;
            }

            var target = _src.TargetName(k.Index);
            k.Name.tooltip = string.IsNullOrEmpty(target) ? "反映先が未設定のため反映されません" : "→ " + target;
            var unit = _src.UnitOf(k.Index);
            if (k.Unit.text != (unit ?? string.Empty))
            {
                k.Unit.text = unit ?? string.Empty;
            }

            k.Unit.Show(!string.IsNullOrEmpty(unit));
            var summary = _src.Summary(k.Index);
            if (k.Summary.text != (summary ?? string.Empty))
            {
                k.Summary.text = summary ?? string.Empty;
                k.Summary.tooltip = summary ?? string.Empty;
            }

            k.Summary.Show(!string.IsNullOrEmpty(summary));
            var modified = k.Index < _baseline.Count && !SameValue(_src.Value(k.Index), _baseline[k.Index]);
            k.Row.EnableInClassList("ux-knob--mod", modified);
            k.Row.EnableInClassList("ux-knob--nolink", string.IsNullOrEmpty(target));
        }

        // ── 分類 ──

        private void OpenCategoryMenu(Knob k)
        {
            var menu = new GenericMenu();
            var cur = CategoryOf(k.Index);
            var names = Names;
            for (var c = 0; c < names.Count; c++)
            {
                var name = names[c];
                menu.AddItem(new GUIContent(name), c == cur, () =>
                {
                    SessionState.SetString(CatKey(_src.Label(k.Index)), name);
                    RefreshChrome();
                });
            }

            menu.ShowAsContext();
        }

        // ウィンドウの検索語で絞る(名前・反映先に一致するつまみだけ出す)。空 = 絞らない。
        public void SetQuery(string query)
        {
            query = (query ?? string.Empty).Trim();
            if (query == _query)
            {
                return;
            }

            _query = query;
            RefreshChrome();
        }

        // つまみの総数・いま絞り込みに当たって表示している数・「最初に開いたときの値」から変わった数(Tuning の件数表示 / 変更 N 件に使う)。
        public int KnobCount => _knobs.Count;

        public int ShownCount => _shown.Count;

        public int ModifiedCount
        {
            get
            {
                var n = 0;
                for (var i = 0; i < _knobs.Count && i < _src.Count && i < _baseline.Count; i++)
                {
                    if (!SameValue(_src.Value(i), _baseline[i]))
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        // 検索語に一致するつまみがあるか(行を残すかの判定に使う)。
        public bool AnyMatch(string query)
        {
            for (var i = 0; i < _knobs.Count; i++)
            {
                if (Matches(_knobs[i].Index, query))
                {
                    return true;
                }
            }

            return false;
        }

        private bool Matches(int index, string query)
        {
            if (string.IsNullOrEmpty(query) || index >= _src.Count)
            {
                return true;
            }

            var label = _src.Label(index);
            var target = _src.TargetName(index);
            var extra = _src.SearchText(index);
            return (label != null && label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                || (target != null && target.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                || (extra != null && extra.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void RefreshChrome()
        {
            var names = Names;
            var counts = new int[names.Count];
            for (var i = 0; i < _knobs.Count; i++)
            {
                UpdateKnobState(_knobs[i]);
                counts[CategoryOf(i)]++;
            }

            if (_filter >= 0 && (_filter >= counts.Length || counts[_filter] == 0))
            {
                _filter = -1;
            }

            // 絞り込み(分類チップ + 検索語)に当たるつまみ。
            _shown.Clear();
            var layout = 17;
            for (var i = 0; i < _knobs.Count; i++)
            {
                var k = _knobs[i];
                k.Visible = (_filter < 0 || CategoryOf(i) == _filter) && Matches(i, _query);
                if (k.Visible)
                {
                    _shown.Add(k);
                    layout = layout * 31 + i;
                }
            }

            LayoutList(layout);

            var key = _filter + ":" + _knobs.Count + ":" + string.Join(",", counts) + ":" + string.Join("|", names);
            if (key == _filterKey)
            {
                return;
            }

            _filterKey = key;
            _filterBar.Clear();
            if (_knobs.Count == 0)
            {
                _filterBar.Show(false);
                return;
            }

            _filterBar.Show(true);
            AddFilterChip("すべて " + _knobs.Count, -1);
            for (var c = 0; c < counts.Length; c++)
            {
                if (counts[c] > 0)
                {
                    AddFilterChip(names[c] + " " + counts[c], c);
                }
            }
        }

        // 表示するつまみが VirtualizeAbove を超えたら ListView(内側スクロール)、以下なら行をそのまま並べる。
        private void LayoutList(int shownSig)
        {
            var useList = _shown.Count > VirtualizeAbove;
            shownSig = shownSig * 31 + (useList ? 1 : 0);
            if (shownSig == _layoutSig)
            {
                return;
            }

            _layoutSig = shownSig;
            _list.Clear();
            if (useList)
            {
                _listView = new ListView
                {
                    virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                    selectionType = SelectionType.None,
                    showAlternatingRowBackgrounds = AlternatingRowBackground.None,
                    itemsSource = _shown,
                    makeItem = () => new VisualElement(),
                    bindItem = (e, i) =>
                    {
                        e.Clear();
                        if (i >= 0 && i < _shown.Count)
                        {
                            e.Add(_shown[i].Row);
                            _shown[i].Row.Show(true);
                        }
                    },
                };
                _listView.AddToClassList("ux-itemlist__lv");
                _list.Add(_listView);
                return;
            }

            _listView = null;
            for (var i = 0; i < _knobs.Count; i++)
            {
                _list.Add(_knobs[i].Row);
                _knobs[i].Row.Show(_knobs[i].Visible);
            }
        }

        private void AddFilterChip(string text, int cat)
        {
            var names = Names;
            var b = new Button(() =>
            {
                _filter = cat;
                RefreshChrome();
            }) { text = text, tooltip = cat < 0 ? "すべてのつまみを表示" : names[cat] + "のつまみだけ表示" };
            b.AddToClassList("ux-filter");
            b.EnableInClassList("ux-filter--on", cat == _filter);
            _filterBar.Add(b);
        }

        // ── 追加・削除 ──

        private void AddKnob(int kind, VisualElement anchor)
        {
            _message.Show(false);
            var plan = _src.PlanAdd(kind);
            if (plan == null)
            {
                return;
            }

            if (plan.Free)
            {
                Mutate("つまみを追加", () => _src.AppendFree(), true);
                return;
            }

            if (!string.IsNullOrEmpty(plan.Message))
            {
                Say(plan.Message);
                return;
            }

            var cands = plan.Candidates;
            if (cands == null || cands.Count == 0)
            {
                return;
            }

            if (cands.Count == 1)
            {
                var only = cands[0];
                Mutate("つまみを追加", () => _src.Append(kind, only), false);
                return;
            }

            var menu = new GenericMenu();
            foreach (var c in cands)
            {
                var cand = c;
                menu.AddItem(new GUIContent(cand.Name), false, () => Mutate("つまみを追加", () => _src.Append(kind, cand), false));
            }

            menu.DropDown(anchor.worldBound);
        }

        private void Say(string text)
        {
            _message.text = text;
            _message.Show(true);
        }

        // 追加の共通処理: Undo + SetDirty → 再構築 → 構造変更の通知。
        private void Mutate(string undoName, Action change, bool openNewDetails)
        {
            var owner = _src.Owner;
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(owner, undoName);
            var before = _src.Count;
            change();
            EditorUtility.SetDirty(owner);
            _so.Update();
            _gIndex = -1;
            if (_src.Count > before)
            {
                _baseline.Add(_src.Value(_src.Count - 1));
                if (openNewDetails)
                {
                    _openDetails.Add(_src.Count - 1);
                }
            }

            _addPanel.Show(false);
            Rebuild();
            _structural?.Invoke();
        }

        private void RemoveKnob(int index)
        {
            if (index < 0 || index >= _src.Count)
            {
                return;
            }

            var owner = _src.Owner;
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(owner, "つまみを削除");
            _src.RemoveAt(index);
            EditorUtility.SetDirty(owner);
            _so.Update();
            _gIndex = -1;
            if (index < _baseline.Count)
            {
                _baseline.RemoveAt(index);
            }

            _openDetails.Clear();
            Rebuild();
            _structural?.Invoke();
        }

        // ── 外部変更の取り込み(150ms ごと / Undo) ──

        public void Sync()
        {
            if (_src.Signature() != _signature)
            {
                Rebuild();
                return;
            }

            for (var i = 0; i < _knobs.Count && i < _src.Count; i++)
            {
                _knobs[i].SetUi?.Invoke(_src.Value(i));
            }

            RefreshChrome();
        }
    }
}
