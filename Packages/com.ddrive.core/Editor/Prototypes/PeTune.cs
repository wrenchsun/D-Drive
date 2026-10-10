using System;
using System.Collections.Generic;
using System.Text;
using DDrive.Foundation.Data;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.EditorPrototypes
{
    // E: Params の欄を「調整つまみ」に置き換える PdRow。1 つまみ = 1 行(分類チップ / 名前 / 部品 / ↺ / ⚙)。
    internal sealed class PeTuneRow : PdRow
    {
        private readonly PeTunePanel _panel;

        public PeTuneRow(SerializedObject so, SerializedObject defaultSo, IReadOnlyList<FieldGuideEntry> entries, string hint, PeTunePanel panel, Action changed)
            : base(so, defaultSo, entries, "調整つまみ", hint, FieldTier.Common, true, panel, changed)
        {
            _panel = panel;
            NoFold = true;
            AddToClassList("pe-tune");
        }

        public override void Sync() => _panel.Sync();
    }

    internal sealed class PeTunePanel : VisualElement
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

        private readonly VfxData _data;
        private readonly SerializedObject _so;
        private readonly Action _edited;      // ドラッグ中も呼ぶ軽い通知
        private readonly Action _structural;  // 追加・削除など(検証を含む)
        private readonly Action<string, ParamValue> _live;
        private readonly string _guid;
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

        public PeTunePanel(VfxData data, SerializedObject so, Action edited, Action structural, Action<string, ParamValue> live)
        {
            _data = data;
            _so = so;
            _edited = edited;
            _structural = structural;
            _live = live;
            _guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data));
            AddToClassList("pe-panel");

            _filterBar = new VisualElement();
            _filterBar.AddToClassList("pe-filters");
            Add(_filterBar);

            _list = new VisualElement();
            _list.AddToClassList("pe-knobs");
            Add(_list);

            var addRow = new VisualElement();
            addRow.AddToClassList("pe-addrow");
            var add = new Button { text = "＋ つまみを追加" };
            add.AddToClassList("pd-btn");
            add.AddToClassList("pd-btn--primary");
            addRow.Add(add);
            Add(addRow);

            _addPanel = new VisualElement();
            _addPanel.AddToClassList("pe-palette");
            _addPanel.style.display = DisplayStyle.None;
            for (var i = 0; i < PeProps.KindNames.Length; i++)
            {
                var kind = (PeProps.Kind)i;
                var b = new Button(() => AddKnob(kind, add)) { text = PeProps.KindNames[i] };
                b.AddToClassList("pd-btn");
                b.AddToClassList("pe-palette__btn");
                _addPanel.Add(b);
            }

            Add(_addPanel);

            _message = new Label();
            _message.AddToClassList("pe-message");
            _message.AddToClassList("pe-hidden");
            Add(_message);

            add.clicked += () =>
            {
                _addPanel.style.display = _addPanel.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
                _message.text = string.Empty;
                _message.EnableInClassList("pe-hidden", true);
            };

            var ps = _data.Params;
            for (var i = 0; ps != null && i < ps.Length; i++)
            {
                _baseline.Add(ps[i].Default);
            }

            Rebuild();
        }

        // ── 構築 ──

        private static string Signature(VfxParam[] ps)
        {
            if (ps == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (var i = 0; i < ps.Length; i++)
            {
                sb.Append((int)ps[i].Type).Append(';');
            }

            return sb.ToString();
        }

        private void Rebuild()
        {
            _signature = Signature(_data.Params);
            _list.Clear();
            _knobs.Clear();
            _filterKey = string.Empty;
            var ps = _data.Params;
            var n = ps != null ? ps.Length : 0;
            while (_baseline.Count > n)
            {
                _baseline.RemoveAt(_baseline.Count - 1);
            }

            while (_baseline.Count < n)
            {
                _baseline.Add(ps[_baseline.Count].Default);
            }

            if (n == 0)
            {
                var empty = new Label("つまみはまだありません。「＋ つまみを追加」から色やサイズを足すと、ここで回せます。");
                empty.AddToClassList("pd-dim");
                empty.AddToClassList("pe-empty");
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
            var p = _data.Params[i];
            var k = new Knob { Index = i };
            var row = new VisualElement();
            row.AddToClassList("pe-knob");
            k.Row = row;

            var bar = new VisualElement { tooltip = "最初に開いたときの値と違います" };
            bar.AddToClassList("pe-knob__bar");
            row.Add(bar);

            var main = new VisualElement();
            main.AddToClassList("pe-knob__main");
            row.Add(main);

            var line = new VisualElement();
            line.AddToClassList("pe-knob__line");
            main.Add(line);

            k.Chip = new Label { tooltip = "分類(クリックで変える)" };
            k.Chip.AddToClassList("pd-chip");
            k.Chip.AddToClassList("pe-cat");
            k.Chip.RegisterCallback<ClickEvent>(e => OpenCategoryMenu(k));
            line.Add(k.Chip);

            k.Name = new Label();
            k.Name.AddToClassList("pe-knob__name");
            line.Add(k.Name);

            var control = BuildControl(k, p);
            control.AddToClassList("pe-knob__control");
            line.Add(control);

            k.Reset = new Button(() => ResetKnob(k)) { text = "↺", tooltip = "最初に開いたときの値に戻す" };
            k.Reset.AddToClassList("pd-btn");
            k.Reset.AddToClassList("pd-btn--icon");
            k.Reset.AddToClassList("pe-knob__reset");
            line.Add(k.Reset);

            var gear = new Button { text = "⚙", tooltip = "定義の詳細(Label / Type / TargetProperty / Anim)" };
            gear.AddToClassList("pd-btn");
            gear.AddToClassList("pd-btn--icon");
            line.Add(gear);

            k.Details = BuildDetails(k);
            k.Details.style.display = _openDetails.Contains(i) ? DisplayStyle.Flex : DisplayStyle.None;
            main.Add(k.Details);
            gear.clicked += () =>
            {
                var open = k.Details.style.display == DisplayStyle.None;
                k.Details.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
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

        private VisualElement BuildControl(Knob k, VfxParam p)
        {
            var i = k.Index;
            switch (p.Type)
            {
                case VfxParamType.Color:
                {
                    var c = p.Default.ColorValue;
                    var f = new ColorField { value = c, showAlpha = true, hdr = c.maxColorComponent > 1f };
                    f.RegisterValueChangedCallback(e => Write(i, ParamValue.Of(e.newValue)));
                    k.SetUi = v => f.SetValueWithoutNotify(v.ColorValue);
                    return f;
                }

                case VfxParamType.Float:
                {
                    var b = _baseline[i].FloatValue;
                    var hi = b > 1f ? b * 4f : 1f;
                    var lo = b < 0f ? b * 4f : 0f;
                    var s = new Slider(lo, hi) { value = p.Default.FloatValue, showInputField = true };
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

                case VfxParamType.Int:
                {
                    var b = _baseline[i].IntValue;
                    var hi = Mathf.Max(10, b * 4);
                    var lo = Mathf.Min(0, b * 4);
                    var s = new SliderInt(lo, hi) { value = p.Default.IntValue, showInputField = true };
                    s.RegisterValueChangedCallback(e => Write(i, ParamValue.Of(e.newValue)));
                    k.SetUi = v =>
                    {
                        s.highValue = Mathf.Max(s.highValue, v.IntValue);
                        s.lowValue = Mathf.Min(s.lowValue, v.IntValue);
                        s.SetValueWithoutNotify(v.IntValue);
                    };
                    return s;
                }

                case VfxParamType.Vector:
                {
                    var f = new Vector3Field { value = (Vector3)p.Default.VectorValue };
                    f.RegisterValueChangedCallback(e =>
                    {
                        var cur = _data.Params[i].Default.VectorValue;
                        Write(i, new ParamValue { Type = ParamValueType.Vector, VectorValue = new Vector4(e.newValue.x, e.newValue.y, e.newValue.z, cur.w) });
                    });
                    k.SetUi = v => f.SetValueWithoutNotify((Vector3)v.VectorValue);
                    return f;
                }

                case VfxParamType.Texture:
                {
                    var f = new ObjectField { objectType = typeof(Texture), value = p.Default.ObjectValue };
                    f.RegisterValueChangedCallback(e => Write(i, new ParamValue { Type = ParamValueType.Object, ObjectValue = e.newValue }));
                    k.SetUi = v => f.SetValueWithoutNotify(v.ObjectValue);
                    return f;
                }

                default:
                {
                    var l = new Label(p.Type == VfxParamType.Curve ? "カーブ(Inspector で編集)" : "グラデーション(Inspector で編集)");
                    l.AddToClassList("pd-dim");
                    return l;
                }
            }
        }

        private VisualElement BuildDetails(Knob k)
        {
            var box = new VisualElement();
            box.AddToClassList("pe-details");
            var arr = _so.FindProperty("Params");
            if (arr == null || k.Index >= arr.arraySize)
            {
                return box;
            }

            var el = arr.GetArrayElementAtIndex(k.Index);
            foreach (var name in new[] { "Label", "Type", "TargetProperty", "Anim" })
            {
                var prop = el.FindPropertyRelative(name);
                if (prop == null)
                {
                    continue;
                }

                var pf = new PropertyField(prop);
                pf.BindProperty(prop);
                box.Add(pf);
            }

            var remove = new Button(() => RemoveKnob(k.Index)) { text = "このつまみを削除" };
            remove.AddToClassList("pd-btn");
            remove.AddToClassList("pd-btn--small");
            box.Add(remove);
            return box;
        }

        // ── 値の書き込み(Default。Undo + SetDirty。再生中は即時反映) ──

        private void Write(int i, ParamValue v)
        {
            var ps = _data.Params;
            if (ps == null || i < 0 || i >= ps.Length)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            if (i == _gIndex && now - _gTime < 1.2)
            {
                Undo.RecordObject(_data, "つまみを調整");
                Undo.CollapseUndoOperations(_gGroup);
            }
            else
            {
                Undo.IncrementCurrentGroup();
                _gGroup = Undo.GetCurrentGroup();
                Undo.RecordObject(_data, "つまみを調整");
                _gIndex = i;
            }

            _gTime = now;
            ps[i].Default = v;
            EditorUtility.SetDirty(_data);
            _so.Update();
            if (_live != null)
            {
                _live(ps[i].Label, v);
            }

            if (i < _knobs.Count)
            {
                UpdateKnobState(_knobs[i]);
            }

            if (_edited != null)
            {
                _edited();
            }
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
            if (k.SetUi != null)
            {
                k.SetUi(v);
            }

            UpdateKnobState(k);
        }

        private static bool SameValue(ParamValue a, ParamValue b)
        {
            return Mathf.Approximately(a.FloatValue, b.FloatValue) && a.IntValue == b.IntValue && a.ColorValue == b.ColorValue
                && a.VectorValue == b.VectorValue && a.ObjectValue == b.ObjectValue;
        }

        private int CategoryOf(int i)
        {
            var p = _data.Params[i];
            var saved = SessionState.GetInt(CatKey(p.Label), -1);
            return saved >= 0 && saved < PeProps.CategoryNames.Length ? saved : PeProps.Infer(p);
        }

        private string CatKey(string label) => "DDrive.PrototypeE.cat." + _guid + "." + label;

        private void UpdateKnobState(Knob k)
        {
            var ps = _data.Params;
            if (ps == null || k.Index >= ps.Length)
            {
                return;
            }

            var p = ps[k.Index];
            var cat = CategoryOf(k.Index);
            var chip = PeProps.CategoryNames[cat];
            if (k.Chip.text != chip)
            {
                k.Chip.text = chip;
            }

            for (var c = 0; c < PeProps.CategoryNames.Length; c++)
            {
                k.Chip.EnableInClassList("pe-cat--" + c, c == cat);
            }

            var label = string.IsNullOrEmpty(p.Label) ? "(名前未設定)" : p.Label;
            if (k.Name.text != label)
            {
                k.Name.text = label;
            }

            k.Name.tooltip = string.IsNullOrEmpty(p.TargetProperty) ? "TargetProperty が未設定のため反映されません" : "→ " + p.TargetProperty;
            var modified = k.Index < _baseline.Count && !SameValue(p.Default, _baseline[k.Index]);
            k.Row.EnableInClassList("pe-knob--mod", modified);
            k.Row.EnableInClassList("pe-knob--nolink", string.IsNullOrEmpty(p.TargetProperty));
        }

        // ── 分類 ──

        private void OpenCategoryMenu(Knob k)
        {
            var menu = new GenericMenu();
            var cur = CategoryOf(k.Index);
            for (var c = 0; c < PeProps.CategoryNames.Length; c++)
            {
                var cat = c;
                menu.AddItem(new GUIContent(PeProps.CategoryNames[c]), c == cur, () =>
                {
                    SessionState.SetInt(CatKey(_data.Params[k.Index].Label), cat);
                    RefreshChrome();
                });
            }

            menu.ShowAsContext();
        }

        private void RefreshChrome()
        {
            var counts = new int[PeProps.CategoryNames.Length];
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
                _knobs[i].Row.style.display = _filter < 0 || CategoryOf(i) == _filter ? DisplayStyle.Flex : DisplayStyle.None;
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
                _filterBar.style.display = DisplayStyle.None;
                return;
            }

            _filterBar.style.display = DisplayStyle.Flex;
            AddFilterChip("すべて " + _knobs.Count, -1);
            for (var c = 0; c < counts.Length; c++)
            {
                if (counts[c] > 0)
                {
                    AddFilterChip(PeProps.CategoryNames[c] + " " + counts[c], c);
                }
            }
        }

        private void AddFilterChip(string text, int cat)
        {
            var b = new Button(() =>
            {
                _filter = cat;
                RefreshChrome();
            }) { text = text, tooltip = cat < 0 ? "すべてのつまみを表示" : PeProps.CategoryNames[cat] + "のつまみだけ表示" };
            b.AddToClassList("pe-filter");
            b.EnableInClassList("pe-filter--on", cat == _filter);
            _filterBar.Add(b);
        }

        // ── 追加・削除 ──

        private void AddKnob(PeProps.Kind kind, VisualElement anchor)
        {
            _message.EnableInClassList("pe-hidden", true);
            if (kind == PeProps.Kind.Free)
            {
                AppendParam(new VfxParam { Label = UniqueLabel("Param"), Type = VfxParamType.Float, TargetProperty = string.Empty, Default = ParamValue.Of(1f) }, true);
                return;
            }

            var used = new HashSet<string>();
            var ps = _data.Params;
            for (var i = 0; ps != null && i < ps.Length; i++)
            {
                if (!string.IsNullOrEmpty(ps[i].TargetProperty))
                {
                    used.Add(ps[i].TargetProperty);
                }
            }

            var all = PeProps.Enumerate(_data.Prefab);
            var found = PeProps.ForKind(all, kind, null);
            var cands = PeProps.ForKind(all, kind, used);
            var kindName = PeProps.KindNames[(int)kind];
            if (found.Count == 0)
            {
                Say(_data.Prefab == null ? "Prefab が未設定です。先に Prefab を入れてください" : "この Prefab には" + kindName + "のプロパティがありません");
                return;
            }

            if (cands.Count == 0)
            {
                Say("この Prefab の" + kindName + "のプロパティは、すべて追加済みです");
                return;
            }

            if (cands.Count == 1)
            {
                AppendCandidate(kind, cands[0]);
                return;
            }

            var menu = new GenericMenu();
            foreach (var c in cands)
            {
                var cand = c;
                menu.AddItem(new GUIContent(cand.Name), false, () => AppendCandidate(kind, cand));
            }

            menu.DropDown(anchor.worldBound);
        }

        private void Say(string text)
        {
            _message.text = text;
            _message.EnableInClassList("pe-hidden", false);
        }

        private void AppendCandidate(PeProps.Kind kind, PeProps.Candidate c)
        {
            AppendParam(new VfxParam
            {
                Label = UniqueLabel(PeProps.KindLabels[(int)kind]),
                Type = PeProps.ParamTypeOf(c.Type),
                TargetProperty = c.Name,
                Default = c.Value,
            }, false);
        }

        private string UniqueLabel(string baseName)
        {
            var ps = _data.Params;
            var label = baseName;
            var n = 1;
            while (Exists(ps, label))
            {
                n++;
                label = baseName + n;
            }

            return label;
        }

        private static bool Exists(VfxParam[] ps, string label)
        {
            for (var i = 0; ps != null && i < ps.Length; i++)
            {
                if (ps[i].Label == label)
                {
                    return true;
                }
            }

            return false;
        }

        private void AppendParam(VfxParam p, bool openDetails)
        {
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(_data, "つまみを追加");
            var old = _data.Params ?? new VfxParam[0];
            var arr = new VfxParam[old.Length + 1];
            Array.Copy(old, arr, old.Length);
            arr[old.Length] = p;
            _data.Params = arr;
            EditorUtility.SetDirty(_data);
            _so.Update();
            _gIndex = -1;
            _baseline.Add(p.Default);
            if (openDetails)
            {
                _openDetails.Add(old.Length);
            }

            _addPanel.style.display = DisplayStyle.None;
            Rebuild();
            if (_structural != null)
            {
                _structural();
            }
        }

        private void RemoveKnob(int index)
        {
            var old = _data.Params;
            if (old == null || index >= old.Length)
            {
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.RecordObject(_data, "つまみを削除");
            var arr = new VfxParam[old.Length - 1];
            for (int s = 0, d = 0; s < old.Length; s++)
            {
                if (s != index)
                {
                    arr[d++] = old[s];
                }
            }

            _data.Params = arr;
            EditorUtility.SetDirty(_data);
            _so.Update();
            _gIndex = -1;
            if (index < _baseline.Count)
            {
                _baseline.RemoveAt(index);
            }

            _openDetails.Clear();
            Rebuild();
            if (_structural != null)
            {
                _structural();
            }
        }

        // ── 外部変更の取り込み(150ms ごと / Undo) ──

        public void Sync()
        {
            var ps = _data.Params;
            if (Signature(ps) != _signature)
            {
                Rebuild();
                return;
            }

            for (var i = 0; ps != null && i < _knobs.Count && i < ps.Length; i++)
            {
                if (_knobs[i].SetUi != null)
                {
                    _knobs[i].SetUi(ps[i].Default);
                }
            }

            RefreshChrome();
        }
    }
}
