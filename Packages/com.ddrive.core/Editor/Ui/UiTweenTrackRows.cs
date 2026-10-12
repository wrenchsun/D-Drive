using System;
using System.Collections.Generic;
using DDrive.Editor.CanvasTool;
using DDrive.Editor.Ux;
using DDrive.Foundation.Data;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ui
{
    internal delegate void TrackEdit(ref TweenTrack track);

    // トラック一覧(UiTweenTrackList / UiTweenTrackRow)が窓に頼むこと。窓(UiTweenEditor)が実装する。
    internal interface IUiTweenTrackHost
    {
        UiTweenData Data { get; }

        SerializedObject So { get; }

        // 時間バーの軸の長さ(秒)。全トラックで共通。
        float AxisMax { get; }

        // 1 トラックの書き換え(Undo + SetDirty)。final = false はドラッグ中(検証を省く)。
        void EditTrack(int index, TrackEdit edit, string undoName, bool final);

        // トラックの追加・削除・複製・並べ替え(配列ごと)。selectIndex >= 0 なら、そのトラックの ⚙ を開いた状態にする。
        void EditTracks(Func<TweenTrack[], TweenTrack[]> change, string undoName, int selectIndex);

        // ▶(そのトラックだけを実要素で再生)。
        void PlayTrack(int index);

        // ⚙ を開いたトラック(SceneView の制御点ハンドルの対象)。
        void OpenedTrack(int index, bool open);

        void Say(string text);
    }

    // トラックの一覧(docs/1010 D-10: 1 トラック = 1 行・絞り込みと件数・20 行を超えたら ListView・行の展開はその行だけ)。
    // CanvasItemList(絞り込み・件数・仮想化)に 1 行ずつ UiTweenTrackRow を載せる。毎 150ms の Sync は「数と項目の署名」の比較だけで、
    // 署名が変わったとき(追加・削除・並べ替え・項目の変更)だけ作り直す。値の変更(長さ・遅れ・イージング・値)は行を作り直さず、見えている行の表示だけ更新する。
    internal sealed class UiTweenTrackList : VisualElement, IUxSyncable
    {
        private readonly IUiTweenTrackHost _host;
        private readonly CanvasItemList _list;
        private readonly Label _limit;
        private readonly HashSet<int> _open = new HashSet<int>();
        private readonly List<UiTweenTrackRow> _rows = new List<UiTweenTrackRow>();
        private int _structure = int.MinValue;
        private int _values = int.MinValue;

        public UiTweenTrackList(IUiTweenTrackHost host)
        {
            _host = host;
            AddToClassList("ux-ut-list");

            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            var add = new Button(ShowAddMenu) { text = "＋ トラック", tooltip = "動かす項目(位置・拡縮・回転・透明度・色など)を 1 本足す。足したトラックの ⚙ が開きます" };
            add.AddToClassList("ux-btn");
            add.AddToClassList("ux-btn--primary");
            line.Add(add);
            Add(line);

            _limit = new Label();
            _limit.AddToClassList("ux-lineflag");
            _limit.Show(false);
            Add(_limit);

            _list = new CanvasItemList("トラック", "ループ", UiTweenTrackInfo.KindNames, "トラックがありません。「＋ トラック」か、上の「雛形から始める」で足してください。", "1 回だけ");
            Add(_list);
            Rebuild();
        }

        public CanvasItemList List => _list;

        public int OpenCount => _open.Count;

        public IReadOnlyList<UiTweenTrackRow> Rows => _rows;

        public bool IsOpen(int index) => _open.Contains(index);

        public void SetExternalQuery(string query) => _list.SetExternalQuery(query);

        public bool AnyMatch(string query) => _list.AnyMatch(query);

        internal void SetOpen(int index, bool open)
        {
            if (open)
            {
                _open.Add(index);
            }
            else
            {
                _open.Remove(index);
            }

            _host.OpenedTrack(index, open);
        }

        // 追加・削除・複製・並べ替えのあとの ⚙ の状態(index を開いた状態にし、ほかは閉じる)。
        public void ResetOpen(int selectIndex)
        {
            _open.Clear();
            if (selectIndex >= 0)
            {
                _open.Add(selectIndex);
            }

            _host.OpenedTrack(selectIndex, selectIndex >= 0);
        }

        private void ShowAddMenu()
        {
            var menu = new GenericMenu();
            foreach (var p in UiTweenTrackInfo.AddChoices)
            {
                var prop = p;
                menu.AddItem(new GUIContent(UiTweenTrackInfo.PropertyLabel(prop) + (prop == TweenProperty.PathMove ? "(経路をスプラインで)" : string.Empty)), false, () => AddTrack(prop));
            }

            menu.ShowAsContext();
        }

        public void AddTrack(TweenProperty property)
        {
            var count = _host.Data != null && _host.Data.Tracks != null ? _host.Data.Tracks.Length : 0;
            _host.EditTracks(tracks => UiTweenTrackInfo.Insert(tracks, count, UiTweenTrackInfo.NewTrack(property)), "UiTweenData: Track を追加", count);
            if (count >= UiTweenManager.MaxTracksPerTween)
            {
                _host.Say($"Track が {UiTweenManager.MaxTracksPerTween} 本を超えました(9 本目以降は再生されません)");
            }
        }

        // 一覧を作り直す(署名も取り直す)。
        public void Rebuild()
        {
            var data = _host.Data;
            _structure = StructureSignature(data);
            _values = UiTweenTrackInfo.Signature(data);
            _rows.Clear();
            var items = new List<CanvasListItem>();
            var tracks = data != null ? data.Tracks : null;
            for (var i = 0; tracks != null && i < tracks.Length; i++)
            {
                var index = i;
                var t = tracks[i];
                items.Add(new CanvasListItem
                {
                    Key = "trk:" + i,
                    Title = UiTweenTrackInfo.PropertyLabel(t.Property),
                    Kind = UiTweenTrackInfo.KindOf(t.Property),
                    IsConfigured = () => IsLoopingAt(index),
                    Search = "#" + i + " " + UiTweenTrackInfo.SearchText(t),
                    Make = () => MakeRow(index),
                });
            }

            _list.SetItems(items);
            UpdateLimit();
        }

        private bool IsLoopingAt(int index)
        {
            var tracks = _host.Data != null ? _host.Data.Tracks : null;
            return tracks != null && index >= 0 && index < tracks.Length && UiTweenTrackInfo.IsLooping(tracks[index]);
        }

        private VisualElement MakeRow(int index)
        {
            // 同じ番号の行を作り直したとき(絞り込み・ListView の再利用)は、古い行を捨てて入れ替える。
            _rows.RemoveAll(r => r == null || r.Index == index);
            var row = new UiTweenTrackRow(_host, this, index);
            _rows.Add(row);
            return row;
        }

        private static int StructureSignature(UiTweenData data)
        {
            if (data == null || data.Tracks == null)
            {
                return 0;
            }

            unchecked
            {
                var h = 17 * 31 + data.Tracks.Length;
                for (var i = 0; i < data.Tracks.Length; i++)
                {
                    h = h * 31 + (int)data.Tracks[i].Property;
                }

                return h;
            }
        }

        private void UpdateLimit()
        {
            var n = _host.Data != null && _host.Data.Tracks != null ? _host.Data.Tracks.Length : 0;
            var over = n > UiTweenManager.MaxTracksPerTween;
            _limit.Show(over);
            if (over)
            {
                _limit.text = $"⚠ {n} 本あります。同時に動かせるのは {UiTweenManager.MaxTracksPerTween} 本までで、{UiTweenManager.MaxTracksPerTween + 1} 本目以降は再生されません。";
            }
        }

        public void Sync()
        {
            var data = _host.Data;
            var structure = StructureSignature(data);
            if (structure != _structure)
            {
                Rebuild();
                return;
            }

            var values = UiTweenTrackInfo.Signature(data);
            if (values == _values)
            {
                return;
            }

            _values = values;
            _rows.RemoveAll(r => r == null || r.panel == null);
            for (var i = 0; i < _rows.Count; i++)
            {
                _rows[i].Refresh();
            }

            _list.UpdateCount();
            UpdateLimit();
        }
    }

    // トラック 1 本 = 1 行。1 行目: 番号・種類のチップ・項目名・時間バー(開始と長さをドラッグ)・▶ ↺ ⚙ ×、2 行目: 「開始 → 終了 · 長さ · イージング」の要約。
    // ⚙ はその行だけを開いて、すべての欄(項目・動き・遅れ・開始 / 終了の値・経路)を出す。
    internal sealed class UiTweenTrackRow : VisualElement
    {
        private readonly IUiTweenTrackHost _host;
        private readonly UiTweenTrackList _list;
        private readonly int _index;
        private readonly Label _chip;
        private readonly Label _name;
        private readonly Label _summary;
        private readonly Label _flag;
        private readonly TrackTimeBar _bar;
        private readonly Button _gear;
        private readonly VisualElement _details;
        private readonly List<ValueEditor> _valueEditors = new List<ValueEditor>();
        private TweenProperty _builtFor;
        private bool _built;
        private VisualElement _fromBox;
        private VisualElement _toBox;
        private VisualElement _offBox;
        private VisualElement _pathBox;
        private EnumField _propertyField;

        public int Index => _index;

        public bool IsOpen => _details.IsShown();

        public UiTweenTrackRow(IUiTweenTrackHost host, UiTweenTrackList list, int index)
        {
            _host = host;
            _list = list;
            _index = index;
            AddToClassList("ux-fxrow");
            AddToClassList("ux-ut-row");

            var head = new VisualElement();
            head.AddToClassList("ux-fxrow__head");
            head.AddToClassList("ux-trk__head");
            head.AddToClassList("ux-ut-head");

            var idx = new Label("#" + index) { tooltip = "トラックの番号(上から順。同時に動かせるのは 8 本まで)" };
            idx.AddToClassList("ux-dim");
            idx.AddToClassList("ux-trk__idx");
            head.Add(idx);

            _chip = new Label();
            _chip.AddToClassList("ux-chip");
            _chip.AddToClassList("ux-fxrow__chip");
            head.Add(_chip);

            _name = new Label();
            _name.AddToClassList("ux-fxrow__name");
            _name.AddToClassList("ux-ut-name");
            _name.tooltip = "クリックで ⚙(すべての欄)を開く / 閉じる";
            _name.RegisterCallback<ClickEvent>(e => Toggle());
            head.Add(_name);

            _bar = new TrackTimeBar(host.Data, () => Current(), () => host.AxisMax, WriteTiming);
            head.Add(_bar);

            var play = MakeIcon("▶", "このトラックだけを、対象の要素で再生する(下の「実際の要素で確かめる」で対象を選ぶ)", () => host.PlayTrack(_index));
            head.Add(play);
            var reset = MakeIcon("↺", "このトラックを既定値に戻す(項目は残す。Undo で戻せます)", () =>
            {
                host.EditTrack(_index, (ref TweenTrack t) => t = UiTweenTrackInfo.ResetTrack(t), "UiTweenData: Track を既定に戻す", true);
                Refresh();
            });
            head.Add(reset);
            _gear = MakeIcon("⚙", "すべての欄(項目・動き・遅れ・開始 / 終了の値・経路)を開く / 閉じる", Toggle);
            head.Add(_gear);
            var del = MakeIcon("×", "このトラックを削除する(Undo で戻せます)", () =>
                host.EditTracks(tracks => UiTweenTrackInfo.RemoveAt(tracks, _index), "UiTweenData: Track を削除", -1));
            head.Add(del);
            Add(head);

            _summary = new Label();
            _summary.AddToClassList("ux-dim");
            _summary.AddToClassList("ux-ut-sum");
            Add(_summary);

            _flag = new Label();
            _flag.AddToClassList("ux-lineflag");
            _flag.AddToClassList("ux-ut-flag");
            Add(_flag);

            _details = new VisualElement();
            _details.AddToClassList("ux-fxrow__details");
            Add(_details);

            Refresh();
            SetOpen(list.IsOpen(index), false);
        }

        private static Button MakeIcon(string text, string tooltip, Action click)
        {
            var b = new Button(click) { text = text, tooltip = tooltip };
            b.AddToClassList("ux-btn");
            b.AddToClassList("ux-btn--icon");
            return b;
        }

        private TweenTrack Current()
        {
            var tracks = _host.Data != null ? _host.Data.Tracks : null;
            return tracks != null && _index < tracks.Length ? tracks[_index] : default;
        }

        private bool Exists()
        {
            var tracks = _host.Data != null ? _host.Data.Tracks : null;
            return tracks != null && _index >= 0 && _index < tracks.Length;
        }

        private void WriteTiming(float delay, float duration, bool final)
        {
            _host.EditTrack(_index, (ref TweenTrack t) =>
            {
                t.Delay = delay;
                if (t.Motion.Time.Mode == DDrive.Foundation.Values.TimeMode.Duration)
                {
                    var m = t.Motion;
                    var time = m.Time;
                    time.Value = duration;
                    m.Time = time;
                    t.Motion = m;
                }
            }, "UiTweenData: トラックの時間", final);
            if (!final)
            {
                RefreshText();
            }
        }

        // 表示を最新にする(行を作り直さない)。
        public void Refresh()
        {
            if (!Exists())
            {
                return;
            }

            RefreshText();
            _bar.Sync();
            if (IsOpen)
            {
                RefreshDetails();
            }
        }

        private void RefreshText()
        {
            var t = Current();
            var kind = UiTweenTrackInfo.KindOf(t.Property);
            for (var k = 0; k < UiTweenTrackInfo.KindNames.Length; k++)
            {
                _chip.EnableInClassList("ux-kind--" + k, k == kind);
            }

            _chip.text = UiTweenTrackInfo.KindNames[kind];
            _name.text = UiTweenTrackInfo.PropertyLabel(t.Property);
            var summary = UiTweenTrackInfo.Summary(t);
            if (_summary.text != summary)
            {
                _summary.text = summary;
                _summary.tooltip = summary;
            }

            var over = _index >= UiTweenManager.MaxTracksPerTween;
            _flag.Show(over);
            if (over)
            {
                _flag.text = $"⚠ {_index + 1} 本目: 同時に動かせるのは {UiTweenManager.MaxTracksPerTween} 本までなので、このトラックは再生されません";
            }
        }

        private void Toggle() => SetOpen(!IsOpen, true);

        private void SetOpen(bool open, bool notify)
        {
            _details.Show(open);
            _gear.EnableInClassList("ux-btn--on", open);
            EnableInClassList("ux-fxrow--open", open);
            if (open && (!_built || _builtFor != Current().Property))
            {
                BuildDetails();
            }

            if (open)
            {
                RefreshDetails();
            }

            if (notify)
            {
                _list.SetOpen(_index, open);
            }
        }

        // ── ⚙ の中身(全欄) ──

        private sealed class ValueEditor
        {
            public VisualElement Field;
            public Func<ParamValue> Get;
            public Action<VisualElement, ParamValue> Show;
        }

        private void BuildDetails()
        {
            _details.Clear();
            _valueEditors.Clear();
            _built = true;
            var t = Current();
            _builtFor = t.Property;
            var so = _host.So;
            var element = so?.FindProperty(nameof(UiTweenData.Tracks))?.GetArrayElementAtIndex(_index);
            if (element == null)
            {
                return;
            }

            _propertyField = new EnumField("項目(Property)", t.Property) { tooltip = "動かす対象。変えると開始 / 終了の値の型も合わせます" };
            _propertyField.AddToClassList("ux-ut-field");
            _propertyField.RegisterValueChangedCallback(e =>
            {
                var next = (TweenProperty)e.newValue;
                _host.EditTrack(_index, (ref TweenTrack tr) =>
                {
                    if (tr.Property == next)
                    {
                        return;
                    }

                    tr.Property = next;
                    UiTweenTrackInfo.NormalizeValues(ref tr);
                    if (next == TweenProperty.PathMove && !tr.Path.IsValid)
                    {
                        tr.Path = UiTweenTrackInfo.NewTrack(TweenProperty.PathMove).Path;
                    }
                }, "UiTweenData: Track の項目を変更", true);
            });
            _details.Add(_propertyField);

            AddProperty(_details, so, element, "Motion", "動き(Motion)");
            AddProperty(_details, so, element, "Delay", "開始の遅れ(Delay)");
            AddProperty(_details, so, element, "From", "開始値の決め方(From)");

            _offBox = new VisualElement();
            AddProperty(_offBox, so, element, "OffScreen", "画面外の方向(OffScreen)");
            _details.Add(_offBox);

            _fromBox = new VisualElement();
            _fromBox.Add(MakeValueEditor("開始値(FromValue)", true));
            _details.Add(_fromBox);

            _toBox = new VisualElement();
            _toBox.Add(MakeValueEditor("終了値(ToValue)", false));
            _details.Add(_toBox);

            _pathBox = new VisualElement();
            AddProperty(_pathBox, so, element, "Path", "経路(Path)");
            var pathTools = new VisualElement();
            pathTools.AddToClassList("ux-toolsline");
            pathTools.Add(MakeButton("＋点を追加", "経路の最後に制御点を 1 つ足す(50px 右)", AddSplinePoint));
            pathTools.Add(MakeButton("−最後の点を削除", "経路の最後の制御点を 1 つ消す", RemoveLastSplinePoint));
            _pathBox.Add(pathTools);
            var pathNote = new Label("このウィンドウがフォーカスされている間、SceneView 上のハンドルで制御点をドラッグ編集できます(プレビュー対象のローカル座標系)。");
            pathNote.AddToClassList("ux-dim");
            pathNote.AddToClassList("ux-note");
            _pathBox.Add(pathNote);
            _details.Add(_pathBox);

            var tools = new VisualElement();
            tools.AddToClassList("ux-toolsline");
            tools.Add(MakeButton("⧉ 複製", "このトラックの直後に同じものを足す", () =>
                _host.EditTracks(tracks => UiTweenTrackInfo.Insert(tracks, _index + 1, tracks[_index]), "UiTweenData: Track を複製", _index + 1)));
            tools.Add(MakeButton("↑ 上へ", "1 つ上へ(再生には影響しません。見た目の並びだけ)", () => Move(-1)));
            tools.Add(MakeButton("↓ 下へ", "1 つ下へ(再生には影響しません。見た目の並びだけ)", () => Move(1)));
            _details.Add(tools);
        }

        private static void AddProperty(VisualElement parent, SerializedObject so, SerializedProperty element, string relative, string label)
        {
            var prop = element.FindPropertyRelative(relative);
            if (prop == null)
            {
                return;
            }

            var pf = new PropertyField(prop, label);
            pf.AddToClassList("ux-ut-field");
            pf.BindProperty(prop);
            parent.Add(pf);
        }

        private static Button MakeButton(string text, string tooltip, Action click)
        {
            var b = new Button(click) { text = text, tooltip = tooltip };
            b.AddToClassList("ux-btn");
            return b;
        }

        private void Move(int delta)
        {
            var to = _index + delta;
            var tracks = _host.Data != null ? _host.Data.Tracks : null;
            if (tracks == null || to < 0 || to >= tracks.Length)
            {
                return;
            }

            _host.EditTracks(arr =>
            {
                UiTweenTrackInfo.Swap(arr, _index, to);
                return arr;
            }, "UiTweenData: Track を並べ替え", to);
        }

        private void AddSplinePoint()
        {
            _host.EditTrack(_index, (ref TweenTrack t) =>
            {
                var points = t.Path.Points ?? Array.Empty<Vector3>();
                var last = points.Length > 0 ? points[points.Length - 1] : Vector3.zero;
                var next = new Vector3[points.Length + 1];
                Array.Copy(points, next, points.Length);
                next[points.Length] = last + new Vector3(50f, 0f, 0f);
                t.Path.Points = next;
            }, "UiTweenData: スプライン点を追加", true);
        }

        private void RemoveLastSplinePoint()
        {
            _host.EditTrack(_index, (ref TweenTrack t) =>
            {
                var points = t.Path.Points;
                if (points == null || points.Length == 0)
                {
                    return;
                }

                var next = new Vector3[points.Length - 1];
                Array.Copy(points, next, next.Length);
                t.Path.Points = next;
            }, "UiTweenData: スプライン点を削除", true);
        }

        // 開始値 / 終了値の欄(項目に合わせた型: 位置 / サイズ = 2 値、拡縮 = 3 値、回転 / 透明度 / 塗り / 色相 = 数値、色 = 色)。
        private VisualElement MakeValueEditor(string label, bool isFrom)
        {
            var property = Current().Property;
            var editor = new ValueEditor();
            _valueEditors.Add(editor);
            ParamValue Get() => isFrom ? Current().FromValue : Current().ToValue;
            editor.Get = Get;

            void Write(ParamValue v)
            {
                _host.EditTrack(_index, (ref TweenTrack t) =>
                {
                    if (isFrom)
                    {
                        t.FromValue = v;
                    }
                    else
                    {
                        t.ToValue = v;
                    }
                }, isFrom ? "UiTweenData: 開始値を変更" : "UiTweenData: 終了値を変更", true);
            }

            switch (property)
            {
                case TweenProperty.AnchoredPosition:
                case TweenProperty.SizeDelta:
                {
                    var f = new Vector2Field(label);
                    f.RegisterValueChangedCallback(e =>
                    {
                        var v = Get();
                        v.Type = ParamValueType.Vector;
                        v.VectorValue = new Vector4(e.newValue.x, e.newValue.y, v.VectorValue.z, 0f);
                        Write(v);
                    });
                    editor.Field = f;
                    editor.Show = (el, v) => ((Vector2Field)el).SetValueWithoutNotify(new Vector2(v.VectorValue.x, v.VectorValue.y));
                    break;
                }

                case TweenProperty.Scale:
                {
                    var f = new Vector3Field(label);
                    f.RegisterValueChangedCallback(e =>
                    {
                        var v = Get();
                        v.Type = ParamValueType.Vector;
                        v.VectorValue = new Vector4(e.newValue.x, e.newValue.y, e.newValue.z, 0f);
                        Write(v);
                    });
                    editor.Field = f;
                    editor.Show = (el, v) => ((Vector3Field)el).SetValueWithoutNotify(new Vector3(v.VectorValue.x, v.VectorValue.y, v.VectorValue.z));
                    break;
                }

                case TweenProperty.Color:
                {
                    var f = new ColorField(label);
                    f.RegisterValueChangedCallback(e =>
                    {
                        var v = Get();
                        v.Type = ParamValueType.Color;
                        v.ColorValue = e.newValue;
                        Write(v);
                    });
                    editor.Field = f;
                    editor.Show = (el, v) => ((ColorField)el).SetValueWithoutNotify(v.ColorValue);
                    break;
                }

                case TweenProperty.PathMove:
                {
                    var none = new Label(label + ": 経路移動では使いません(下の経路の制御点で決まります)");
                    none.AddToClassList("ux-dim");
                    none.AddToClassList("ux-note");
                    editor.Field = none;
                    editor.Show = (el, v) => { };
                    break;
                }

                default:
                {
                    var f = new FloatField(label);
                    f.RegisterValueChangedCallback(e =>
                    {
                        var v = Get();
                        v.Type = ParamValueType.Float;
                        v.FloatValue = e.newValue;
                        Write(v);
                    });
                    editor.Field = f;
                    editor.Show = (el, v) => ((FloatField)el).SetValueWithoutNotify(v.Type == ParamValueType.Float ? v.FloatValue : v.VectorValue.x);
                    break;
                }
            }

            editor.Field.AddToClassList("ux-ut-field");
            return editor.Field;
        }

        private static bool IsFocusedWithin(VisualElement element)
        {
            var focused = element.panel?.focusController?.focusedElement as VisualElement;
            return focused != null && (focused == element || element.Contains(focused));
        }

        private void RefreshDetails()
        {
            if (!_built || !Exists())
            {
                return;
            }

            var t = Current();
            if (_builtFor != t.Property)
            {
                BuildDetails();
            }

            _propertyField.SetValueWithoutNotify(t.Property);
            _offBox.Show(t.From == TweenFromMode.OffScreen);
            _fromBox.Show(t.From == TweenFromMode.Absolute || t.From == TweenFromMode.Relative);
            _toBox.Show(t.From != TweenFromMode.OffScreen && t.Property != TweenProperty.PathMove);
            _pathBox.Show(t.Property == TweenProperty.PathMove);
            for (var i = 0; i < _valueEditors.Count; i++)
            {
                var e = _valueEditors[i];
                if (e.Field != null && !IsFocusedWithin(e.Field))
                {
                    e.Show(e.Field, UiTweenTrackInfo.Coerce(t.Property, e.Get()));
                }
            }
        }
    }
}
