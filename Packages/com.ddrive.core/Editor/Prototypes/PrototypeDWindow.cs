using System;
using System.Collections.Generic;
using System.Text;
using DDrive.Editor.Manual;
using DDrive.Editor.Preview;
using DDrive.Editor.Validation;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.EditorPrototypes
{
    // D. デザイン重視(専用 USS テーマ + ビジュアル部品)(docs/1008 §3-D)。
    // 構造は A の段階表示(かんたん / 標準 / 詳細)+ C の目的別カード + 検索。見た目は Theme/PrototypeD.uss に集め、
    // C# 側はクラス名を付けるだけ(インラインスタイルなし)。長さは DurationBar、位置は AnchorPad で編集する。
    internal sealed class PrototypeDWindow : PrototypeWindowBase
    {
        private const string ManualPage = "vfx-editor";
        private const string SheetName = "PrototypeD";
        private static readonly string[] ModeNames = { "かんたん", "標準", "詳細" };
        private static readonly string[] ModeTips =
        {
            "必須の欄だけ。Prefab を入れて ▶ で確かめるところまで",
            "よく使う欄まで。位置・長さ・調整つまみ",
            "すべての欄",
        };

        [SerializeField] private int _mode = 1;
        [SerializeField] private string _query = string.Empty;
        [SerializeField] private bool _modifiedOnly;

        private readonly List<PdCard> _cards = new List<PdCard>();
        private readonly List<PdRow> _rows = new List<PdRow>();
        private readonly List<PdRow> _changeRows = new List<PdRow>();
        private readonly Dictionary<string, PdRow> _rowByField = new Dictionary<string, PdRow>();
        private readonly List<Button> _modeButtons = new List<Button>();

        private VisualElement _root;
        private bool _lastPro;
        private Label _title;
        private Label _idLabel;
        private Label _chipCategory;
        private Label _chipStatus;
        private Label _chipMod;
        private Label _hit;
        private Button _btnPlay;
        private Button _btnSave;
        private Button _btnLock;
        private Button _btnResetAll;
        private PdCard _extraCard;
        private AnchorPad _anchorPad;
        private PdAnchorRow _anchorRow;
        private int _errors;
        private int _warnings;
        private int _modifiedCount;
        private int _signature;
        private int _tick;
        private string _extraKey = string.Empty;
        private bool _wasPlaying;

        protected override string Aim => "D デザイン重視: 専用テーマとビジュアル部品(長さのバー・位置のパッド)で、見た目と触り心地を作り込んだ案。";
        protected override bool CustomChrome => true;

        public static void Open() => OpenWindow<PrototypeDWindow>("D デザイン重視");

        // ── ルート(テーマ) ──

        protected override void ConfigureRoot(VisualElement root)
        {
            _root = root;
            var sheet = LoadSheet();
            if (sheet != null)
            {
                root.styleSheets.Add(sheet);
            }

            root.AddToClassList("pd-root");
            ApplyTheme();
        }

        private static StyleSheet LoadSheet()
        {
            var guids = AssetDatabase.FindAssets(SheetName + " t:StyleSheet");
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.EndsWith(SheetName + ".uss", StringComparison.Ordinal))
                {
                    return AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                }
            }

            Debug.LogWarning("[Prototype D] Theme/PrototypeD.uss が見つかりません。");
            return null;
        }

        private void ApplyTheme()
        {
            if (_root == null)
            {
                return;
            }

            _lastPro = EditorGUIUtility.isProSkin;
            _root.EnableInClassList("pd-dark", _lastPro);
            _root.EnableInClassList("pd-light", !_lastPro);
        }

        // ── 本体 ──

        protected override void BuildBody(VisualElement body)
        {
            _cards.Clear();
            _rows.Clear();
            _changeRows.Clear();
            _rowByField.Clear();
            _modeButtons.Clear();
            _extraCard = null;
            _extraKey = string.Empty;
            _anchorPad = null;
            _anchorRow = null;
            _title = _idLabel = _chipCategory = _chipStatus = _chipMod = _hit = null;
            _btnPlay = _btnSave = _btnLock = _btnResetAll = null;

            body.AddToClassList("pd-scroll");
            var page = new VisualElement();
            page.AddToClassList("pd-page");
            body.Add(page);
            page.Add(new Label(TrialNotice) { style = { opacity = 0.6f, whiteSpace = WhiteSpace.Normal, marginBottom = 4 } });

            if (Target == null || So == null)
            {
                page.Add(BuildEmpty());
                return;
            }

            _mode = Mathf.Clamp(_mode, 0, 2);
            page.Add(BuildHero());
            BuildCards(page);
            RunValidation();
            RefreshAll(false);
        }

        // ── 空状態 ──

        private VisualElement BuildEmpty()
        {
            var wrap = new VisualElement();
            wrap.AddToClassList("pd-empty");

            var zone = new VisualElement();
            zone.AddToClassList("pd-empty__zone");
            var border = new VisualElement { pickingMode = PickingMode.Ignore };
            border.AddToClassList("pd-empty__border");
            var pal = PdPalette.Attach(border);
            border.generateVisualContent += mgc => DrawDropBorder(mgc, border, pal);
            border.RegisterCallback<GeometryChangedEvent>(e => border.MarkDirtyRepaint());
            zone.Add(border);

            var icon = new VisualElement();
            icon.AddToClassList("pd-empty__icon");
            icon.Add(new Image { image = DefaultIcon(), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore });
            icon[0].AddToClassList("pd-empty__img");
            zone.Add(icon);

            var head = new Label("VfxData を選んでください");
            head.AddToClassList("pd-empty__headline");
            zone.Add(head);
            var sub = new Label("Project ウィンドウで VfxData を選ぶか、ここにドロップ");
            sub.AddToClassList("pd-dim");
            sub.AddToClassList("pd-empty__sub");
            zone.Add(sub);

            var picker = new UnityEditor.UIElements.ObjectField { objectType = typeof(VfxData) };
            picker.AddToClassList("pd-empty__picker");
            picker.RegisterValueChangedCallback(e =>
            {
                if (e.newValue is VfxData d)
                {
                    SetTarget(d);
                }
            });
            zone.Add(picker);
            zone.Add(SampleVfx.CreateButton(SetTarget));

            var recent = new VisualElement();
            recent.AddToClassList("pd-empty__recent");
            var guids = AssetDatabase.FindAssets("t:VfxData");
            var shown = 0;
            for (var i = 0; i < guids.Length && shown < 5; i++)
            {
                var d = AssetDatabase.LoadAssetAtPath<VfxData>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (d == null)
                {
                    continue;
                }

                var data = d;
                var b = new Button(() => SetTarget(data)) { text = d.name, tooltip = AssetDatabase.GetAssetPath(d) };
                b.AddToClassList("pd-btn");
                recent.Add(b);
                shown++;
            }

            if (shown > 0)
            {
                zone.Add(recent);
            }

            zone.RegisterCallback<DragEnterEvent>(e => zone.EnableInClassList("pd-empty__zone--hover", FindDraggedVfx() != null));
            zone.RegisterCallback<DragLeaveEvent>(e => zone.RemoveFromClassList("pd-empty__zone--hover"));
            zone.RegisterCallback<DragExitedEvent>(e => zone.RemoveFromClassList("pd-empty__zone--hover"));
            zone.RegisterCallback<DragUpdatedEvent>(e =>
            {
                DragAndDrop.visualMode = FindDraggedVfx() != null ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
                e.StopPropagation();
            });
            zone.RegisterCallback<DragPerformEvent>(e =>
            {
                var d = FindDraggedVfx();
                if (d == null)
                {
                    return;
                }

                DragAndDrop.AcceptDrag();
                zone.RemoveFromClassList("pd-empty__zone--hover");
                SetTarget(d);
                e.StopPropagation();
            });

            wrap.Add(zone);
            return wrap;
        }

        private static VfxData FindDraggedVfx()
        {
            var objs = DragAndDrop.objectReferences;
            for (var i = 0; i < objs.Length; i++)
            {
                if (objs[i] is VfxData d)
                {
                    return d;
                }
            }

            return null;
        }

        private static void DrawDropBorder(MeshGenerationContext mgc, VisualElement zone, PdPalette pal)
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
            PdPalette.DashedLine(p, new Vector2(l + rad, t), new Vector2(rt - rad, t), 6f, 5f);
            PdPalette.DashedLine(p, new Vector2(l + rad, b), new Vector2(rt - rad, b), 6f, 5f);
            PdPalette.DashedLine(p, new Vector2(l, t + rad), new Vector2(l, b - rad), 6f, 5f);
            PdPalette.DashedLine(p, new Vector2(rt, t + rad), new Vector2(rt, b - rad), 6f, 5f);
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

        private static Texture DefaultIcon()
        {
            var c = EditorGUIUtility.IconContent("ParticleSystem Icon");
            return c != null ? c.image : null;
        }

        // ── ヒーロー ──

        private VisualElement BuildHero()
        {
            var hero = new VisualElement();
            hero.AddToClassList("pd-hero");

            var top = new VisualElement();
            top.AddToClassList("pd-hero__top");

            var iconBox = new VisualElement { tooltip = "Project ウィンドウで表示" };
            iconBox.AddToClassList("pd-hero__icon");
            var img = new Image { image = Target.Icon != null ? Target.Icon : DefaultIcon(), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            img.AddToClassList("pd-hero__img");
            iconBox.Add(img);
            iconBox.RegisterCallback<ClickEvent>(e => EditorGUIUtility.PingObject(Target));
            top.Add(iconBox);

            var info = new VisualElement();
            info.AddToClassList("pd-hero__info");
            _title = new Label();
            _title.AddToClassList("pd-hero__title");
            info.Add(_title);
            _idLabel = new Label();
            _idLabel.AddToClassList("pd-dim");
            _idLabel.AddToClassList("pd-hero__id");
            info.Add(_idLabel);
            var chips = new VisualElement();
            chips.AddToClassList("pd-hero__chips");
            _chipCategory = MakeChip(chips, null);
            _chipStatus = MakeChip(chips, "pd-chip--ok");
            _chipMod = MakeChip(chips, "pd-chip--mod");
            _chipMod.AddToClassList("pd-chip--clickable");
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
            actions.AddToClassList("pd-hero__actions");
            _btnPlay = MakeButton("▶ 再生", "pd-btn--primary", () => PlayMain(true), "確認用シーンではなく、いま開いているシーンで再生する");
            actions.Add(_btnPlay);
            actions.Add(MakeButton("■ 停止", null, StopMain, "再生を止める"));
            var scene = PreviewPlacementButton.Create("確認用シーン", "ライト / カメラ / 床を備えた確認用シーンを開き、対象をそこで再生する", OpenPreviewScene);
            scene.AddToClassList("pd-btn");
            actions.Add(scene);
            _btnSave = MakeButton("保存", null, SaveTarget, "AssetDatabase.SaveAssets");
            actions.Add(_btnSave);
            var spacer = new VisualElement();
            spacer.AddToClassList("pd-hero__spacer");
            actions.Add(spacer);
            _btnLock = MakeButton("🔒 固定", null, ToggleLock, "ON: Project ウィンドウの選択に追従しない");
            _btnLock.EnableInClassList("pd-btn--on", Locked);
            actions.Add(_btnLock);
            hero.Add(actions);

            var modes = new VisualElement();
            modes.AddToClassList("pd-hero__modes");
            var seg = new VisualElement();
            seg.AddToClassList("pd-seg");
            for (var i = 0; i < ModeNames.Length; i++)
            {
                var m = i;
                var b = new Button(() => SetMode(m)) { text = ModeNames[i], tooltip = ModeTips[i] };
                b.AddToClassList("pd-seg__btn");
                _modeButtons.Add(b);
                seg.Add(b);
            }

            modes.Add(seg);
            _btnResetAll = new Button(ResetAll) { text = "↺ すべて既定に戻す", tooltip = "既定値と違う設定をすべて既定に戻す(確認あり。管理情報は対象外)" };
            _btnResetAll.AddToClassList("pd-btn");
            _btnResetAll.AddToClassList("pd-btn--ghost");
            modes.Add(_btnResetAll);
            hero.Add(modes);

            _hit = new Label();
            _hit.AddToClassList("pd-dim");
            _hit.AddToClassList("pd-hit");
            hero.Add(_hit);
            return hero;
        }

        private static Label MakeChip(VisualElement parent, string cls)
        {
            var l = new Label();
            l.AddToClassList("pd-chip");
            if (cls != null)
            {
                l.AddToClassList(cls);
            }

            parent.Add(l);
            return l;
        }

        private static Button MakeButton(string text, string cls, Action click, string tooltip)
        {
            var b = new Button(click) { text = text, tooltip = tooltip };
            b.AddToClassList("pd-btn");
            if (cls != null)
            {
                b.AddToClassList(cls);
            }

            return b;
        }

        private VisualElement BuildSearch()
        {
            var box = new VisualElement { tooltip = "ラベル・説明・検索語に一致する設定だけ出す(例: 高さ / ループ / レイヤー)" };
            box.AddToClassList("pd-searchbox");
            var icon = new Image { image = EditorGUIUtility.IconContent("Search Icon").image, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            icon.AddToClassList("pd-searchbox__icon");
            box.Add(icon);
            var field = new TextField { value = _query };
            field.AddToClassList("pd-searchbox__field");
            field.textEdition.placeholder = "設定を検索";
            field.textEdition.hidePlaceholderOnFocus = true;
            box.Add(field);
            field.RegisterCallback<FocusInEvent>(e => box.AddToClassList("pd-searchbox--focus"));
            field.RegisterCallback<FocusOutEvent>(e => box.RemoveFromClassList("pd-searchbox--focus"));
            var clear = new Button(() => field.value = string.Empty) { text = "×", tooltip = "検索を消す" };
            clear.AddToClassList("pd-searchbox__clear");
            clear.style.display = string.IsNullOrEmpty(_query) ? DisplayStyle.None : DisplayStyle.Flex;
            box.Add(clear);
            field.RegisterValueChangedCallback(e =>
            {
                _query = e.newValue ?? string.Empty;
                clear.style.display = string.IsNullOrEmpty(_query) ? DisplayStyle.None : DisplayStyle.Flex;
                ApplyFilters();
            });
            return box;
        }

        private void ToggleLock()
        {
            Locked = !Locked;
            _btnLock.EnableInClassList("pd-btn--on", Locked);
        }

        private void SaveTarget()
        {
            if (Target == null)
            {
                return;
            }

            EditorUtility.SetDirty(Target);
            DDrive.Editor.Versioning.DDriveAssetSave.SaveAllSuppressed();
        }

        // ── カードと欄 ──

        private static string CardDesc(string purpose)
        {
            switch (purpose)
            {
                case VfxFieldGuide.PurShow: return "どのエフェクトを、どこに描くか";
                case VfxFieldGuide.PurPlace: return "エフェクトが出る位置と高さ";
                case VfxFieldGuide.PurLife: return "どれくらい続いて、どう消えるか";
                case VfxFieldGuide.PurLook: return "色やサイズなど、あとから変えられる調整つまみ";
                case VfxFieldGuide.PurLink: return "節目で別の音や演出を一緒に鳴らす";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        private void BuildCards(VisualElement page)
        {
            foreach (var purpose in Guide.Purposes)
            {
                var card = new PdCard(purpose, CardDesc(purpose), () => ManualLauncher.OpenPage(ManualPage));
                var isAdmin = purpose == VfxFieldGuide.PurAdmin;
                foreach (var entry in Guide.Entries)
                {
                    if (entry.Purpose != purpose)
                    {
                        continue;
                    }

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

            _extraCard = new PdCard("その他の検証", "どの欄にも当てはまらない検査結果", () => ManualLauncher.OpenPage(ManualPage));
            _extraCard.style.display = DisplayStyle.None;
            page.Add(_extraCard);
        }

        private void AddRow(PdCard card, PdRow row, bool isAdmin)
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

        private PdRow CreateRow(FieldGuideEntry e, PdCard card, bool isAdmin)
        {
            var data = Target;
            switch (e.Field)
            {
                case "Duration":
                case "FadeOutSec":
                case "RenderLayer":
                    return null; // 長さ・描画の複合欄に含める

                case nameof(VfxData.LifeMode):
                {
                    var bar = new DurationBar(
                        data,
                        () => new DurationBar.Values { Mode = data.LifeMode, Duration = data.Duration, Fade = data.FadeOutSec },
                        v =>
                        {
                            data.LifeMode = v.Mode;
                            data.Duration = v.Duration;
                            data.FadeOutSec = v.Fade;
                        },
                        final => OnVisualEdited(final, false));
                    var entries = new[] { e, Guide.Find(nameof(VfxData.Duration)), Guide.Find(nameof(VfxData.FadeOutSec)) };
                    return new PdLifeRow(So, DefaultSo, entries, "再生の終わり方・続く秒数・停止後の余韻を、1 本のバーで。", bar, RowChanged);
                }

                case nameof(VfxData.Render):
                {
                    var entries = new[] { e, Guide.Find(nameof(VfxData.RenderLayer)) };
                    return new PdRenderRow(So, DefaultSo, entries, "通常のシーンに出すか、UI の上に重ねて出すか。詳細では表示レイヤーも選べます。", RowChanged);
                }

                case nameof(VfxData.Anchor):
                {
                    var pad = new AnchorPad(
                        data,
                        () => data.Anchor.LocalOffset,
                        v =>
                        {
                            var a = data.Anchor;
                            a.LocalOffset = v;
                            data.Anchor = a;
                        },
                        final => OnVisualEdited(final, true));
                    _anchorPad = pad;
                    _anchorRow = new PdAnchorRow(So, DefaultSo, new[] { e }, "上から見たパッドをドラッグ。高さは右のスライダー。", pad, RowChanged);
                    RefreshAnchorNote();

                    // 同じ欄の全設定(基準・回転・スケール)は詳細でだけ。
                    var full = new PdRow(So, DefaultSo, new[] { e }, "位置の全設定(基準・回転)", e.Hint, FieldTier.Advanced, true,
                        PdRow.MakeInput(So, e.Field), RowChanged) { IsDuplicate = true };
                    card.AddRow(_anchorRow);
                    _rows.Add(_anchorRow);
                    _rowByField[e.Field] = _anchorRow;
                    _changeRows.Add(_anchorRow);
                    return full;
                }

                default:
                {
                    var compound = PdRow.IsCompound(So, e.Field);
                    return new PdRow(So, DefaultSo, new[] { e }, e.Label, e.Hint, e.Tier, compound, PdRow.MakeInput(So, e.Field), RowChanged);
                }
            }
        }

        private VisualElement MakeReadOnlyInfo()
        {
            var box = new VisualElement();
            box.AddToClassList("pd-card__empty");
            box.Add(new Label($"Id {Target.Id}   Version {Target.Version}   最終更新者 {(string.IsNullOrEmpty(Target.Author) ? "-" : Target.Author)}   最終更新日時 {(string.IsNullOrEmpty(Target.UpdatedAt) ? "-" : Target.UpdatedAt)}"));
            return box;
        }

        // ── 編集のあと ──

        private void RowChanged()
        {
            So?.Update();
            ReapplyAnchor();
            RunValidation();
            RefreshAll(false);
        }

        // ビジュアル入力(バー・パッド)からの書き込み。ドラッグ中は検証を省く。
        private void OnVisualEdited(bool final, bool anchor)
        {
            So?.Update();
            if (anchor)
            {
                ReapplyAnchor();
            }

            if (final)
            {
                RunValidation();
            }

            RefreshAll(false);
        }

        protected override void OnDataEdited()
        {
            if (Target == null || So == null || _rows.Count == 0)
            {
                return;
            }

            RunValidation();
            RefreshAll(true);
        }

        protected override void OnTargetChanged()
        {
            _wasPlaying = false;
            _errors = _warnings = 0;
        }

        private void RefreshAll(bool syncVisuals)
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

            RefreshAnchorNote();
            UpdateChips();
            ApplyFilters();
        }

        private void RefreshAnchorNote()
        {
            if (_anchorPad == null || Target == null)
            {
                return;
            }

            if (Target.AnchorId.IsValid)
            {
                _anchorPad.SetDimmed(true);
                _anchorPad.SetNote("Anchor アセットが設定されているため、この位置は使われません(詳細の「共通の出る位置」)。");
            }
            else if (Target.Anchor.Space != AnchorSpace.World)
            {
                _anchorPad.SetDimmed(false);
                _anchorPad.SetNote($"基準が {Target.Anchor.Space} です。位置は基準からの相対(メートル)になります。");
            }
            else
            {
                _anchorPad.SetDimmed(false);
                _anchorPad.SetNote(null);
            }
        }

        // ── 検証(欄の下にインライン表示) ──

        private void RunValidation()
        {
            if (Target == null || _rows.Count == 0)
            {
                return;
            }

            var results = DataValidationRunner.Run(Target);
            _errors = 0;
            _warnings = 0;
            var byRow = new Dictionary<PdRow, List<ValidationResult>>();
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

                var row = FindRowForMessage(r.Message);
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
            }

            var extraKey = PdRow.IssueKey(extra);
            if (_extraCard != null && extraKey != _extraKey)
            {
                _extraKey = extraKey;
                _extraCard.Body.Clear();
                for (var i = 0; i < extra.Count; i++)
                {
                    _extraCard.Body.Add(PdRow.MakeIssue(extra[i], RowChanged));
                }
            }

            if (_extraCard != null)
            {
                _extraCard.style.display = extra.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            UpdateChips();
        }

        private static readonly KeyValuePair<string, string>[] MessageTokens =
        {
            new KeyValuePair<string, string>("Pool", "Flags"),
            new KeyValuePair<string, string>("Domain", "Flags"),
            new KeyValuePair<string, string>("マテリアル", "Prefab"),
            new KeyValuePair<string, string>("シェーダー", "Prefab"),
        };

        // 検査メッセージを欄に結びつける。先頭がフィールド名(例: "Params 'x' の…")ならその欄、次に代表語(マテリアル → Prefab 等)、
        // 最後にメッセージ中で最初に出てくるフィールド名(同位置なら長い名前を優先)。
        private PdRow FindRowForMessage(string msg)
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

            if (best == null)
            {
                for (var i = 0; i < MessageTokens.Length; i++)
                {
                    if (msg.IndexOf(MessageTokens[i].Key, StringComparison.Ordinal) >= 0)
                    {
                        best = MessageTokens[i].Value;
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

        // ── モード・検索・絞り込み ──

        private void SetMode(int mode)
        {
            _mode = mode;
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            if (_modeButtons.Count == 0)
            {
                return;
            }

            for (var i = 0; i < _modeButtons.Count; i++)
            {
                _modeButtons[i].EnableInClassList("pd-seg__btn--on", i == _mode);
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
                for (var i = 0; i < card.Rows.Count; i++)
                {
                    var row = card.Rows[i];
                    var show = searching ? row.Matches(q) : (int)row.Tier <= _mode || row.NeedsAttention;
                    if (_modifiedOnly && !row.IsModified)
                    {
                        show = false;
                    }

                    row.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                    if (show)
                    {
                        visible++;
                        if (!row.IsDuplicate)
                        {
                            hits++;
                        }
                    }
                }

                card.SetVisibility(visible, filtering);
            }

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
                _hit.style.display = filtering ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // ── チップ・ボタン ──

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
                cls = "pd-chip--err";
            }
            else if (_warnings > 0)
            {
                status = $"⚠ 警告 {_warnings}";
                cls = "pd-chip--warn";
            }
            else
            {
                status = "✓ 検証 OK";
                cls = "pd-chip--ok";
            }

            SetText(_chipStatus, status);
            _chipStatus.EnableInClassList("pd-chip--ok", cls == "pd-chip--ok");
            _chipStatus.EnableInClassList("pd-chip--warn", cls == "pd-chip--warn");
            _chipStatus.EnableInClassList("pd-chip--err", cls == "pd-chip--err");
            _chipStatus.tooltip = "Validation の結果。該当する欄の下に表示されます";

            var count = 0;
            for (var i = 0; i < _changeRows.Count; i++)
            {
                if (_changeRows[i].IsModified)
                {
                    count++;
                }
            }

            _modifiedCount = count;
            SetText(_chipMod, count > 0 ? $"● 変更 {count} 件" : "変更なし");
            _chipMod.EnableInClassList("pd-chip--mod", count > 0);
            _chipMod.EnableInClassList("pd-chip--on", _modifiedOnly);
            _chipMod.tooltip = count > 0 ? "既定値と違う設定の数。クリックで「変更した設定だけ」を切り替え" : "既定値と違う設定はありません";
            _btnResetAll?.SetEnabled(count > 0);
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
                if (_changeRows[i].IsModified)
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
                if (_changeRows[i].IsModified)
                {
                    _changeRows[i].ApplyDefault();
                }
            }

            So.ApplyModifiedProperties();
            EditorUtility.SetDirty(Target);
            ReapplyAnchor();
            RunValidation();
            RefreshAll(true);
        }

        // ── 定期更新(外部変更・再生状態・テーマ) ──

        protected override void OnTick()
        {
            if (EditorGUIUtility.isProSkin != _lastPro)
            {
                // Light / Dark が切り替わった: クラスを付け替え、Painter の色を読み直すため作り直す。
                ApplyTheme();
                RebuildBody();
                return;
            }

            if (_btnPlay == null || Target == null || So == null)
            {
                return;
            }

            var playing = IsPlaying;
            if (playing != _wasPlaying)
            {
                _wasPlaying = playing;
                _btnPlay.text = playing ? "● 再生中" : "▶ 再生";
                _btnPlay.EnableInClassList("pd-btn--playing", playing);
            }

            var dirty = EditorUtility.IsDirty(Target);
            var saveText = dirty ? "保存 ●" : "保存";
            if (_btnSave != null && _btnSave.text != saveText)
            {
                _btnSave.text = saveText;
            }

            So.Update();
            var sig = 17;
            for (var i = 0; i < _rows.Count; i++)
            {
                _rows[i].UpdateState();
                _rows[i].Sync();
                sig = sig * 31 + (_rows[i].IsModified ? 1 : 0) + (_rows[i].HasIssues ? 2 : 0);
            }

            if (sig != _signature)
            {
                _signature = sig;
                UpdateChips();
                if (_modifiedOnly)
                {
                    ApplyFilters();
                }
            }

            _tick++;
            if (_tick % 7 == 0)
            {
                RunValidation();
            }
        }
    }
}
