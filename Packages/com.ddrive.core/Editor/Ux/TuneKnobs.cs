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
    internal enum TuneValueKind
    {
        Color,
        Float,
        Int,
        Vector,
        Texture,
        Other,
    }

    // 「＋ つまみを追加」の候補 1 件。Name は反映先の名前(Vfx なら材質の TargetProperty)。
    internal struct TuneCandidate
    {
        public string Name;
        public TuneValueKind Kind;
        public ParamValue Value;
    }

    // 追加の見込み。Free = 候補の列挙をせず自由入力の行を足す / Message = 足せない理由 / それ以外は Candidates から選ぶ。
    internal sealed class TuneAddPlan
    {
        public bool Free;
        public string Message;
        public List<TuneCandidate> Candidates;
    }

    // つまみの供給元(docs/1010 §2 TuneKnobs)。種別ごとに実装する: Vfx の Params、Material の Specific、Canvas の ElementFx パラメータ、Tuning の値。
    // 値の読み書きの Undo / SetDirty / 再生中の即時反映は TuneKnobs が行い、供給元はデータの読み書きだけをする。
    internal interface ITuneSource
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
    }

    // つまみの分類(チップ)の共通の語彙と推定。
    internal static class TuneCategories
    {
        // 0 色 / 1 サイズ / 2 速度 / 3 強さ / 4 不透明度 / 5 その他
        public static readonly string[] Names = { "色", "サイズ", "速度", "強さ", "不透明度", "その他" };

        // 候補の絞り込み・分類の推定に使う語(添字は種類: 0 色 / 1 サイズ / 2 速度 / 3 強さ / 4 不透明度 / 5 寿命)。
        public static readonly string[][] Words =
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

        public static bool HasWord(string text, string[] words)
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
    internal sealed class TuneKnobsRow : UxRow
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
    internal sealed class TuneKnobs : VisualElement
    {
        private sealed class Knob
        {
            public int Index;
            public VisualElement Row;
            public Label Chip;
            public Label Name;
            public Button Reset;
            public Action<ParamValue> SetUi;
            public VisualElement Details;
        }

        private readonly ITuneSource _src;
        private readonly SerializedObject _so;
        private readonly Action _edited;      // ドラッグ中も呼ぶ軽い通知
        private readonly Action _structural;  // 追加・削除など(検証を含む)
        private readonly List<Knob> _knobs = new List<Knob>();
        private readonly List<ParamValue> _baseline = new List<ParamValue>();
        private readonly HashSet<int> _openDetails = new HashSet<int>();
        private readonly VisualElement _filterBar;
        private readonly VisualElement _list;
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

            _list = new VisualElement();
            _list.AddToClassList("ux-knobs");
            Add(_list);

            var addRow = new VisualElement();
            addRow.AddToClassList("ux-addrow");
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
            _filterKey = string.Empty;
            var n = _src.Count;
            while (_baseline.Count > n)
            {
                _baseline.RemoveAt(_baseline.Count - 1);
            }

            while (_baseline.Count < n)
            {
                _baseline.Add(_src.Value(_baseline.Count));
            }

            if (n == 0)
            {
                var empty = new Label("つまみはまだありません。「＋ つまみを追加」から色やサイズを足すと、ここで回せます。");
                empty.AddToClassList("ux-dim");
                empty.AddToClassList("ux-knobs-empty");
                _list.Add(empty);
            }

            for (var i = 0; i < n; i++)
            {
                var k = BuildKnob(i);
                _knobs.Add(k);
                _list.Add(k.Row);
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
            k.Chip.RegisterCallback<ClickEvent>(e => OpenCategoryMenu(k));
            line.Add(k.Chip);

            k.Name = new Label();
            k.Name.AddToClassList("ux-knob__name");
            line.Add(k.Name);

            var control = BuildControl(k);
            control.AddToClassList("ux-knob__control");
            line.Add(control);

            k.Reset = new Button(() => ResetKnob(k)) { text = "↺", tooltip = "最初に開いたときの値に戻す" };
            k.Reset.AddToClassList("ux-btn");
            k.Reset.AddToClassList("ux-btn--icon");
            k.Reset.AddToClassList("ux-knob__reset");
            line.Add(k.Reset);

            var gear = new Button { text = "⚙", tooltip = "定義の詳細(名前 / 型 / 反映先 など)" };
            gear.AddToClassList("ux-btn");
            gear.AddToClassList("ux-btn--icon");
            line.Add(gear);

            k.Details = BuildDetails(k);
            k.Details.Show(_openDetails.Contains(i));
            main.Add(k.Details);
            gear.clicked += () =>
            {
                var open = !k.Details.IsShown();
                k.Details.Show(open);
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

                case TuneValueKind.Float:
                {
                    var b = _baseline[i].FloatValue;
                    var hi = b > 1f ? b * 4f : 1f;
                    var lo = b < 0f ? b * 4f : 0f;
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

        private VisualElement BuildDetails(Knob k)
        {
            var box = new VisualElement();
            box.AddToClassList("ux-details");
            var arr = _so.FindProperty(_src.ArrayPath);
            if (arr == null || k.Index >= arr.arraySize)
            {
                return box;
            }

            var el = arr.GetArrayElementAtIndex(k.Index);
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

            var remove = new Button(() => RemoveKnob(k.Index)) { text = "このつまみを削除" };
            remove.AddToClassList("ux-btn");
            remove.AddToClassList("ux-btn--small");
            box.Add(remove);
            return box;
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
                && a.VectorValue == b.VectorValue && a.ObjectValue == b.ObjectValue;
        }

        private int CategoryOf(int i)
        {
            var saved = SessionState.GetInt(CatKey(_src.Label(i)), -1);
            return saved >= 0 && saved < TuneCategories.Names.Length ? saved : _src.InferCategory(i);
        }

        private string CatKey(string label) => "DDrive.Ux.cat." + _src.OwnerKey + "." + label;

        private void UpdateKnobState(Knob k)
        {
            if (k.Index >= _src.Count)
            {
                return;
            }

            var cat = CategoryOf(k.Index);
            var chip = TuneCategories.Names[cat];
            if (k.Chip.text != chip)
            {
                k.Chip.text = chip;
            }

            for (var c = 0; c < TuneCategories.Names.Length; c++)
            {
                k.Chip.EnableInClassList("ux-cat--" + c, c == cat);
            }

            var rawLabel = _src.Label(k.Index);
            var label = string.IsNullOrEmpty(rawLabel) ? "(名前未設定)" : rawLabel;
            if (k.Name.text != label)
            {
                k.Name.text = label;
            }

            var target = _src.TargetName(k.Index);
            k.Name.tooltip = string.IsNullOrEmpty(target) ? "反映先が未設定のため反映されません" : "→ " + target;
            var modified = k.Index < _baseline.Count && !SameValue(_src.Value(k.Index), _baseline[k.Index]);
            k.Row.EnableInClassList("ux-knob--mod", modified);
            k.Row.EnableInClassList("ux-knob--nolink", string.IsNullOrEmpty(target));
        }

        // ── 分類 ──

        private void OpenCategoryMenu(Knob k)
        {
            var menu = new GenericMenu();
            var cur = CategoryOf(k.Index);
            for (var c = 0; c < TuneCategories.Names.Length; c++)
            {
                var cat = c;
                menu.AddItem(new GUIContent(TuneCategories.Names[c]), c == cur, () =>
                {
                    SessionState.SetInt(CatKey(_src.Label(k.Index)), cat);
                    RefreshChrome();
                });
            }

            menu.ShowAsContext();
        }

        private void RefreshChrome()
        {
            var counts = new int[TuneCategories.Names.Length];
            for (var i = 0; i < _knobs.Count; i++)
            {
                UpdateKnobState(_knobs[i]);
                counts[CategoryOf(i)]++;
            }

            if (_filter >= 0 && (_filter >= counts.Length || counts[_filter] == 0))
            {
                _filter = -1;
            }

            for (var i = 0; i < _knobs.Count; i++)
            {
                _knobs[i].Row.Show(_filter < 0 || CategoryOf(i) == _filter);
            }

            var key = _filter + ":" + _knobs.Count + ":" + string.Join(",", counts);
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
                    AddFilterChip(TuneCategories.Names[c] + " " + counts[c], c);
                }
            }
        }

        private void AddFilterChip(string text, int cat)
        {
            var b = new Button(() =>
            {
                _filter = cat;
                RefreshChrome();
            }) { text = text, tooltip = cat < 0 ? "すべてのつまみを表示" : TuneCategories.Names[cat] + "のつまみだけ表示" };
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
