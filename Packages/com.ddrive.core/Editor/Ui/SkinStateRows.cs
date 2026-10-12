using System;
using System.Collections.Generic;
using DDrive.Editor.CanvasTool;
using DDrive.Editor.Ux;
using DDrive.Foundation.Values;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ui
{
    // Button Skin / Slider Skin の新エディター(docs/1010 UX-13)が共有する部品: 状態・パーツの一覧(1 状態 = 1 行 + 要約)。
    // 状態の ▶ / 演出の編集 / SE の試聴は、窓が持つ ControlSkinPreviewSection(確認する)へ ISkinStateHost 経由でつなぐ。

    // 一覧の 1 項目(状態 = StateVisual 1 つ分)。Slider Skin はこれに加えてパーツ(Track / Fill / Handle / DelayFill)を持つ。
    internal sealed class SkinPart
    {
        public string Field;               // SerializedObject のフィールド名(ControlSkinData / SliderSkinData)
        public string Label;               // 行の名前
        public string Meaning;             // 一言(ツールチップ)
        public int Kind;                   // 0 = 状態 / 1 = パーツ(一覧の種類チップ)
        public ControlState State;         // Kind == 0 のとき
        public Func<ControlSkinData, StateVisual> Get;

        public bool IsState => Kind == 0;
    }

    internal static class SkinParts
    {
        public static readonly string[] KindNames = { "状態", "パーツ" };

        public static readonly SkinPart[] States =
        {
            State(nameof(ControlSkinData.Normal), "Normal", "通常(何も起きていないとき)", ControlState.Normal, s => s.Normal),
            State(nameof(ControlSkinData.Hover), "Hover", "カーソルが乗っているとき", ControlState.Hover, s => s.Hover),
            State(nameof(ControlSkinData.Pressed), "Pressed", "押している間", ControlState.Pressed, s => s.Pressed),
            State(nameof(ControlSkinData.Selected), "Selected", "パッド / キーボードで選んでいるとき", ControlState.Selected, s => s.Selected),
            State(nameof(ControlSkinData.Disabled), "Disabled", "押せないとき(無効)", ControlState.Disabled, s => s.Disabled),
            State(nameof(ControlSkinData.Locked), "Locked", "ロック中(押すと拒否の SE が鳴る)", ControlState.Locked, s => s.Locked),
        };

        public static readonly SkinPart[] SliderParts =
        {
            Part(nameof(SliderSkinData.Track), "Track", "溝(背景の帯)", s => ((SliderSkinData)s).Track),
            Part(nameof(SliderSkinData.Fill), "Fill", "値まで満たした部分", s => ((SliderSkinData)s).Fill),
            Part(nameof(SliderSkinData.Handle), "Handle", "つまみ", s => ((SliderSkinData)s).Handle),
            Part(nameof(SliderSkinData.DelayFill), "Delay Fill", "遅れて追いかける 2 本目のバー(HP バーの白い残り等)", s => ((SliderSkinData)s).DelayFill),
        };

        private static SkinPart State(string field, string label, string meaning, ControlState state, Func<ControlSkinData, StateVisual> get) =>
            new SkinPart { Field = field, Label = label, Meaning = meaning, Kind = 0, State = state, Get = get };

        private static SkinPart Part(string field, string label, string meaning, Func<ControlSkinData, StateVisual> get) =>
            new SkinPart { Field = field, Label = label, Meaning = meaning, Kind = 1, Get = get };

        // 一覧の絞り込み(検索)に使う語。
        public static string SearchText(SkinPart part) => part.Label + " " + part.Meaning + " " + (part.IsState ? "状態 state" : "パーツ part");

        private static string Hex(Color c)
        {
            var rgb = ColorUtility.ToHtmlStringRGB(c);
            return c.a >= 0.999f ? "#" + rgb : "#" + rgb + " α" + c.a.ToString("0.##");
        }

        // 1 行の要約: 「#FFFFFF · ×1.10 · 画像 Btn_On · コマ 4 枚 @12fps · スクロール (0.1, 0) · 演出: Pop In」。
        // 既定のままのときは「既定のまま(白・等倍・演出なし)」。パーツは演出が本体に掛かるので演出は出さない。
        public static string Summary(in StateVisual v, bool isPart, UiTweenData tween)
        {
            var items = new List<string>(6);
            var plain = true;
            if (isPart)
            {
                if (v.Tint.a > 0f)
                {
                    items.Add("色 " + Hex(v.Tint));
                    plain = false;
                }

                if (v.Scale.Mode != ValueMode.Constant || v.Scale.Constant > 0f)
                {
                    items.Add("拡大 ×" + ScaleText(v.Scale));
                    plain = false;
                }
            }
            else
            {
                var white = Mathf.Approximately(v.Tint.r, 1f) && Mathf.Approximately(v.Tint.g, 1f) && Mathf.Approximately(v.Tint.b, 1f) && v.Tint.a >= 0.999f;
                items.Add(v.Tint.a <= 0f ? "色 透明(何も描かれません)" : "色 " + (white ? "白" : Hex(v.Tint)));
                if (!white)
                {
                    plain = false;
                }

                if (v.Scale.Mode != ValueMode.Constant || !Mathf.Approximately(v.Scale.Constant, 1f))
                {
                    items.Add("拡大 ×" + ScaleText(v.Scale));
                    plain = false;
                }
            }

            if (v.OverrideSprite != null)
            {
                items.Add("画像 " + v.OverrideSprite.name);
                plain = false;
            }

            if (v.AnimFrames != null && v.AnimFrames.Length > 0)
            {
                items.Add($"コマ {v.AnimFrames.Length} 枚 @{(v.AnimFps > 0f ? v.AnimFps : 12f):0.#}fps" + (v.AnimLoop ? string.Empty : " 1 回"));
                plain = false;
            }

            if (v.ScrollSpeed != Vector2.zero)
            {
                items.Add($"スクロール ({v.ScrollSpeed.x:0.##}, {v.ScrollSpeed.y:0.##})");
                plain = false;
            }

            if (!isPart)
            {
                if (v.EnterTween.IsValid)
                {
                    items.Add(tween != null ? "演出 Tween: " + (string.IsNullOrEmpty(tween.DisplayName) ? tween.name : tween.DisplayName) : "演出 Tween: 見つかりません");
                    plain = false;
                }
                else if (v.EnterPreset.Preset != UiPreset.None)
                {
                    items.Add("演出 Preset: " + v.EnterPreset.Preset);
                    plain = false;
                }
                else
                {
                    items.Add("演出なし");
                }
            }

            if (plain)
            {
                return isPart ? "既定のまま(変えない)" : "既定のまま(白・等倍・演出なし)";
            }

            return string.Join(" · ", items);
        }

        private static string ScaleText(ValueDef scale) =>
            scale.Mode == ValueMode.Constant ? scale.Constant.ToString("0.##") : "カーブ";
    }

    // 窓(ButtonSkinEditor / SliderSkinEditor)が一覧・SE の行に頼むこと。確認する(ControlSkinPreviewSection)への橋渡し。
    internal interface ISkinStateHost
    {
        ControlSkinData Skin { get; }

        SerializedObject So { get; }

        SerializedObject DefaultSo { get; }

        // 行・欄を書き換えたあとの更新(検証・変更件数)。
        void Changed();

        void PlayState(ControlState state);

        void OpenTween(ControlState state);

        VisualElement AnimImport(SerializedProperty stateProp);

        string StatusText { get; }

        bool CanAudition(string field);

        bool IsAuditioning(string field);

        void Audition(string field);

        void StopAudition();
    }

    // Tint の色見本(Painter2D。透明は市松で見せる)。
    internal sealed class SkinSwatch : VisualElement
    {
        private Color _color = Color.white;

        public SkinSwatch()
        {
            AddToClassList("ux-sk-swatch");
            generateVisualContent += Draw;
            pickingMode = PickingMode.Ignore;
        }

        public void SetColor(Color color)
        {
            if (_color != color)
            {
                _color = color;
                MarkDirtyRepaint();
            }
        }

        private void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 2f || r.height < 2f)
            {
                return;
            }

            var p = mgc.painter2D;
            const float cell = 5f;
            // 市松(下地)。色が不透明でも全面を覆うだけなので問題ない。
            for (var y = 0; y * cell < r.height; y++)
            {
                for (var x = 0; x * cell < r.width; x++)
                {
                    p.fillColor = (x + y) % 2 == 0 ? new Color(0.78f, 0.78f, 0.8f) : new Color(0.5f, 0.5f, 0.54f);
                    p.BeginPath();
                    p.MoveTo(new Vector2(x * cell, y * cell));
                    p.LineTo(new Vector2(Mathf.Min((x + 1) * cell, r.width), y * cell));
                    p.LineTo(new Vector2(Mathf.Min((x + 1) * cell, r.width), Mathf.Min((y + 1) * cell, r.height)));
                    p.LineTo(new Vector2(x * cell, Mathf.Min((y + 1) * cell, r.height)));
                    p.ClosePath();
                    p.Fill();
                }
            }

            p.fillColor = _color;
            p.BeginPath();
            p.MoveTo(Vector2.zero);
            p.LineTo(new Vector2(r.width, 0f));
            p.LineTo(new Vector2(r.width, r.height));
            p.LineTo(new Vector2(0f, r.height));
            p.ClosePath();
            p.Fill();
        }
    }

    // 状態・パーツの一覧(docs/1010 D-10: 1 状態 = 1 行・絞り込みと件数・行の展開はその行だけ)。
    // CanvasItemList(絞り込み・件数)に 1 行ずつ SkinStateRow を載せる。毎 150ms の Sync は値の署名(contentHash)の比較だけで、
    // 変わった行の表示だけ更新する(行を作り直さない = 入力中のフォーカスを奪わない)。
    internal sealed class SkinStateList : VisualElement, IUxSyncable
    {
        private readonly ISkinStateHost _host;
        private readonly SkinPart[] _parts;
        private readonly CanvasItemList _list;
        private readonly Label _status;
        private readonly HashSet<string> _open = new HashSet<string>();
        private readonly List<SkinStateRow> _rows = new List<SkinStateRow>();
        private int _signature = int.MinValue;

        public SkinStateList(ISkinStateHost host, SkinPart[] parts, string[] kinds)
        {
            _host = host;
            _parts = parts;
            AddToClassList("ux-sk-list");

            _list = new CanvasItemList(kinds != null && kinds.Length > 1 ? "状態・パーツ" : "状態", "設定済み", kinds, "状態がありません。", "既定のまま");
            Add(_list);

            _status = new Label();
            _status.AddToClassList("ux-dim");
            _status.AddToClassList("ux-note");
            Add(_status);
            Rebuild();
        }

        public CanvasItemList List => _list;

        public IReadOnlyList<SkinStateRow> Rows => _rows;

        public bool IsOpen(string field) => _open.Contains(field);

        internal void SetOpen(string field, bool open)
        {
            if (open)
            {
                _open.Add(field);
            }
            else
            {
                _open.Remove(field);
            }
        }

        public void SetExternalQuery(string query) => _list.SetExternalQuery(query);

        public bool AnyMatch(string query) => _list.AnyMatch(query);

        public void Rebuild()
        {
            _rows.Clear();
            var items = new List<CanvasListItem>();
            for (var i = 0; i < _parts.Length; i++)
            {
                var part = _parts[i];
                items.Add(new CanvasListItem
                {
                    Key = "skin:" + part.Field,
                    Title = part.Label,
                    Kind = part.Kind,
                    IsConfigured = () => IsConfigured(part),
                    Search = SkinParts.SearchText(part),
                    Make = () => MakeRow(part),
                });
            }

            _list.SetItems(items);
            _signature = Signature();
        }

        private VisualElement MakeRow(SkinPart part)
        {
            _rows.RemoveAll(r => r == null || r.Part == part);
            var row = new SkinStateRow(_host, this, part);
            _rows.Add(row);
            return row;
        }

        internal bool IsConfigured(SkinPart part)
        {
            var so = _host.So;
            var def = _host.DefaultSo;
            var prop = so?.FindProperty(part.Field);
            var d = def?.FindProperty(part.Field);
            return prop != null && d != null && !SerializedProperty.DataEquals(prop, d);
        }

        private int Signature()
        {
            var so = _host.So;
            if (so == null)
            {
                return 0;
            }

            unchecked
            {
                var h = 17;
                for (var i = 0; i < _parts.Length; i++)
                {
                    var prop = so.FindProperty(_parts[i].Field);
                    h = h * 31 + (prop != null ? (int)prop.contentHash : 0);
                }

                return h;
            }
        }

        public void Sync()
        {
            var text = _host.StatusText ?? string.Empty;
            if (_status.text != text)
            {
                _status.text = text;
            }

            var sig = Signature();
            if (sig == _signature)
            {
                return;
            }

            _signature = sig;
            _rows.RemoveAll(r => r == null || r.panel == null);
            for (var i = 0; i < _rows.Count; i++)
            {
                _rows[i].Refresh();
            }

            _list.UpdateCount();
        }
    }

    // 状態 1 つ = 1 行。1 行目: 種類のチップ・名前・色見本・▶(状態だけ)・↺・⚙、2 行目: 要約。⚙ でその行だけ全欄を開く。
    internal sealed class SkinStateRow : VisualElement
    {
        private readonly ISkinStateHost _host;
        private readonly SkinStateList _list;
        private readonly SkinPart _part;
        private readonly Label _name;
        private readonly Label _summary;
        private readonly SkinSwatch _swatch;
        private readonly Button _reset;
        private readonly Button _gear;
        private readonly VisualElement _details;
        private Button _tweenButton;
        private bool _built;

        public SkinPart Part => _part;

        public bool IsOpen => _details.IsShown();

        public string SummaryText => _summary.text;

        public SkinStateRow(ISkinStateHost host, SkinStateList list, SkinPart part)
        {
            _host = host;
            _list = list;
            _part = part;
            AddToClassList("ux-fxrow");
            AddToClassList("ux-sk-row");

            var head = new VisualElement();
            head.AddToClassList("ux-fxrow__head");
            head.AddToClassList("ux-trk__head");
            head.AddToClassList("ux-sk-head");

            var chip = new Label(SkinParts.KindNames[part.Kind]);
            chip.AddToClassList("ux-chip");
            chip.AddToClassList("ux-fxrow__chip");
            chip.AddToClassList("ux-sk-chip");
            chip.AddToClassList("ux-kind--" + part.Kind);
            head.Add(chip);

            _name = new Label(part.Label) { tooltip = part.Meaning + "(クリックで ⚙ を開く / 閉じる)" };
            _name.AddToClassList("ux-fxrow__name");
            _name.AddToClassList("ux-sk-name");
            _name.RegisterCallback<ClickEvent>(e => Toggle());
            head.Add(_name);

            _swatch = new SkinSwatch { tooltip = "色 Tint" };
            head.Add(_swatch);

            var spacer = new VisualElement();
            spacer.AddToClassList("ux-sk-spacer");
            head.Add(spacer);

            if (part.IsState)
            {
                var play = MakeIcon("▶", "プレビューの部品をこの状態にして、状態に入ったときの演出を再生する(未配置なら確認用シーンに配置)", () => host.PlayState(part.State));
                head.Add(play);
            }

            _reset = MakeIcon("↺", "この " + (part.IsState ? "状態" : "パーツ") + "を既定に戻す(Undo で戻せます)", ResetToDefault);
            head.Add(_reset);
            _gear = MakeIcon("⚙", "すべての欄(色・拡大率・画像・コマ・スクロール" + (part.IsState ? "・演出" : string.Empty) + ")を開く / 閉じる", Toggle);
            head.Add(_gear);
            Add(head);

            _summary = new Label();
            _summary.AddToClassList("ux-dim");
            _summary.AddToClassList("ux-sk-sum");
            Add(_summary);

            _details = new VisualElement();
            _details.AddToClassList("ux-fxrow__details");
            Add(_details);

            Refresh();
            SetOpen(list.IsOpen(part.Field), false);
        }

        private static Button MakeIcon(string text, string tooltip, Action click)
        {
            var b = new Button(click) { text = text, tooltip = tooltip };
            b.AddToClassList("ux-btn");
            b.AddToClassList("ux-btn--icon");
            return b;
        }

        private StateVisual Current()
        {
            var skin = _host.Skin;
            return skin != null ? _part.Get(skin) : default;
        }

        public void Refresh()
        {
            var skin = _host.Skin;
            if (skin == null)
            {
                return;
            }

            var v = _part.Get(skin);
            var tween = !_part.IsState || !v.EnterTween.IsValid ? null : DataIdLookup.Find<UiTweenData>(v.EnterTween.Value);
            var text = SkinParts.Summary(in v, !_part.IsState, tween);
            if (_summary.text != text)
            {
                _summary.text = text;
                _summary.tooltip = text;
            }

            _swatch.SetColor(v.Tint);
            var modified = _list.IsConfigured(_part);
            EnableInClassList("ux-sk-row--mod", modified);
            _reset.SetEnabled(modified);
            _tweenButton?.SetEnabled(tween != null);
        }

        private void Toggle() => SetOpen(!IsOpen, true);

        public void SetOpen(bool open, bool notify)
        {
            _details.Show(open);
            _gear.EnableInClassList("ux-btn--on", open);
            EnableInClassList("ux-fxrow--open", open);
            if (open && !_built)
            {
                BuildDetails();
            }

            if (notify)
            {
                _list.SetOpen(_part.Field, open);
            }
        }

        private void ResetToDefault()
        {
            var so = _host.So;
            var def = _host.DefaultSo;
            var target = so?.targetObject;
            var d = def?.FindProperty(_part.Field);
            if (target == null || d == null)
            {
                return;
            }

            Undo.RecordObject(target, "状態を既定に戻す");
            so.Update();
            so.CopyFromSerializedProperty(d);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            _host.Changed();
            Refresh();
        }

        // ⚙ の中身: StateVisual の全欄(演出はパーツには出さない = 状態ごとの演出は部品本体に掛かる)。
        private void BuildDetails()
        {
            _built = true;
            _details.Clear();
            var so = _host.So;
            var prop = so?.FindProperty(_part.Field);
            if (prop == null)
            {
                return;
            }

            var child = prop.Copy();
            var end = prop.GetEndProperty();
            if (child.NextVisible(true))
            {
                do
                {
                    if (SerializedProperty.EqualContents(child, end))
                    {
                        break;
                    }

                    var name = child.name;
                    var isEnter = name == nameof(StateVisual.EnterTween) || name == nameof(StateVisual.EnterPreset);
                    if (isEnter && !_part.IsState)
                    {
                        continue;
                    }

                    var cp = child.Copy();
                    if (cp.propertyType == SerializedPropertyType.Generic && !cp.isArray)
                    {
                        cp.isExpanded = false; // 簡易演出・拡大率は閉じておく(必要なときだけ開く)
                    }

                    var pf = new PropertyField(cp, FieldLabel(name));
                    pf.AddToClassList("ux-ut-field");
                    pf.BindProperty(cp);
                    if (name == nameof(StateVisual.EnterTween))
                    {
                        var line = new VisualElement();
                        line.AddToClassList("ux-sk-tweenline");
                        pf.AddToClassList("ux-sk-tweenfield");
                        line.Add(pf);
                        _tweenButton = new Button(() => _host.OpenTween(_part.State))
                        {
                            text = "✎ Tween Editor",
                            tooltip = "この UiTweenData を UI Tween Editor で開く(プレビューの部品を対象にする)",
                        };
                        _tweenButton.AddToClassList("ux-btn");
                        line.Add(_tweenButton);
                        _details.Add(line);
                    }
                    else
                    {
                        _details.Add(pf);
                    }
                }
                while (child.NextVisible(false));
            }

            if (_part.IsState)
            {
                var import = _host.AnimImport(prop.Copy());
                if (import != null)
                {
                    import.AddToClassList("ux-sk-import");
                    _details.Add(import);
                }
            }

            Refresh();
        }

        private static string FieldLabel(string name)
        {
            switch (name)
            {
                case nameof(StateVisual.EnterTween): return "演出 Tween";
                case nameof(StateVisual.EnterPreset): return "簡易演出";
                case nameof(StateVisual.Tint): return "色 Tint";
                case nameof(StateVisual.Scale): return "拡大率 Scale";
                case nameof(StateVisual.OverrideSprite): return "画像";
                case nameof(StateVisual.AnimFrames): return "コマ画像";
                case nameof(StateVisual.AnimFps): return "コマ数 / 秒";
                case nameof(StateVisual.AnimLoop): return "コマのループ";
                case nameof(StateVisual.ScrollSpeed): return "スクロール速度";
                default: return ObjectNames.NicifyVariableName(name);
            }
        }
    }

    // SE / 触覚の欄に「▶ 試聴 ■」を横に付けた行(確認する の ControlSkinPreviewSection で実 AudioManager から鳴らす)。
    internal sealed class SkinSeRow : UxRow
    {
        private readonly ISkinStateHost _host;
        private readonly string _field;
        private readonly Button _play;
        private readonly Button _stop;

        private SkinSeRow(ISkinStateHost host, FieldGuideEntry e, SerializedObject so, SerializedObject def, VisualElement input, Button play, Button stop, Action changed)
            : base(so, def, new[] { e }, e.Label, e.Hint, e.Tier, false, input, changed)
        {
            _host = host;
            _field = e.Field;
            _play = play;
            _stop = stop;
            Sync();
        }

        public static SkinSeRow Create(ISkinStateHost host, FieldGuideEntry e, Action changed)
        {
            var so = host.So;
            var box = new VisualElement();
            box.AddToClassList("ux-sk-se");
            var field = GuidedField.MakeInput(so, e.Field);
            field.AddToClassList("ux-sk-se__field");
            box.Add(field);
            var play = new Button(() => host.Audition(e.Field)) { text = "▶", tooltip = "試聴(実際の AudioManager で鳴らす)" };
            play.AddToClassList("ux-btn");
            play.AddToClassList("ux-btn--icon");
            box.Add(play);
            var stop = new Button(host.StopAudition) { text = "■", tooltip = "試聴を止める" };
            stop.AddToClassList("ux-btn");
            stop.AddToClassList("ux-btn--icon");
            box.Add(stop);
            return new SkinSeRow(host, e, so, host.DefaultSo, box, play, stop, changed);
        }

        public override void Sync()
        {
            _play?.SetEnabled(_host.CanAudition(_field));
            _stop?.SetEnabled(_host.IsAuditioning(_field));
        }
    }

    // スライダーの雛形 5 種のボタン列(確認用スライダーに値の範囲・刻み・応答・追従を当てる。SliderPresets。Undo は 1 回)。
    internal sealed class SkinPresetsRow : VisualElement
    {
        private readonly Label _note;
        private readonly string _defaultNote;

        // forSkin = true: Slider Skin Editor(確認用スライダーに当てる。Skin には保存されない)/ false: Slider Editor(対象のスライダーを書き換える)。
        public SkinPresetsRow(Action<SliderPresets.SliderPreset> apply, bool forSkin = true)
        {
            AddToClassList("ux-fxp");
            var line = new VisualElement();
            line.AddToClassList("ux-fxp__line");
            Add(line);

            var subject = forSkin ? "確認用スライダー" : "対象のスライダー";
            _defaultNote = forSkin
                ? "ボタンにマウスを乗せると、その雛形の中身が出ます。当てるのは確認用のスライダーで、Skin には保存されません"
                : "ボタンにマウスを乗せると、その雛形の中身が出ます。対象のスライダーの設定を書き換えます(Ctrl+Z で戻せます)";
            _note = new Label(_defaultNote);
            _note.AddToClassList("ux-dim");
            _note.AddToClassList("ux-note");

            foreach (SliderPresets.SliderPreset preset in Enum.GetValues(typeof(SliderPresets.SliderPreset)))
            {
                if (preset == SliderPresets.SliderPreset.None)
                {
                    continue;
                }

                var captured = preset;
                var description = Describe(preset);
                var b = new Button(() =>
                {
                    apply(captured);
                    _note.text = $"{captured} を{subject}に当てました: {description}";
                })
                {
                    text = preset.ToString(),
                    tooltip = description,
                };
                b.AddToClassList("ux-btn");
                b.AddToClassList("ux-fxp__btn");
                line.Add(b);
            }

            Add(_note);
        }

        public string NoteText => _note.text;

        public static string Describe(SliderPresets.SliderPreset preset)
        {
            switch (preset)
            {
                case SliderPresets.SliderPreset.音量: return "0〜1・連続・応答 InQuad(下側を細かく)・追従なし";
                case SliderPresets.SliderPreset.感度: return "0.1〜5・連続・応答 OutQuad";
                case SliderPresets.SliderPreset.HPバー: return "0〜100・刻み 1・追従 OutCubic 0.25 秒・入力なし(表示専用)";
                case SliderPresets.SliderPreset.スタミナ: return "0〜100・刻み 1・目盛り 4(吸着 0.03)";
                case SliderPresets.SliderPreset.キャラメイク: return "0〜10 の整数・目盛り 10";
                default: return string.Empty;
            }
        }
    }
}
