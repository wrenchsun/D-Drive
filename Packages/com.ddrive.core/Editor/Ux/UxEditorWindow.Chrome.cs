using System;
using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Foundation.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ux
{
    // 窓の外枠(テーマ・ヒーロー・検索・固定バー・空状態・カード)。
    internal abstract partial class UxEditorWindow<TData>
    {
        private const string SheetName = "DDriveUx";
        private static readonly string[] ModeNames = { "かんたん", "標準", "詳細" };

        private static StyleSheet _sheet;

        private readonly List<Button> _modeButtons = new List<Button>();

        private Label _title;
        private Label _idLabel;
        private Label _chipCategory;
        private Label _chipStatus;
        private Label _chipMod;
        private Label _hit;
        private Button _btnPlay;
        private Button _btnStop;
        private Button _btnSave;
        private Button _btnLock;
        private Button _btnResetAll;
        private Label _barMod;
        private Toggle _repeatToggle;
        private Toggle _autoToggle;
        private Slider _speedSlider;
        private Label _speedValue;
        private DataSwitcher _switcher;
        private bool _hideUntouched = true;

        // ── テーマ ──

        private static StyleSheet LoadSheet()
        {
            if (_sheet != null)
            {
                return _sheet;
            }

            var guids = AssetDatabase.FindAssets(SheetName + " t:StyleSheet");
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.EndsWith(SheetName + ".uss", StringComparison.Ordinal))
                {
                    _sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                    return _sheet;
                }
            }

            Debug.LogWarning("[D-Drive] Editor/Ux/Theme/" + SheetName + ".uss が見つかりません。");
            return null;
        }

        private void ApplyThemeSheet(VisualElement root)
        {
            var sheet = LoadSheet();
            if (sheet != null)
            {
                root.styleSheets.Add(sheet);
            }

            root.AddToClassList("ux-root");
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            if (_root == null)
            {
                return;
            }

            _lastPro = EditorGUIUtility.isProSkin;
            _root.EnableInClassList("ux-dark", _lastPro);
            _root.EnableInClassList("ux-light", !_lastPro);
        }

        // ── 本体 ──

        protected void RebuildBody()
        {
            if (_body == null)
            {
                return;
            }

            _body.Clear();
            BuildBody(_body);
        }

        private void BuildBody(VisualElement body)
        {
            OnBuildBodyStart();
            _cards.Clear();
            _rows.Clear();
            _changeRows.Clear();
            _rowByField.Clear();
            _modeButtons.Clear();
            _extraCard = null;
            _extraKey = string.Empty;
            _title = _idLabel = _chipCategory = _chipStatus = _chipMod = _hit = null;
            _btnLock = _btnResetAll = null;
            _switcher = null;
            _hideUntouched = EditorPrefs.GetBool(PrefKey("HideUntouched"), true);
            _footer?.Show(Target != null);

            body.AddToClassList("ux-scroll");
            var page = new VisualElement();
            page.AddToClassList("ux-page");
            body.Add(page);

            if (Target == null || So == null)
            {
                page.Add(BuildEmpty());
                return;
            }

            _mode = Mathf.Clamp(_mode, 0, 2);
            page.Add(BuildHero());
            AfterHero(page);
            BuildCards(page);
            RunValidation();
            RefreshAll(false);
            Reclassify();
            ApplyFilters();
            DataSwitcher.Record(typeof(TData), Target);
        }

        // ── 空状態 ──

        private VisualElement BuildEmpty()
        {
            var wrap = new VisualElement();
            wrap.AddToClassList("ux-empty");

            var zone = new VisualElement();
            zone.AddToClassList("ux-empty__zone");
            var border = new VisualElement { pickingMode = PickingMode.Ignore };
            border.AddToClassList("ux-empty__border");
            var pal = UxPalette.Attach(border);
            border.generateVisualContent += mgc => DrawDropBorder(mgc, border, pal);
            border.RegisterCallback<GeometryChangedEvent>(e => border.MarkDirtyRepaint());
            zone.Add(border);

            var icon = new VisualElement();
            icon.AddToClassList("ux-empty__icon");
            var img = new Image { image = DefaultIcon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            img.AddToClassList("ux-empty__img");
            icon.Add(img);
            zone.Add(icon);

            var head = new Label(DataKindName + " を選んでください");
            head.AddToClassList("ux-empty__headline");
            zone.Add(head);
            var sub = new Label("Project ウィンドウで " + DataKindName + " を選ぶか、ここにドロップ");
            sub.AddToClassList("ux-dim");
            sub.AddToClassList("ux-empty__sub");
            zone.Add(sub);

            var picker = new ObjectField { objectType = typeof(TData) };
            picker.AddToClassList("ux-empty__picker");
            picker.RegisterValueChangedCallback(e =>
            {
                if (e.newValue is TData d)
                {
                    SetTarget(d);
                }
            });
            zone.Add(picker);

            if (HasSample)
            {
                var sample = new Button(() =>
                {
                    var d = CreateSample();
                    if (d != null)
                    {
                        SetTarget(d);
                    }
                }) { text = SampleLabel, tooltip = "サンプルの Data を作って選びます(既にあればそれを選びます)" };
                sample.AddToClassList("ux-btn");
                sample.AddToClassList("ux-empty__sample");
                zone.Add(sample);
            }

            var recent = new VisualElement();
            recent.AddToClassList("ux-empty__recent");
            var guids = AssetDatabase.FindAssets("t:" + DataKindName);
            var shown = 0;
            for (var i = 0; i < guids.Length && shown < 5; i++)
            {
                var d = AssetDatabase.LoadAssetAtPath<TData>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (d == null)
                {
                    continue;
                }

                var data = d;
                var b = new Button(() => SetTarget(data)) { text = d.name, tooltip = AssetDatabase.GetAssetPath(d) };
                b.AddToClassList("ux-btn");
                recent.Add(b);
                shown++;
            }

            if (shown > 0)
            {
                zone.Add(recent);
            }

            zone.RegisterCallback<DragEnterEvent>(e => zone.EnableInClassList("ux-empty__zone--hover", FindDragged() != null));
            zone.RegisterCallback<DragLeaveEvent>(e => zone.RemoveFromClassList("ux-empty__zone--hover"));
            zone.RegisterCallback<DragExitedEvent>(e => zone.RemoveFromClassList("ux-empty__zone--hover"));
            zone.RegisterCallback<DragUpdatedEvent>(e =>
            {
                DragAndDrop.visualMode = FindDragged() != null ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
                e.StopPropagation();
            });
            zone.RegisterCallback<DragPerformEvent>(e =>
            {
                var d = FindDragged();
                if (d == null)
                {
                    return;
                }

                DragAndDrop.AcceptDrag();
                zone.RemoveFromClassList("ux-empty__zone--hover");
                SetTarget(d);
                e.StopPropagation();
            });

            wrap.Add(zone);
            return wrap;
        }

        private static TData FindDragged()
        {
            var objs = DragAndDrop.objectReferences;
            for (var i = 0; i < objs.Length; i++)
            {
                if (objs[i] is TData d)
                {
                    return d;
                }
            }

            return null;
        }

        private static void DrawDropBorder(MeshGenerationContext mgc, VisualElement zone, UxPalette pal)
        {
            var r = zone.contentRect;
            if (r.width < 40f || r.height < 40f)
            {
                return;
            }

            pal.Read();
            var p = mgc.painter2D;
            const float inset = 1f;
            const float rad = 12f;
            p.strokeColor = pal.Drop;
            p.lineWidth = 1.5f;
            var l = inset;
            var t = inset;
            var rt = r.width - inset;
            var b = r.height - inset;
            UxPalette.DashedLine(p, new Vector2(l + rad, t), new Vector2(rt - rad, t), 6f, 5f);
            UxPalette.DashedLine(p, new Vector2(l + rad, b), new Vector2(rt - rad, b), 6f, 5f);
            UxPalette.DashedLine(p, new Vector2(l, t + rad), new Vector2(l, b - rad), 6f, 5f);
            UxPalette.DashedLine(p, new Vector2(rt, t + rad), new Vector2(rt, b - rad), 6f, 5f);
            p.BeginPath();
            p.MoveTo(new Vector2(l, t + rad));
            p.ArcTo(new Vector2(l, t), new Vector2(l + rad, t), rad);
            p.MoveTo(new Vector2(rt - rad, t));
            p.ArcTo(new Vector2(rt, t), new Vector2(rt, t + rad), rad);
            p.MoveTo(new Vector2(rt, b - rad));
            p.ArcTo(new Vector2(rt, b), new Vector2(rt - rad, b), rad);
            p.MoveTo(new Vector2(l + rad, b));
            p.ArcTo(new Vector2(l, b), new Vector2(l, b - rad), rad);
            p.Stroke();
        }

        // ── ヒーロー ──

        private VisualElement BuildHero()
        {
            var hero = new VisualElement();
            hero.AddToClassList("ux-hero");

            var top = new VisualElement();
            top.AddToClassList("ux-hero__top");

            var iconBox = new VisualElement { tooltip = "Project ウィンドウで表示" };
            iconBox.AddToClassList("ux-hero__icon");
            var img = new Image { image = Target.Icon != null ? Target.Icon : DefaultIcon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            img.AddToClassList("ux-hero__img");
            iconBox.Add(img);
            iconBox.RegisterCallback<ClickEvent>(e => EditorGUIUtility.PingObject(Target));
            top.Add(iconBox);

            var info = new VisualElement();
            info.AddToClassList("ux-hero__info");
            _switcher = new DataSwitcher(typeof(TData), () => Target, d => SetTarget(d as TData));
            info.Add(_switcher);
            _title = new Label();
            _title.AddToClassList("ux-hero__title");
            info.Add(_title);
            _idLabel = new Label();
            _idLabel.AddToClassList("ux-dim");
            _idLabel.AddToClassList("ux-hero__id");
            info.Add(_idLabel);
            var chips = new VisualElement();
            chips.AddToClassList("ux-hero__chips");
            _chipCategory = MakeChip(chips, null);
            _chipStatus = MakeChip(chips, "ux-chip--ok");
            _chipMod = MakeChip(chips, "ux-chip--mod");
            _chipMod.AddToClassList("ux-chip--clickable");
            _chipMod.RegisterCallback<ClickEvent>(e =>
            {
                _modifiedOnly = !_modifiedOnly;
                ApplyFilters();
                UpdateChips();
            });
            info.Add(chips);
            top.Add(info);

            top.Add(BuildSearch());
            hero.Add(top);

            var actions = new VisualElement();
            actions.AddToClassList("ux-hero__actions");
            AddLeadingHeroActions(actions);
            actions.Add(MakeButton("Project", null, PingTarget, "この Data を Project ウィンドウで表示する"));
            actions.Add(MakeButton("＋ 新規", null, NewAsset, "命名規則どおりの新しい " + DataKindName + " を作り、このエディタで開く"));
            var spacer = new VisualElement();
            spacer.AddToClassList("ux-hero__spacer");
            actions.Add(spacer);
            _btnLock = MakeButton("🔒 固定", null, ToggleLock, "ON: Project ウィンドウの選択に追従しない");
            _btnLock.EnableInClassList("ux-btn--on", Locked);
            actions.Add(_btnLock);
            hero.Add(actions);

            var modes = new VisualElement();
            modes.AddToClassList("ux-hero__modes");
            var seg = new VisualElement();
            seg.AddToClassList("ux-seg");
            var tips = ModeTips;
            for (var i = 0; i < ModeNames.Length; i++)
            {
                var m = i;
                var b = new Button(() => SetMode(m)) { text = ModeNames[i], tooltip = tips[i] };
                b.AddToClassList("ux-seg__btn");
                _modeButtons.Add(b);
                seg.Add(b);
            }

            modes.Add(seg);
            var hide = new Toggle("未調整を隠す") { value = _hideUntouched, tooltip = "詳細モードで、既定値のままの設定をカードごとに「未調整の設定」へ折りたたむ" };
            hide.AddToClassList("ux-hidetoggle");
            hide.RegisterValueChangedCallback(e =>
            {
                _hideUntouched = e.newValue;
                EditorPrefs.SetBool(PrefKey("HideUntouched"), e.newValue);
                Reclassify();
                ApplyFilters();
            });
            modes.Add(hide);
            _btnResetAll = new Button(ResetAll) { text = "↺ すべて既定に戻す", tooltip = "既定値と違う設定をすべて既定に戻す(確認あり。管理情報は対象外)" };
            _btnResetAll.AddToClassList("ux-btn");
            _btnResetAll.AddToClassList("ux-btn--ghost");
            modes.Add(_btnResetAll);
            hero.Add(modes);

            _hit = new Label();
            _hit.AddToClassList("ux-dim");
            _hit.AddToClassList("ux-hit");
            hero.Add(_hit);
            return hero;
        }

        private static Label MakeChip(VisualElement parent, string cls)
        {
            var l = new Label();
            l.AddToClassList("ux-chip");
            if (cls != null)
            {
                l.AddToClassList(cls);
            }

            parent.Add(l);
            return l;
        }

        protected static Button MakeButton(string text, string cls, Action click, string tooltip)
        {
            var b = new Button(click) { text = text, tooltip = tooltip };
            b.AddToClassList("ux-btn");
            if (cls != null)
            {
                b.AddToClassList(cls);
            }

            return b;
        }

        private VisualElement BuildSearch()
        {
            var box = new VisualElement { tooltip = SearchHint };
            box.AddToClassList("ux-searchbox");
            var icon = new Image { image = EditorGUIUtility.IconContent("Search Icon").image, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            icon.AddToClassList("ux-searchbox__icon");
            box.Add(icon);
            var field = new TextField { value = _query };
            field.AddToClassList("ux-searchbox__field");
            field.textEdition.placeholder = "設定を検索";
            field.textEdition.hidePlaceholderOnFocus = true;
            box.Add(field);
            field.RegisterCallback<FocusInEvent>(e => box.AddToClassList("ux-searchbox--focus"));
            field.RegisterCallback<FocusOutEvent>(e => box.RemoveFromClassList("ux-searchbox--focus"));
            var clear = new Button(() => field.value = string.Empty) { text = "×", tooltip = "検索を消す" };
            clear.AddToClassList("ux-searchbox__clear");
            clear.Show(!string.IsNullOrEmpty(_query));
            box.Add(clear);
            field.RegisterValueChangedCallback(e =>
            {
                _query = e.newValue ?? string.Empty;
                clear.Show(!string.IsNullOrEmpty(_query));
                ApplyFilters();
            });
            return box;
        }

        private void PingTarget()
        {
            if (Target != null)
            {
                EditorGUIUtility.PingObject(Target);
            }
        }

        private void NewAsset() =>
            NewAssetDialog.Open(new[] { typeof(TData) }, created =>
            {
                if (created is TData d)
                {
                    SetTarget(d);
                }
            });

        private void ToggleLock()
        {
            Locked = !Locked;
            _btnLock.EnableInClassList("ux-btn--on", Locked);
        }

        private void SetMode(int mode)
        {
            _mode = mode;
            EditorPrefs.SetInt(PrefKey("Mode"), mode);
            Reclassify();
            ApplyFilters();
        }

        // ── 固定アクションバー(ScrollView の外 = ウィンドウ下部に常時表示) ──

        private VisualElement CreateFooter()
        {
            var bar = new VisualElement();
            bar.AddToClassList("ux-bar");
            bar.Show(false);

            if (SupportsPlayback)
            {
                _btnPlay = MakeButton("▶ 再生", "ux-btn--primary", () => PlayMain(true), "いま開いているシーンで再生する(再生中はやり直し)");
                bar.Add(_btnPlay);
                _btnStop = MakeButton("■ 停止", null, StopMain, "再生を止める");
                bar.Add(_btnStop);
                _repeatToggle = new Toggle("リピート") { value = Repeat, tooltip = "終わったら自動で再生し直す(調整中に何度も押さなくてよい)" };
                _repeatToggle.AddToClassList("ux-bar__repeat");
                _repeatToggle.RegisterValueChangedCallback(e => Repeat = e.newValue);
                bar.Add(_repeatToggle);
                if (SupportsSpeed)
                {
                    _speedSlider = new Slider("速度", SpeedMin, SpeedMax) { value = Speed, showInputField = false, tooltip = "再生の速さ(0.1〜2 倍)" };
                    _speedSlider.AddToClassList("ux-bar__speed");
                    _speedSlider.RegisterValueChangedCallback(OnSpeedSliderChanged);
                    bar.Add(_speedSlider);
                    AddSpeedTick(_speedSlider);
                    _speedValue = new Label();
                    _speedValue.AddToClassList("ux-bar__speed-value");
                    _speedValue.tooltip = "クリックで ×1.0 に戻す";
                    _speedValue.RegisterCallback<ClickEvent>(_ => ResetSpeed());
                    bar.Add(_speedValue);
                    RefreshSpeedLabel();
                }
            }

            var spacer = new VisualElement();
            spacer.AddToClassList("ux-bar__spacer");
            bar.Add(spacer);
            _barMod = new Label("変更なし");
            _barMod.AddToClassList("ux-chip");
            _barMod.AddToClassList("ux-bar__mod");
            bar.Add(_barMod);
            _autoToggle = new Toggle("自動保存") { value = AutoSave, tooltip = "ON: 編集が止まって 1 秒たつと自動で保存する(再生中でも)。保存のたびに版数が進む" };
            _autoToggle.AddToClassList("ux-bar__repeat");
            _autoToggle.RegisterValueChangedCallback(e => AutoSave = e.newValue);
            bar.Add(_autoToggle);
            _btnSave = MakeButton("保存", null, SaveTarget, "この Data を保存する(版数が進む)。Ctrl+S(File > Save)でも保存されます");
            bar.Add(_btnSave);
            return bar;
        }

        // 速度スライダー上の ×1.0 の目盛り線(Painter2D。色は USS から読む)。
        private static void AddSpeedTick(Slider slider)
        {
            var ticks = new VisualElement { pickingMode = PickingMode.Ignore };
            ticks.AddToClassList("ux-bar__speed-ticks");
            var pal = UxPalette.Attach(ticks);
            ticks.generateVisualContent += mgc =>
            {
                var r = ticks.contentRect;
                if (r.width < 8f || r.height < 8f)
                {
                    return;
                }

                pal.Read();
                var x = (1f - SpeedMin) / (SpeedMax - SpeedMin) * r.width;
                var p = mgc.painter2D;
                p.strokeColor = pal.Dim;
                p.lineWidth = 1f;
                p.BeginPath();
                p.MoveTo(new Vector2(x, 4f));
                p.LineTo(new Vector2(x, r.height - 4f));
                p.Stroke();
            };
            ticks.RegisterCallback<GeometryChangedEvent>(e => ticks.MarkDirtyRepaint());
            var drag = slider.Q(className: "unity-base-slider__drag-container");
            (drag ?? slider).Add(ticks);
        }

        private void OnSpeedSliderChanged(ChangeEvent<float> e)
        {
            var v = e.newValue;
            if (!Mathf.Approximately(v, 1f) && Mathf.Abs(v - 1f) <= SpeedDetent)
            {
                v = 1f;
                _speedSlider.SetValueWithoutNotify(1f);
            }

            Speed = v;
            RefreshSpeedLabel();
        }

        private void ResetSpeed()
        {
            _speedSlider.SetValueWithoutNotify(1f);
            Speed = 1f;
            RefreshSpeedLabel();
        }

        private void RefreshSpeedLabel()
        {
            if (_speedValue != null)
            {
                _speedValue.text = "×" + Speed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        // 固定バーの状態を実際の状態に合わせる(外部変更・再生の開始と終了・保存)。
        private void SyncFooter()
        {
            if (_btnPlay != null)
            {
                var playing = IsPlaying;
                if (playing != _wasPlaying)
                {
                    _wasPlaying = playing;
                    _btnPlay.text = playing ? "▶ やり直し" : "▶ 再生";
                    _btnStop.EnableInClassList("ux-btn--stop-on", playing);
                }
            }

            if (_repeatToggle != null && _repeatToggle.value != Repeat)
            {
                _repeatToggle.SetValueWithoutNotify(Repeat);
            }

            _switcher?.Refresh();
            var dirty = EditorUtility.IsDirty(Target);
            var saveText = dirty ? "保存 ●" : "保存";
            if (_btnSave != null && _btnSave.text != saveText)
            {
                _btnSave.text = saveText;
                _btnSave.tooltip = dirty
                    ? "未保存の変更があります。Ctrl+S(File > Save)でも保存されます"
                    : "この Data を保存する(版数が進む)。Ctrl+S(File > Save)でも保存されます";
            }

            if (_autoToggle != null && _autoToggle.value != AutoSave)
            {
                _autoToggle.SetValueWithoutNotify(AutoSave);
            }

            if (_speedSlider != null && !Mathf.Approximately(_speedSlider.value, Speed))
            {
                _speedSlider.SetValueWithoutNotify(Speed);
                RefreshSpeedLabel();
            }
        }
    }
}
