using System;
using System.Collections.Generic;
using System.Text;
using DDrive.Editor.CanvasTool;
using DDrive.Editor.Inspector;
using DDrive.Editor.Ux;
using DDrive.Editor.Vfx;
using DDrive.Foundation.Data;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Presentation
{
    // 「トラック」の欄(docs/1010 UX-5 / D-10)。1 トラック = 1 行(# ・Kind のチップ・アセット名・「t=0.35s · 位置: Self (0, 1.2, 0) · Params 2」の 1 行要約・▶・↺・⚙)。
    // 展開(⚙)はその行だけで、Kind / Asset / Trigger / Time / SignalKey / Target / Anchor(AnchorPad)/ Params(TuneKnobs)/ Scope / StopOnCancel・
    // 専用エディタで開く・並べ替え・複製・削除が出る。一覧の上に絞り込み(検索・調整あり / なし・Kind のチップ)と件数。20 行を超えたら ListView。
    internal sealed partial class PresentationEditor
    {
        // タイムラインのレーン(Kind をまとめる粒度)と、一覧の Kind チップの絞り込み。
        private static readonly (TrackKind[] kinds, string label, string chip)[] Lanes =
        {
            (new[] { TrackKind.Anim, TrackKind.Anim2D }, "Anim / Anim2D", "Anim"),
            (new[] { TrackKind.Se, TrackKind.Bgm }, "Se / Bgm", "Se / Bgm"),
            (new[] { TrackKind.Vfx, TrackKind.AnchorGroup }, "Vfx / AnchorGroup", "Vfx"),
            (new[] { TrackKind.CameraShake, TrackKind.Haptic }, "CameraShake / Haptic", "Shake / Haptic"),
            (new[] { TrackKind.HitStop, TrackKind.Marker, TrackKind.Signal }, "HitStop / Marker / Signal", "HitStop / 合図"),
            (new[] { TrackKind.Canvas, TrackKind.UiTween, TrackKind.Timeline }, "Canvas / UiTween / Timeline", "UI / Timeline"),
        };

        private static int LaneIndexFor(TrackKind kind)
        {
            for (var i = 0; i < Lanes.Length; i++)
            {
                foreach (var k in Lanes[i].kinds)
                {
                    if (k == kind)
                    {
                        return i;
                    }
                }
            }

            return Lanes.Length - 1;
        }

        private static string[] LaneChips()
        {
            var a = new string[Lanes.Length];
            for (var i = 0; i < a.Length; i++)
            {
                a[i] = Lanes[i].chip;
            }

            return a;
        }

        private sealed class TrackHead
        {
            public Label Name;
            public Label Summary;
            public Button Reset;
            public Button Play;
        }

        private int _selectedTrack = -1;
        private int _openTrack = -1;
        private CanvasItemList _trackList;
        private PresentationSyncBox _trackSection;
        private List<CanvasListItem> _trackItems = new();
        private readonly Dictionary<int, Label> _trackFlags = new();
        private readonly Dictionary<int, TrackHead> _trackHeads = new();
        private readonly List<(VisualElement el, Action sync)> _detailSyncs = new();
        private readonly Dictionary<(Type, ulong), AssetDataBase> _assetCache = new();

        // ── 参照先アセットの解決(プロジェクト検索は重いのでキャッシュ。プロジェクトの変更で捨てる) ──

        private AssetDataBase ResolveAsset(in PresentationTrack track)
        {
            var type = PresentationTrackKindMapping.AssetTypeFor(track.Kind);
            if (type == null || !track.Asset.IsAssigned)
            {
                return null;
            }

            var key = (type, track.Asset.Id);
            if (!_assetCache.TryGetValue(key, out var asset))
            {
                asset = PresentationTrackKindMapping.FindAssetById(type, track.Asset.Id);
                _assetCache[key] = asset;
            }

            return asset;
        }

        // プロジェクトの変更(アセットの追加・名前の変更・保存)があったら、参照先の引き当てを捨てて行の文言だけ最新にする
        // (行は作り直さない = 入力中の欄からフォーカスを奪わない)。
        private void OnProjectChangedForAssets()
        {
            _assetCache.Clear();
            if (Target != null)
            {
                RefreshTrackSummaries();
            }
        }

        // ── 欄 ──

        private UxRow CreateTrackRows(FieldGuideEntry e)
        {
            _trackList = new CanvasItemList("トラック", "調整あり", LaneChips(), "トラックがありません。「＋ トラック」を押すか、Data をここ(またはタイムライン)へドラッグして追加してください。", "調整なし");
            var box = new PresentationSyncBox(TrackSignature, RebuildTrackList, SyncTrackDetails);

            var bar = new VisualElement();
            bar.AddToClassList("ux-toolsline");
            var add = MakeButton("＋ トラック", "ux-btn--primary", null, "新しいトラックを追加する(Kind を選ぶ)。Data を ドラッグしても追加できる");
            add.clicked += () => ShowAddTrackMenu(add);
            bar.Add(add);
            var hint = new Label("Data(Se / Vfx / Anim など)をこの一覧やタイムラインへドラッグして追加することもできます");
            hint.AddToClassList("ux-dim");
            hint.AddToClassList("ux-note");
            bar.Add(hint);
            box.Add(bar);
            box.Add(_trackList);

            box.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (HasDraggableAssets())
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    evt.StopPropagation();
                }
            });
            box.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (AddTracksFromDrag(0f))
                {
                    DragAndDrop.AcceptDrag();
                }

                evt.StopPropagation();
            });

            _trackSection = box;
            box.Rebuild();
            return new UxVisualRow(So, DefaultSo, new[] { e }, e.Label, e.Hint, e.Tier, box, RowChanged)
            {
                NoFold = true,
                NoBulkReset = true,
                MaxIssues = 3,
                ExtraMatch = q => _trackList != null && _trackList.AnyMatch(q),
            };
        }

        private void ShowAddTrackMenu(VisualElement anchor)
        {
            var menu = new GenericMenu();
            foreach (TrackKind kind in Enum.GetValues(typeof(TrackKind)))
            {
                var k = kind;
                menu.AddItem(new GUIContent(k.ToString()), false, () => AddTrack(k, 0f, null));
            }

            menu.DropDown(anchor.worldBound);
        }

        // ── 一覧の項目 ──

        private int TrackSignature()
        {
            var t = Target;
            if (t == null)
            {
                return 0;
            }

            var h = CanvasSig.Add(17, t.Tracks != null ? t.Tracks.Length : 0);
            var tracks = t.Tracks;
            for (var i = 0; tracks != null && i < tracks.Length; i++)
            {
                h = CanvasSig.Add(h, (int)tracks[i].Kind);
                h = CanvasSig.Add(h, (int)tracks[i].Trigger);
                h = CanvasSig.Add(h, tracks[i].Asset.Id);
                h = CanvasSig.Add(h, tracks[i].Params != null ? tracks[i].Params.Length : -1);
                h = CanvasSig.Add(h, (int)tracks[i].Scope);
            }

            return h;
        }

        private void RebuildTrackList()
        {
            if (_trackList == null)
            {
                return;
            }

            _trackHeads.Clear();
            _trackFlags.Clear();
            _detailSyncs.Clear();
            _trackItems = CollectTrackItems();
            _trackList.SetItems(_trackItems);
            ApplyTrackFlags();
        }

        private List<CanvasListItem> CollectTrackItems()
        {
            var items = new List<CanvasListItem>();
            var tracks = Target?.Tracks;
            for (var i = 0; tracks != null && i < tracks.Length; i++)
            {
                var index = i;
                var kind = tracks[i].Kind;
                items.Add(new CanvasListItem
                {
                    Key = "trk:" + i,
                    Title = TrackAssetName(in tracks[i]),
                    Chip = kind.ToString(),
                    Kind = LaneIndexFor(kind),
                    IsConfigured = () => TrackAdjusted(index),
                    Search = kind + " " + TrackSummary(in tracks[i]),
                    Make = () => MakeTrackRow(index),
                });
            }

            return items;
        }

        // 行の外から Data が変わったあと(時刻・SignalKey・位置の編集、Undo)に、行の文言・件数・絞り込み用の語だけを最新にする(行は作り直さない)。
        private void RefreshTrackSummaries()
        {
            var tracks = Target?.Tracks;
            if (tracks == null)
            {
                return;
            }

            for (var i = 0; i < _trackItems.Count && i < tracks.Length; i++)
            {
                _trackItems[i].Title = TrackAssetName(in tracks[i]);
                _trackItems[i].Search = tracks[i].Kind + " " + TrackSummary(in tracks[i]);
            }

            foreach (var kv in _trackHeads)
            {
                if (kv.Key < 0 || kv.Key >= tracks.Length)
                {
                    continue;
                }

                var head = kv.Value;
                var track = tracks[kv.Key];
                SetLabel(head.Name, TrackAssetName(in track));
                SetLabel(head.Summary, TrackSummary(in track));
                if (head.Summary != null)
                {
                    head.Summary.tooltip = head.Summary.text;
                }

                head.Reset?.SetEnabled(TrackAdjusted(kv.Key) || track.Trigger != TrackTrigger.AtTime || track.Time != 0f || !string.IsNullOrEmpty(track.SignalKey));
            }

            _trackList?.UpdateCount();
        }

        private static void SetLabel(Label l, string text)
        {
            if (l != null && l.text != text)
            {
                l.text = text;
            }
        }

        // 位置・値・Scope に手を入れた(既定のままではない)か。一覧の「調整あり / 調整なし」の判定。
        private bool TrackAdjusted(int index)
        {
            var tracks = Target?.Tracks;
            if (tracks == null || index < 0 || index >= tracks.Length)
            {
                return false;
            }

            var t = tracks[index];
            return !t.Anchor.IsDefault || t.Target != TrackTargetMode.Self || (t.Params != null && t.Params.Length > 0) || t.Scope != PresentationEffectScope.Everyone;
        }

        private string TrackAssetName(in PresentationTrack t)
        {
            if (PresentationTrackKindMapping.AssetTypeFor(t.Kind) != null)
            {
                if (!t.Asset.IsAssigned)
                {
                    return "(Asset 未設定)";
                }

                var asset = ResolveAsset(in t);
                if (asset == null)
                {
                    return $"(Id 0x{t.Asset.Id:X} が見つかりません)";
                }

                return string.IsNullOrEmpty(asset.DisplayName) ? asset.name : asset.DisplayName;
            }

            if (t.Kind == TrackKind.Marker || t.Kind == TrackKind.Signal)
            {
                return string.IsNullOrEmpty(t.SignalKey) ? "(名前なし)" : t.SignalKey;
            }

            return t.Kind.ToString();
        }

        private static string TrackSummary(in PresentationTrack t)
        {
            var sb = new StringBuilder();
            sb.Append(t.Trigger == TrackTrigger.OnSignal ? "onSignal:" + t.SignalKey : "t=" + t.Time.ToString("0.##") + "s");
            if (PresentationTrackAnchorResolver.HasPosition(t.Kind))
            {
                sb.Append(" · ").Append(AnchorText(in t));
            }

            if (t.Params != null && t.Params.Length > 0)
            {
                sb.Append(" · Params ").Append(t.Params.Length);
            }

            if (t.Scope == PresentationEffectScope.ParticipantsOnly)
            {
                sb.Append(" · 当事者のみ");
            }

            return sb.ToString();
        }

        private static string AnchorText(in PresentationTrack t)
        {
            if (t.Target == TrackTargetMode.Self && t.Anchor.IsDefault)
            {
                return "位置: 既定";
            }

            var basis = t.Target == TrackTargetMode.Self ? "Self" : t.Target == TrackTargetMode.ContextTarget ? "Target" : t.Target == TrackTargetMode.World ? "World" : "Anchor";
            var path = (t.Anchor.Space == AnchorSpace.BoneName || t.Anchor.Space == AnchorSpace.NamedObject) && !string.IsNullOrEmpty(t.Anchor.Path) ? "/" + t.Anchor.Path : string.Empty;
            var o = t.Anchor.LocalOffset;
            return $"位置: {basis}{path} ({o.x:0.##}, {o.y:0.##}, {o.z:0.##})";
        }

        // ── 1 行 ──

        private VisualElement MakeTrackRow(int index)
        {
            var tracks = Target?.Tracks;
            if (tracks == null || index < 0 || index >= tracks.Length)
            {
                return new VisualElement();
            }

            var t = tracks[index];
            var open = index == _openTrack;
            var row = new VisualElement();
            row.AddToClassList("ux-fxrow");
            row.EnableInClassList("ux-fxrow--hl", index == _selectedTrack);
            row.EnableInClassList("ux-fxrow--open", open);

            var head = new VisualElement();
            head.AddToClassList("ux-fxrow__head");
            head.AddToClassList("ux-trk__head");
            var flag = new Label("⚠");
            flag.AddToClassList("ux-lineflag");
            var has = _trackIssue.TryGetValue(index, out var msg);
            flag.Show(has);
            flag.tooltip = has ? msg : string.Empty;
            _trackFlags[index] = flag;
            head.Add(flag);

            var idx = new Label("#" + index);
            idx.AddToClassList("ux-dim");
            idx.AddToClassList("ux-trk__idx");
            head.Add(idx);

            var chip = new Label(t.Kind.ToString()) { tooltip = "Kind(このトラックが委譲する Manager)" };
            chip.AddToClassList("ux-chip");
            chip.AddToClassList("ux-fxrow__chip");
            chip.AddToClassList("ux-kind--" + LaneIndexFor(t.Kind));
            head.Add(chip);

            var refs = new TrackHead();
            refs.Name = new Label(TrackAssetName(in t)) { tooltip = "クリックでこのトラックを選ぶ(タイムライン・SceneView の選択と共通)" };
            refs.Name.AddToClassList("ux-fxrow__name");
            refs.Name.RegisterCallback<ClickEvent>(_ => SelectTrack(index, false));
            head.Add(refs.Name);

            refs.Summary = new Label(TrackSummary(in t));
            refs.Summary.AddToClassList("ux-dim");
            refs.Summary.AddToClassList("ux-fxrow__summary");
            refs.Summary.tooltip = refs.Summary.text;
            head.Add(refs.Summary);

            refs.Play = new Button(() => AuditionTrack(index)) { text = "▶", tooltip = "このトラックだけを単体で試聴する(確認用シーンのモデルを基準に、実 Manager で再生。ほかのトラックは鳴らさない)" };
            refs.Play.AddToClassList("ux-btn");
            refs.Play.AddToClassList("ux-btn--icon");
            head.Add(refs.Play);

            refs.Reset = new Button(() => ResetTrack(index)) { text = "↺", tooltip = "このトラックの時刻・位置・Params・Scope を既定に戻す(Kind と Asset は残す。Ctrl+Z で戻せる)" };
            refs.Reset.AddToClassList("ux-btn");
            refs.Reset.AddToClassList("ux-btn--icon");
            refs.Reset.SetEnabled(TrackAdjusted(index) || t.Trigger != TrackTrigger.AtTime || t.Time != 0f || !string.IsNullOrEmpty(t.SignalKey));
            head.Add(refs.Reset);

            var toggle = new Button(() => ToggleTrackOpen(index)) { text = open ? "▾" : "⚙", tooltip = "このトラックの詳細を開く / 閉じる(Kind・Asset・時刻・位置・Params・Scope・専用エディタ・並べ替え・複製・削除)" };
            toggle.AddToClassList("ux-btn");
            toggle.AddToClassList("ux-btn--icon");
            head.Add(toggle);
            row.Add(head);
            _trackHeads[index] = refs;

            if (open)
            {
                row.Add(MakeTrackDetails(index));
            }

            return row;
        }

        private void ToggleTrackOpen(int index)
        {
            _openTrack = _openTrack == index ? -1 : index;
            if (_openTrack == index)
            {
                _selectedTrack = index;
            }

            ReRenderTrackList();
        }

        // 選ぶ(タイムラインのマーカー・SceneView の点・一覧の名前から共通)。open = その行を開く。
        private void SelectTrack(int index, bool open)
        {
            _selectedTrack = index;
            if (open)
            {
                _openTrack = index;
            }

            ReRenderTrackList();
            _timelineContainer?.MarkDirtyRepaint();
            SceneView.RepaintAll();
        }

        private void ReRenderTrackList()
        {
            _trackHeads.Clear();
            _trackFlags.Clear();
            _detailSyncs.Clear();
            _trackList?.Render();
            ApplyTrackFlags();
        }

        // ── 展開(その行だけ) ──

        private PropertyField TrackProp(int index, string field, string label)
        {
            var prop = So?.FindProperty($"Tracks.Array.data[{index}].{field}");
            if (prop == null)
            {
                return null;
            }

            var pf = new PropertyField(prop, label);
            pf.AddToClassList("ux-fxrow__prop");
            pf.BindProperty(prop);
            return pf;
        }

        private VisualElement MakeTrackDetails(int index)
        {
            var d = Target;
            var track = d.Tracks[index];
            var box = new VisualElement();
            box.AddToClassList("ux-fxrow__details");

            // Kind / Asset
            var kindLine = new VisualElement();
            kindLine.AddToClassList("ux-fxrow__props");
            var kindField = new EnumField("Kind", track.Kind) { tooltip = "このトラックが委譲する Manager の種別。変えると Asset は空に戻ります" };
            kindField.AddToClassList("ux-fxrow__prop");
            kindField.RegisterValueChangedCallback(evt =>
            {
                if ((TrackKind)evt.newValue == track.Kind)
                {
                    return;
                }

                EditTrack(index, "Change Presentation Track Kind", t =>
                {
                    t.Kind = (TrackKind)evt.newValue;
                    t.Asset = default;
                    return t;
                });
            });
            kindLine.Add(kindField);

            var assetType = PresentationTrackKindMapping.AssetTypeFor(track.Kind);
            if (assetType != null)
            {
                var assetField = new ObjectField("Asset") { objectType = assetType, tooltip = "再生する Data(" + assetType.Name + ")" };
                assetField.AddToClassList("ux-fxrow__prop");
                assetField.SetValueWithoutNotify(ResolveAsset(in track));
                assetField.RegisterValueChangedCallback(evt =>
                {
                    var picked = evt.newValue as AssetDataBase;
                    EditTrack(index, "Set Presentation Track Asset", t =>
                    {
                        t.Asset = picked != null ? new AssetRef { Type = PresentationTrackKindMapping.AssetKindFor(t.Kind), Id = picked.Id } : default;
                        return t;
                    });
                });
                kindLine.Add(assetField);
            }

            box.Add(kindLine);

            // いつ・誰基準で
            var when = new VisualElement();
            when.AddToClassList("ux-fxrow__props");
            AddIfNotNull(when, TrackProp(index, "Trigger", "Trigger"));
            AddIfNotNull(when, TrackProp(index, "Time", "Time(秒)"));
            AddIfNotNull(when, TrackProp(index, "SignalKey", "SignalKey"));
            AddIfNotNull(when, TrackProp(index, "Target", "Target"));
            box.Add(when);

            // 位置(Vfx / Se)
            if (track.Kind == TrackKind.Vfx || track.Kind == TrackKind.Se)
            {
                box.Add(MakeAnchorBlock(index));
            }
            else
            {
                box.Add(MakeAnchorFoldout(index, false));
            }

            // Params(つまみ)
            box.Add(MakeParamsBlock(index, track));

            var props = new VisualElement();
            props.AddToClassList("ux-fxrow__props");
            if (track.Kind == TrackKind.CameraShake || track.Kind == TrackKind.Haptic || track.Kind == TrackKind.HitStop)
            {
                AddIfNotNull(props, TrackProp(index, "Scope", "Scope"));
            }

            AddIfNotNull(props, TrackProp(index, "StopOnCancel", "StopOnCancel"));
            box.Add(props);

            // 専用エディタ
            if (assetType != null && DataEditorRegistry.GetEntries(assetType).Count > 0)
            {
                var open = new VisualElement();
                open.AddToClassList("ux-toolsline");
                open.Add(MakeButton("専用エディタで開く(一緒に調整)", null, () => OpenTrackEditorTogether(index), "確認用シーン・配置済みモデル・再生状態はそのまま。専用エディタの「スポーン先」に統合プレビューが配置した Self を渡す(Vfx のみ。ほかの種別は通常どおり開く)"));
                open.Add(MakeButton("単体で確認用シーンに開き直す", null, () => OpenTrackEditorAlone(index), "専用エディタのいつもの流れで開く(その Data 単体)。統合プレビューは片付きます"));
                box.Add(open);
            }

            // 並べ替え・複製・削除
            var ops = new VisualElement();
            ops.AddToClassList("ux-toolsline");
            var up = MakeButton("▲ 上へ", null, () => MoveTrack(index, -1), "一覧の並びを 1 つ上へ(再生には影響しません)");
            up.SetEnabled(index > 0);
            ops.Add(up);
            var down = MakeButton("▼ 下へ", null, () => MoveTrack(index, 1), "一覧の並びを 1 つ下へ(再生には影響しません)");
            down.SetEnabled(index < d.Tracks.Length - 1);
            ops.Add(down);
            ops.Add(MakeButton("複製", null, () => DuplicateTrack(index), "このトラックを直後に複製する"));
            ops.Add(MakeButton("削除", null, () => RemoveTrack(index), "このトラックを削除する(Ctrl+Z で戻せる)"));
            box.Add(ops);
            return box;
        }

        private static void AddIfNotNull(VisualElement parent, VisualElement child)
        {
            if (child != null)
            {
                parent.Add(child);
            }
        }

        // Anchor(出る位置): 上から見たパッド + 全設定(基準・回転・スケール)。アセット側の Anchor との関係(3 ケース)を 1 行で出す。
        private VisualElement MakeAnchorBlock(int index)
        {
            var d = Target;
            var wrap = new VisualElement();
            var caption = new Label(AnchorCaption(index));
            caption.AddToClassList("ux-dim");
            caption.AddToClassList("ux-note");
            wrap.Add(caption);

            var pad = new AnchorPad(
                d,
                () => index < d.Tracks.Length ? d.Tracks[index].Anchor.LocalOffset : Vector3.zero,
                v =>
                {
                    if (index >= d.Tracks.Length)
                    {
                        return;
                    }

                    var t = d.Tracks[index];
                    t.Anchor.LocalOffset = v;
                    d.Tracks[index] = t;
                },
                final =>
                {
                    VisualEdited(final, true);
                    caption.text = AnchorCaption(index);
                });
            wrap.Add(pad);
            _detailSyncs.Add((pad, pad.Sync));
            wrap.Add(MakeAnchorFoldout(index, true));
            return wrap;
        }

        private string AnchorCaption(int index)
        {
            var d = Target;
            if (d?.Tracks == null || index < 0 || index >= d.Tracks.Length)
            {
                return string.Empty;
            }

            var current = d.Tracks[index];
            if (current.Kind != TrackKind.Vfx && current.Kind != TrackKind.Se)
            {
                return "Anchor(VFX / SE の位置)";
            }

            var asset = ResolveAsset(in current);
            PresentationTrackAnchorComposer.TryGetAssetAnchor(asset, out var assetAnchorId, out var assetEmbedded);
            var kase = PresentationTrackAnchorComposer.DetermineCase(in current, assetAnchorId, in assetEmbedded);
            return "Anchor(出る位置) — " + DescribeAnchorCase(kase);
        }

        private static string DescribeAnchorCase(PresentationTrackAnchorComposer.Case kase) => kase switch
        {
            PresentationTrackAnchorComposer.Case.AssetOnly => "現在: アセット側の Anchor を使用",
            PresentationTrackAnchorComposer.Case.TrackOnly => "現在: トラックの Anchor を使用",
            PresentationTrackAnchorComposer.Case.Both => "現在: 両方設定されているため親子合成(トラックが親)",
            _ => "現在: 未設定(ワールド原点)",
        };

        // 位置の全設定(基準・Path・回転・スケール・追従)。入れ子の Foldout を作らないよう、Anchor の子の欄を直接並べる。
        private VisualElement MakeAnchorFoldout(int index, bool extra)
        {
            var fold = new Foldout { text = extra ? "位置の全設定(基準・回転・スケール)" : "Anchor(VFX / SE の位置。この Kind では使われません)", value = false };
            var anchor = So?.FindProperty($"Tracks.Array.data[{index}].Anchor");
            if (anchor == null)
            {
                return fold;
            }

            var it = anchor.Copy();
            var end = anchor.GetEndProperty();
            var enter = true;
            while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
            {
                enter = false;
                var prop = it.Copy();
                var pf = new PropertyField(prop);
                pf.AddToClassList("ux-fxrow__prop");
                pf.BindProperty(prop);
                fold.Add(pf);
            }

            return fold;
        }

        // Params(上書き値)。Params[i] は参照先 VFX の Params[i] に順番で対応する(つまみの名前に出る)。
        private VisualElement MakeParamsBlock(int index, PresentationTrack track)
        {
            var wrap = new VisualElement();
            var cap = new Label("Params(参照先の値を上書き)");
            cap.AddToClassList("ux-dim");
            cap.AddToClassList("ux-note");
            wrap.Add(cap);
            if (track.Kind == TrackKind.Vfx)
            {
                var vfx = ResolveAsset(in track) as VfxData;
                if (vfx == null || vfx.Params == null || vfx.Params.Length == 0)
                {
                    var hint = new Label("参照先 VFX に Params が未設定のため、ここへ追加しても反映されません");
                    hint.AddToClassList("ux-dim");
                    hint.AddToClassList("ux-note");
                    wrap.Add(hint);
                }
            }

            var knobs = new TuneKnobs(
                new PresentationTrackTuneSource(Target, index),
                So,
                () => VisualEdited(false, false),
                () =>
                {
                    So?.Update();
                    RunValidation();
                    RefreshAll(false);
                    _trackSection?.Sync();
                });
            wrap.Add(knobs);
            _detailSyncs.Add((knobs, knobs.Sync));
            return wrap;
        }

        private void SyncTrackDetails()
        {
            for (var i = _detailSyncs.Count - 1; i >= 0; i--)
            {
                var (el, sync) = _detailSyncs[i];
                if (el.panel == null)
                {
                    _detailSyncs.RemoveAt(i);
                    continue;
                }

                sync();
            }
        }

        // ── トラックの操作(すべて Undo 1 回) ──

        private void EditTrack(int index, string undoName, Func<PresentationTrack, PresentationTrack> change)
        {
            var d = Target;
            if (d?.Tracks == null || index < 0 || index >= d.Tracks.Length)
            {
                return;
            }

            Undo.RecordObject(d, undoName);
            d.Tracks[index] = change(d.Tracks[index]);
            EditorUtility.SetDirty(d);
            So?.Update();
            AfterDataWrite();
        }

        private void ResetTrack(int index)
        {
            EditTrack(index, "Presentation Track: 既定に戻す", t =>
            {
                var fresh = PresentationTrackEditOps.CreateTrack(t.Kind, 0f, null);
                fresh.Asset = t.Asset;
                return fresh;
            });
        }

        private void AddTrack(TrackKind kind, float time, AssetDataBase asset)
        {
            if (Target == null)
            {
                return;
            }

            _selectedTrack = PresentationTrackEditOps.AddTrack(Target, kind, time, asset);
            _openTrack = _selectedTrack;
            So?.Update();
            AfterDataWrite();
            ReRenderTrackList();
        }

        private void DuplicateTrack(int index)
        {
            if (Target == null)
            {
                return;
            }

            PresentationTrackEditOps.DuplicateTrack(Target, index);
            So?.Update();
            _selectedTrack = index + 1;
            _openTrack = _selectedTrack;
            AfterDataWrite();
            ReRenderTrackList();
        }

        private void RemoveTrack(int index)
        {
            if (Target == null)
            {
                return;
            }

            PresentationTrackEditOps.RemoveTrack(Target, index);
            So?.Update();
            _selectedTrack = -1;
            _openTrack = -1;
            AfterDataWrite();
            ReRenderTrackList();
        }

        private void MoveTrack(int index, int delta)
        {
            var d = Target;
            var other = index + delta;
            if (d?.Tracks == null || index < 0 || index >= d.Tracks.Length || other < 0 || other >= d.Tracks.Length)
            {
                return;
            }

            Undo.RecordObject(d, "Move Presentation Track In List");
            (d.Tracks[index], d.Tracks[other]) = (d.Tracks[other], d.Tracks[index]);
            EditorUtility.SetDirty(d);
            So?.Update();
            _selectedTrack = other;
            _openTrack = other;
            AfterDataWrite();
            ReRenderTrackList();
        }

        private static bool HasDraggableAssets()
        {
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is AssetDataBase asset && PresentationTrackKindMapping.TryKindFor(asset, out _))
                {
                    return true;
                }
            }

            return false;
        }

        private bool AddTracksFromDrag(float time)
        {
            var added = false;
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is not AssetDataBase asset || !PresentationTrackKindMapping.TryKindFor(asset, out var kind))
                {
                    continue;
                }

                AddTrack(kind, time, asset);
                added = true;
            }

            return added;
        }

        // ── 専用エディタで開く(新エディターへ。未移行の種別は DataEditorRegistry の第 1 候補 = 移行後は自動で新へ向く) ──

        private void OpenTrackEditorTogether(int index)
        {
            var asset = ResolveOpenableTrackAsset(index);
            if (asset == null)
            {
                AppendLog("⚠ Asset が未設定のため専用エディタを開けません");
                return;
            }

            var attach = _preview?.SelfRoot != null ? _preview.SelfRoot.gameObject : null;
            if (PresentationTrackEditorRouting.Classify(asset) == PresentationTrackEditorRouting.EditorKind.VfxWithAttach)
            {
                VfxEditor.Open((VfxData)asset, attach);
            }
            else
            {
                DataEditorRegistry.OpenDefault(asset);
            }

            AppendLog($"'{asset.DisplayName ?? asset.name}' を専用エディタで開きました(一緒に調整。確認用シーン・再生状態は維持)");
        }

        private void OpenTrackEditorAlone(int index)
        {
            var asset = ResolveOpenableTrackAsset(index);
            if (asset == null)
            {
                AppendLog("⚠ Asset が未設定のため専用エディタを開けません");
                return;
            }

            if (DataEditorRegistry.OpenDefault(asset))
            {
                AppendLog($"'{asset.DisplayName ?? asset.name}' を専用エディタで開きました(単体で確認用シーンに開き直す)");
            }
        }

        private AssetDataBase ResolveOpenableTrackAsset(int index)
        {
            var tracks = Target?.Tracks;
            if (tracks == null || index < 0 || index >= tracks.Length)
            {
                return null;
            }

            return ResolveAsset(in tracks[index]);
        }
    }
}
