using System;
using System.Collections.Generic;
using System.Text;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Audio;
using DDrive.Editor.CanvasTool;
using DDrive.Editor.Common;
using DDrive.Editor.Inspector;
using DDrive.Editor.Ux;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anim
{
    // 「合図(イベント)」の欄(docs/1010 UX-6 / D-10)。
    // タイムライン(IMGUI。クリックでシーク・マーカーのドラッグで時刻変更・SE の波形)+ イベントの一覧(1 イベント = 1 行)。
    // 1 行 = # ・種類のチップ(SE / VFX / 配置セット / ほか)・対象の名前・「F12 (0.40s) · 毎周回」の 1 行要約・▶ 試聴・⚙・✕。
    // 展開(⚙)はその行だけで、Action / Trigger / 時刻 / Target / Repeat / Param・専用エディタで開く・複製。一覧の上に絞り込み(検索・設定済み / 未設定・種類のチップ)と件数。
    // 保存先は従来どおり AnimData.Events(クリップに AnimationEvent は書かない)。20 行を超えたら ListView。
    internal sealed partial class AnimEditor
    {
        private const float TimelineHeight = 62f; // フレーム番号の行を含む
        private static readonly string[] EventKinds = { "SE", "VFX", "配置セット", "ほか" };
        private static readonly List<string> RepeatLabels = new() { "毎周回", "1 回", "再生中は維持" };

        private sealed class AssetChoice
        {
            public AssetType Type;
            public AssetDataBase Asset;
            public string Label;
        }

        private sealed class EventHead
        {
            public Label Name;
            public Label Summary;
        }

        private IMGUIContainer _timelineContainer;
        private CanvasItemList _eventList;
        private AnimSyncBox _eventSection;
        private List<CanvasListItem> _eventItems = new();
        private readonly Dictionary<int, Label> _eventFlags = new();
        private readonly Dictionary<int, EventHead> _eventHeads = new();
        private readonly List<Action> _eventDetailSyncs = new();
        private int _openEvent = -1;
        private int _selectedEvent = -1;
        private int _draggingEvent = -1;
        private int _dragUndoGroup;
        private ObjectField _copyTargetField;
        private readonly List<(float x, string text)> _timelineLabels = new();

        private readonly List<AssetChoice> _assetChoices = new();
        private readonly Dictionary<ulong, SeData> _seById = new();
        private bool _choicesDirty = true;

        // ── 参照先アセットの一覧(SE / VFX / 配置セット。プロジェクト検索は重いのでキャッシュ。プロジェクトの変更で捨てる) ──

        private void OnProjectChangedForChoices() => _choicesDirty = true;

        private void EnsureAssetChoices()
        {
            if (!_choicesDirty && _assetChoices.Count > 0)
            {
                return;
            }

            RefreshAssetChoices();
        }

        // プロジェクト内の SE / VFX / 配置セットを列挙する。
        private void RefreshAssetChoices()
        {
            _choicesDirty = false;
            _assetChoices.Clear();
            _seById.Clear();
            Collect<SeData>(AssetType.Se, "SE");
            Collect<VfxData>(AssetType.Vfx, "VFX");
            Collect<AnchorGroupData>(AssetType.AnchorGroup, "配置セット");
        }

        private void Collect<T>(AssetType type, string prefix) where T : AssetDataBase
        {
            var start = _assetChoices.Count;
            foreach (var guid in AssetSearch.FindAssets("t:" + typeof(T).Name))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Tests/"))
                {
                    continue;
                }

                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset == null || asset.Id == 0)
                {
                    continue;
                }

                _assetChoices.Add(new AssetChoice { Type = type, Asset = asset, Label = $"{prefix}: {asset.DisplayName ?? asset.name}" });
                if (asset is SeData se)
                {
                    _seById[se.Id] = se;
                }
            }

            _assetChoices.Sort(start, _assetChoices.Count - start, Comparer<AssetChoice>.Create((a, b) => string.CompareOrdinal(a.Label, b.Label)));
        }

        private AssetDataBase FindAsset(AssetRef target)
        {
            if (!target.IsAssigned)
            {
                return null;
            }

            EnsureAssetChoices();
            foreach (var c in _assetChoices)
            {
                if (c.Type == target.Type && c.Asset.Id == target.Id)
                {
                    return c.Asset;
                }
            }

            return null;
        }

        private static AssetRef ToRef(AssetChoice choice) => choice.Type switch
        {
            AssetType.Se => AssetRef.From(new AssetId<SeMarker>(choice.Asset.Id, AssetType.Se)),
            AssetType.Vfx => AssetRef.From(new AssetId<VfxMarker>(choice.Asset.Id, AssetType.Vfx)),
            AssetType.AnchorGroup => AssetRef.From(new AssetId<AnchorGroupMarker>(choice.Asset.Id, AssetType.AnchorGroup)),
            _ => default,
        };

        // ── 欄: タイムライン ──

        private UxRow CreateTimelineRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            _timelineContainer = new IMGUIContainer(DrawTimeline);
            _timelineContainer.AddToClassList("ux-atl");
            box.Add(_timelineContainer);
            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        // ── 欄: イベントの一覧 ──

        private UxRow CreateEventRows(FieldGuideEntry e)
        {
            _eventList = new CanvasItemList("イベント", "対象あり", EventKinds, "イベントがありません。「＋ 現在位置に SE」などで追加してください。", "対象なし");
            var box = new AnimSyncBox(EventSignature, RebuildEventList, SyncEventDetails);

            var bar = new VisualElement();
            bar.AddToClassList("ux-toolsline");
            bar.Add(MakeButton("＋ 現在位置に SE", "ux-btn--primary", () => AddEventAtPlayhead(AssetType.Se), "再生ヘッドの位置(フレーム)に SE のイベントを追加する"));
            bar.Add(MakeButton("＋ VFX", null, () => AddEventAtPlayhead(AssetType.Vfx), "再生ヘッドの位置に VFX のイベントを追加する"));
            bar.Add(MakeButton("＋ 配置セット", null, () => AddEventAtPlayhead(AssetType.AnchorGroup), "再生ヘッドの位置に配置セット(AnchorGroup)のイベントを追加する"));
            bar.Add(MakeButton("＋ そのほか", null, () => AddEventAtPlayhead(AssetType.None), "対象のない空のイベントを追加する(Action を SetParam / SendMessage / Duck に変えて使う)"));
            bar.Add(MakeButton("一覧を更新", null, () =>
            {
                RefreshAssetChoices();
                RebuildEventList();
            }, "SE / VFX アセットを追加・改名したあとに押す"));
            box.Add(bar);
            box.Add(_eventList);
            box.Add(MakeCopyFoldout());

            _eventSection = box;
            box.Rebuild();
            return new UxVisualRow(So, DefaultSo, new[] { e }, e.Label, e.Hint, e.Tier, box, RowChanged)
            {
                NoFold = true,
                NoBulkReset = true,
                MaxIssues = 3,
                ExtraMatch = q => _eventList != null && _eventList.AnyMatch(q),
            };
        }

        private VisualElement MakeCopyFoldout()
        {
            var fold = new Foldout { text = "イベントをほかの AnimData へコピー", value = false };
            var row = new VisualElement();
            row.AddToClassList("ux-toolsline");
            _copyTargetField = new ObjectField { objectType = typeof(AnimData), tooltip = "コピー先の AnimData(対象自身は不可)。Events はまるごと置き換わる" };
            row.Add(_copyTargetField);
            row.Add(MakeButton("コピー", null, CopyEventsToField, "このイベントをコピー先の AnimData へ(Ctrl+Z で戻せる)"));
            fold.Add(row);
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            line.Add(MakeButton("同じステートの他クリップの AnimData へ一括コピー(無ければ作成)", null, CopyEventsToStateClips, "「Animator のステートから選ぶ」で同じステートにある他のクリップの AnimData へまとめてコピーする"));
            fold.Add(line);
            return fold;
        }

        // 一覧を作り直すか(Time の変更では作り直さない = 入力中の欄からフォーカスを奪わない)。
        private int EventSignature()
        {
            var t = Target;
            if (t == null)
            {
                return 0;
            }

            var events = t.Events;
            var h = CanvasSig.Add(17, events != null ? events.Length : 0);
            for (var i = 0; events != null && i < events.Length; i++)
            {
                h = CanvasSig.Add(h, (int)events[i].Trigger);
                h = CanvasSig.Add(h, (int)events[i].Action);
                h = CanvasSig.Add(h, (int)events[i].Repeat);
                h = CanvasSig.Add(h, events[i].Target.Id);
                h = CanvasSig.Add(h, (int)events[i].Target.Type);
            }

            return CanvasSig.Add(h, _choicesDirty ? 1 : 0);
        }

        private void RebuildEventList()
        {
            if (_eventList == null)
            {
                return;
            }

            EnsureAssetChoices();
            _eventHeads.Clear();
            _eventFlags.Clear();
            _eventDetailSyncs.Clear();
            _eventItems = CollectEventItems();
            _eventList.SetItems(_eventItems);
            ApplyEventFlags();
        }

        private List<CanvasListItem> CollectEventItems()
        {
            var items = new List<CanvasListItem>();
            var events = Target?.Events;
            for (var i = 0; events != null && i < events.Length; i++)
            {
                var index = i;
                var e = events[i];
                items.Add(new CanvasListItem
                {
                    Key = "evt:" + i,
                    Title = EventTitle(e),
                    Chip = EventKinds[EventKindIndex(e)],
                    Kind = EventKindIndex(e),
                    IsConfigured = () => EventHasTarget(index),
                    Search = EventKinds[EventKindIndex(e)] + " " + EventSummary(e),
                    Make = () => MakeEventRow(index),
                });
            }

            return items;
        }

        private static int EventKindIndex(in AssetEvent e)
        {
            if (e.Action != EventAction.PlayAsset)
            {
                return 3;
            }

            switch (e.Target.Type)
            {
                case AssetType.Se: return 0;
                case AssetType.Vfx: return 1;
                case AssetType.AnchorGroup: return 2;
                default: return 3;
            }
        }

        // 「対象あり」: PlayAsset は Target が入っている、それ以外の Action は常に「あり」。
        private bool EventHasTarget(int index)
        {
            var events = Target?.Events;
            if (events == null || index < 0 || index >= events.Length)
            {
                return false;
            }

            return events[index].Action != EventAction.PlayAsset || events[index].Target.IsAssigned;
        }

        private string EventTitle(in AssetEvent e)
        {
            if (e.Action != EventAction.PlayAsset)
            {
                return string.IsNullOrEmpty(e.CustomKey) ? e.Action.ToString() : e.Action + " " + e.CustomKey;
            }

            if (!e.Target.IsAssigned)
            {
                return "(対象未設定)";
            }

            var asset = FindAsset(e.Target);
            return asset == null ? $"(Id 0x{e.Target.Id:X} が見つかりません)" : (asset.DisplayName ?? asset.name);
        }

        private string EventSummary(in AssetEvent e)
        {
            var fps = Target != null ? Target.FrameRate : 30f;
            var sb = new StringBuilder();
            switch (e.Trigger)
            {
                case EventTrigger.Frame:
                    sb.Append('F').Append(e.Time.ToString("0.#")).Append(" (").Append((e.Time / fps).ToString("0.00")).Append("s)");
                    break;
                case EventTrigger.Time:
                    sb.Append(e.Time.ToString("0.00")).Append("s (F").Append((e.Time * fps).ToString("0.#")).Append(')');
                    break;
                default:
                    sb.Append(e.Trigger);
                    break;
            }

            if (e.Trigger == EventTrigger.Frame || e.Trigger == EventTrigger.Time)
            {
                sb.Append(" · ").Append(RepeatLabels[Mathf.Clamp((int)e.Repeat, 0, RepeatLabels.Count - 1)]);
            }

            if (e.Action != EventAction.PlayAsset)
            {
                sb.Append(" · ").Append(e.Action);
            }

            return sb.ToString();
        }

        // Frame / Time イベントの時刻(秒)。それ以外(OnLoop など)は -1。
        private float EventSeconds(in AssetEvent e)
        {
            var fps = Target != null ? Target.FrameRate : 30f;
            switch (e.Trigger)
            {
                case EventTrigger.Frame: return e.Time / fps;
                case EventTrigger.Time: return e.Time;
                default: return -1f;
            }
        }

        private bool EventOverLength(in AssetEvent e)
        {
            var sec = EventSeconds(in e);
            return Target != null && Target.Clip != null && sec > Target.LengthSec + 1e-3f;
        }

        // 行の外から Data が変わったあと(時刻の編集・タイムラインのドラッグ・Undo)に、行の文言・件数・絞り込み用の語だけを最新にする(行は作り直さない)。
        private void RefreshEventSummaries()
        {
            var events = Target?.Events;
            if (events == null)
            {
                return;
            }

            for (var i = 0; i < _eventItems.Count && i < events.Length; i++)
            {
                _eventItems[i].Title = EventTitle(events[i]);
                _eventItems[i].Search = EventKinds[EventKindIndex(events[i])] + " " + EventSummary(events[i]);
            }

            foreach (var kv in _eventHeads)
            {
                if (kv.Key < 0 || kv.Key >= events.Length)
                {
                    continue;
                }

                SetLabel(kv.Value.Name, EventTitle(events[kv.Key]));
                SetLabel(kv.Value.Summary, EventSummary(events[kv.Key]));
                if (kv.Value.Summary != null)
                {
                    kv.Value.Summary.tooltip = kv.Value.Summary.text;
                }
            }

            ApplyEventFlags();
            _eventList?.UpdateCount();
        }

        // 行の ⚠: Clip の長さを超えているイベント(Validator の Error と同じ条件)。
        private void ApplyEventFlags()
        {
            var events = Target?.Events;
            foreach (var kv in _eventFlags)
            {
                var has = events != null && kv.Key >= 0 && kv.Key < events.Length && EventOverLength(events[kv.Key]);
                kv.Value.Show(has);
                kv.Value.tooltip = has ? "イベントの時刻が Clip の長さを超えています(発火しません)" : string.Empty;
            }
        }

        private void SyncEventDetails()
        {
            for (var i = 0; i < _eventDetailSyncs.Count; i++)
            {
                _eventDetailSyncs[i]();
            }
        }

        // ── 1 行 ──

        private VisualElement MakeEventRow(int index)
        {
            var events = Target?.Events;
            if (events == null || index < 0 || index >= events.Length)
            {
                return new VisualElement();
            }

            var e = events[index];
            var open = index == _openEvent;
            var row = new VisualElement();
            row.AddToClassList("ux-fxrow");
            row.EnableInClassList("ux-fxrow--hl", index == _selectedEvent);
            row.EnableInClassList("ux-fxrow--open", open);

            var head = new VisualElement();
            head.AddToClassList("ux-fxrow__head");
            head.AddToClassList("ux-trk__head");
            var flag = new Label("⚠");
            flag.AddToClassList("ux-lineflag");
            flag.Show(false);
            _eventFlags[index] = flag;
            head.Add(flag);

            var idx = new Label("#" + (index + 1));
            idx.AddToClassList("ux-dim");
            idx.AddToClassList("ux-trk__idx");
            head.Add(idx);

            var kind = EventKindIndex(in e);
            var chip = new Label(EventKinds[kind]) { tooltip = "イベントの種類(PlayAsset の対象の種別。ほか = SetParam / SendMessage / Duck または対象未設定)" };
            chip.AddToClassList("ux-chip");
            chip.AddToClassList("ux-fxrow__chip");
            chip.AddToClassList(kind == 3 ? "ux-kind--5" : "ux-kind--" + kind); // ほか = 中立の色(赤はエラーに見える)
            head.Add(chip);

            var refs = new EventHead();
            refs.Name = new Label(EventTitle(in e)) { tooltip = "クリックでこのイベントを選ぶ(タイムラインのマーカーと共通)" };
            refs.Name.AddToClassList("ux-fxrow__name");
            refs.Name.RegisterCallback<ClickEvent>(_ => SelectEvent(index, false));
            head.Add(refs.Name);

            refs.Summary = new Label(EventSummary(in e));
            refs.Summary.AddToClassList("ux-dim");
            refs.Summary.AddToClassList("ux-fxrow__summary");
            refs.Summary.tooltip = refs.Summary.text;
            head.Add(refs.Summary);
            _eventHeads[index] = refs;

            var play = new Button(() => PreviewEvent(index)) { text = "▶", tooltip = "この SE / VFX だけ試聴・試し出し(シーン上の対象の位置に出る)" };
            play.AddToClassList("ux-btn");
            play.AddToClassList("ux-btn--icon");
            play.SetEnabled(e.Action == EventAction.PlayAsset && e.Target.IsAssigned);
            head.Add(play);

            var toggle = new Button(() => ToggleEventOpen(index)) { text = open ? "▾" : "⚙", tooltip = "このイベントの詳細を開く / 閉じる(Action・Trigger・時刻・Target・Repeat・専用エディタ・複製)" };
            toggle.AddToClassList("ux-btn");
            toggle.AddToClassList("ux-btn--icon");
            head.Add(toggle);

            var del = new Button(() => RemoveEvent(index)) { text = "✕", tooltip = "このイベントを削除する(Ctrl+Z で戻せる)" };
            del.AddToClassList("ux-btn");
            del.AddToClassList("ux-btn--icon");
            head.Add(del);
            row.Add(head);

            if (open)
            {
                row.Add(MakeEventDetails(index));
            }

            return row;
        }

        private void ToggleEventOpen(int index)
        {
            _openEvent = _openEvent == index ? -1 : index;
            if (_openEvent == index)
            {
                _selectedEvent = index;
            }

            ReRenderEventList();
        }

        // 選ぶ(タイムラインのマーカー・一覧の名前から共通)。open = その行を開く。
        private void SelectEvent(int index, bool open)
        {
            _selectedEvent = index;
            if (open)
            {
                _openEvent = index;
            }

            ReRenderEventList();
            _timelineContainer?.MarkDirtyRepaint();
        }

        private void ReRenderEventList()
        {
            _eventHeads.Clear();
            _eventFlags.Clear();
            _eventDetailSyncs.Clear();
            _eventList?.Render();
            ApplyEventFlags();
        }

        // ── 展開(その行だけ) ──

        private VisualElement MakeEventDetails(int index)
        {
            var d = Target;
            var e = d.Events[index];
            var box = new VisualElement();
            box.AddToClassList("ux-fxrow__details");

            // Action / Trigger
            var line = new VisualElement();
            line.AddToClassList("ux-fxrow__props");
            var action = new EnumField("Action", e.Action) { tooltip = "PlayAsset = SE / VFX / 配置セットを出す。ほかは値を送る / メッセージ / ダッキング" };
            action.AddToClassList("ux-fxrow__prop");
            action.RegisterValueChangedCallback(evt => EditEvent(index, "Set Event Action", ev =>
            {
                ev.Action = (EventAction)evt.newValue;
                return ev;
            }));
            line.Add(action);
            var trigger = new EnumField("Trigger", e.Trigger) { tooltip = "いつ発火するか。Frame = Clip のフレーム番号、Time = 秒。OnLoop などは周回・生成・破棄のとき" };
            trigger.AddToClassList("ux-fxrow__prop");
            trigger.RegisterValueChangedCallback(evt =>
            {
                var next = (EventTrigger)evt.newValue;
                EditEvent(index, "Set Event Trigger", ev =>
                {
                    var fps = d.FrameRate;
                    if (ev.Trigger == EventTrigger.Frame && next == EventTrigger.Time)
                    {
                        ev.Time /= fps;
                    }
                    else if (ev.Trigger == EventTrigger.Time && next == EventTrigger.Frame)
                    {
                        ev.Time = Mathf.Round(ev.Time * fps);
                    }

                    ev.Trigger = next;
                    return ev;
                });
            });
            line.Add(trigger);
            box.Add(line);

            // 時刻
            if (e.Trigger == EventTrigger.Frame || e.Trigger == EventTrigger.Time)
            {
                var when = new VisualElement();
                when.AddToClassList("ux-fxrow__props");
                if (e.Trigger == EventTrigger.Time)
                {
                    var sec = new FloatField("時刻(秒)") { value = e.Time };
                    sec.AddToClassList("ux-fxrow__prop");
                    sec.RegisterValueChangedCallback(evt => EditEvent(index, "Set Event Time", ev =>
                    {
                        ev.Time = Mathf.Max(0f, evt.newValue);
                        return ev;
                    }));
                    when.Add(sec);
                    var conv = new Label($"= F{e.Time * d.FrameRate:0.#}");
                    conv.AddToClassList("ux-dim");
                    when.Add(conv);
                    _eventDetailSyncs.Add(() =>
                    {
                        if (index < d.Events.Length && d.Events[index].Trigger == EventTrigger.Time)
                        {
                            if (!HasFocus(sec))
                            {
                                sec.SetValueWithoutNotify(d.Events[index].Time);
                            }

                            conv.text = $"= F{d.Events[index].Time * d.FrameRate:0.#}";
                        }
                    });
                }
                else
                {
                    var frame = new IntegerField("時刻(フレーム)") { value = Mathf.RoundToInt(e.Time) };
                    frame.AddToClassList("ux-fxrow__prop");
                    frame.RegisterValueChangedCallback(evt => EditEvent(index, "Set Event Frame", ev =>
                    {
                        ev.Time = Mathf.Max(0, evt.newValue);
                        return ev;
                    }));
                    when.Add(frame);
                    var conv = new Label($"F = {e.Time / d.FrameRate:0.00}s");
                    conv.AddToClassList("ux-dim");
                    when.Add(conv);
                    _eventDetailSyncs.Add(() =>
                    {
                        if (index < d.Events.Length && d.Events[index].Trigger == EventTrigger.Frame)
                        {
                            if (!HasFocus(frame))
                            {
                                frame.SetValueWithoutNotify(Mathf.RoundToInt(d.Events[index].Time));
                            }

                            conv.text = $"F = {d.Events[index].Time / d.FrameRate:0.00}s";
                        }
                    });
                }

                box.Add(when);
            }

            if (e.Trigger == EventTrigger.Custom)
            {
                var key = EventProp(index, "CustomKey", "CustomKey");
                if (key != null)
                {
                    var keyLine = new VisualElement();
                    keyLine.AddToClassList("ux-fxrow__props");
                    keyLine.Add(key);
                    box.Add(keyLine);
                }
            }

            // Target / Repeat / Param
            var what = new VisualElement();
            what.AddToClassList("ux-fxrow__props");
            if (e.Action == EventAction.PlayAsset)
            {
                var labels = new List<string> { "(未設定)" };
                foreach (var c in _assetChoices)
                {
                    labels.Add(c.Label);
                }

                var dropdown = new DropdownField("Target", labels, LabelIndexFor(e.Target)) { tooltip = "出す SE / VFX / 配置セット" };
                dropdown.AddToClassList("ux-fxrow__prop");
                dropdown.RegisterValueChangedCallback(evt =>
                {
                    var choiceIndex = dropdown.index - 1;
                    EditEvent(index, "Set Event Target", ev =>
                    {
                        ev.Target = choiceIndex >= 0 && choiceIndex < _assetChoices.Count ? ToRef(_assetChoices[choiceIndex]) : default;
                        return ev;
                    });
                });
                what.Add(dropdown);
            }
            else
            {
                AddIfNotNull(what, EventProp(index, "Target", "Target"));
                AddIfNotNull(what, EventProp(index, "Param", "Param"));
            }

            if (e.Trigger == EventTrigger.Frame || e.Trigger == EventTrigger.Time)
            {
                var repeat = new DropdownField("繰り返し", RepeatLabels, Mathf.Clamp((int)e.Repeat, 0, RepeatLabels.Count - 1))
                {
                    tooltip = "毎周回 = ループのたびに出す / 1 回 = 再生ごとに 1 回 / 再生中は維持 = 1 回出して終了・中断で止める(ダッシュの土煙などループする追従エフェクト向け)",
                };
                repeat.AddToClassList("ux-fxrow__prop");
                repeat.RegisterValueChangedCallback(evt =>
                {
                    var next = (EventRepeat)Mathf.Max(0, repeat.index);
                    EditEvent(index, "Set Event Repeat", ev =>
                    {
                        ev.Repeat = next;
                        return ev;
                    });
                });
                what.Add(repeat);
            }

            box.Add(what);

            // ツール
            var tools = new VisualElement();
            tools.AddToClassList("ux-toolsline");
            var openEditor = MakeButton("専用エディタで開く", null, () => OpenEventAssetEditor(index), DescribeEditorFor(e.Target));
            openEditor.SetEnabled(e.Action == EventAction.PlayAsset && e.Target.IsAssigned);
            tools.Add(openEditor);
            tools.Add(MakeButton("複製", null, () => DuplicateEvent(index), "このイベントを直後に複製する"));
            box.Add(tools);
            return box;
        }

        private PropertyField EventProp(int index, string field, string label)
        {
            var prop = So?.FindProperty($"Events.Array.data[{index}].{field}");
            if (prop == null)
            {
                return null;
            }

            var pf = new PropertyField(prop, label);
            pf.AddToClassList("ux-fxrow__prop");
            pf.BindProperty(prop);
            return pf;
        }

        // 入力中の欄を外からの同期で上書きしない。
        private static bool HasFocus(VisualElement element) =>
            element.focusController?.focusedElement is VisualElement f && (f == element || element.Contains(f));

        private static void AddIfNotNull(VisualElement parent, VisualElement child)
        {
            if (child != null)
            {
                parent.Add(child);
            }
        }

        private int LabelIndexFor(AssetRef target)
        {
            if (!target.IsAssigned)
            {
                return 0;
            }

            for (var i = 0; i < _assetChoices.Count; i++)
            {
                if (_assetChoices[i].Type == target.Type && _assetChoices[i].Asset.Id == target.Id)
                {
                    return i + 1;
                }
            }

            return 0;
        }

        // ── イベントの操作(すべて Undo 1 回) ──

        private void EditEvent(int index, string undoName, Func<AssetEvent, AssetEvent> change)
        {
            var d = Target;
            if (d?.Events == null || index < 0 || index >= d.Events.Length)
            {
                return;
            }

            Undo.RecordObject(d, undoName);
            d.Events[index] = change(d.Events[index]);
            EditorUtility.SetDirty(d);
            So?.Update();
            AfterDataWrite();
        }

        private void AddEventAtPlayhead(AssetType type)
        {
            var d = Target;
            if (d == null)
            {
                return;
            }

            EnsureAssetChoices();
            var normalized = _scene != null ? _scene.Manager.GetNormalizedTime(_animHandle) : -1f;
            if (normalized < 0f)
            {
                normalized = 0f;
            }

            AssetChoice first = null;
            foreach (var c in _assetChoices)
            {
                if (c.Type == type)
                {
                    first = c;
                    break;
                }
            }

            var ev = new AssetEvent
            {
                Trigger = EventTrigger.Frame,
                Time = Mathf.Round(normalized * d.LengthSec * d.FrameRate),
                Action = EventAction.PlayAsset,
                Target = first != null ? ToRef(first) : default,
            };

            Undo.RecordObject(d, "Add Anim Event");
            var list = new List<AssetEvent>(d.Events ?? Array.Empty<AssetEvent>()) { ev };
            d.Events = list.ToArray();
            EditorUtility.SetDirty(d);
            So?.Update();
            _selectedEvent = d.Events.Length - 1;
            _openEvent = _selectedEvent;
            AfterDataWrite();
            ReRenderEventList();
        }

        private void DuplicateEvent(int index)
        {
            var d = Target;
            if (d?.Events == null || index < 0 || index >= d.Events.Length)
            {
                return;
            }

            Undo.RecordObject(d, "Duplicate Anim Event");
            var list = new List<AssetEvent>(d.Events);
            list.Insert(index + 1, list[index]);
            d.Events = list.ToArray();
            EditorUtility.SetDirty(d);
            So?.Update();
            _selectedEvent = index + 1;
            _openEvent = _selectedEvent;
            AfterDataWrite();
            ReRenderEventList();
        }

        private void RemoveEvent(int index)
        {
            var d = Target;
            if (d?.Events == null || index < 0 || index >= d.Events.Length)
            {
                return;
            }

            Undo.RecordObject(d, "Remove Anim Event");
            var list = new List<AssetEvent>(d.Events);
            list.RemoveAt(index);
            d.Events = list.ToArray();
            EditorUtility.SetDirty(d);
            So?.Update();
            _selectedEvent = -1;
            _openEvent = -1;
            AfterDataWrite();
            ReRenderEventList();
        }

        private void PreviewEvent(int index)
        {
            var events = Target?.Events;
            if (_scene == null || events == null || index < 0 || index >= events.Length || !events[index].Target.IsAssigned)
            {
                return;
            }

            var e = events[index];
            EnsureAssetChoices();
            foreach (var c in _assetChoices)
            {
                if (c.Type != e.Target.Type || c.Asset.Id != e.Target.Id)
                {
                    continue;
                }

                switch (c.Asset)
                {
                    case SeData se:
                        _scene.PreviewSe(se);
                        break;
                    case VfxData vfx:
                        _scene.PreviewVfx(vfx);
                        break;
                    case AnchorGroupData group:
                        _scene.PreviewGroup(group);
                        break;
                }

                AppendLog($"▶ 試聴: {c.Label}");
                return;
            }

            AppendLog("⚠ 対象のアセットが見つかりません(「一覧を更新」を押してください)");
        }

        // 専用エディタで開く(DataEditorRegistry の対応表。新エディターが第 1 候補)。
        private void OpenEventAssetEditor(int index)
        {
            var events = Target?.Events;
            if (events == null || index < 0 || index >= events.Length)
            {
                return;
            }

            var asset = FindAsset(events[index].Target);
            if (asset == null)
            {
                AppendLog("⚠ 対象のアセットが見つかりません(「一覧を更新」を押してください)");
                return;
            }

            if (!DataEditorRegistry.OpenDefault(asset))
            {
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset;
            }
        }

        private string DescribeEditorFor(AssetRef target)
        {
            var asset = FindAsset(target);
            if (asset == null)
            {
                return "アセット未設定";
            }

            var entries = DataEditorRegistry.GetEntries(asset.GetType());
            return entries.Count > 0 ? $"'{asset.DisplayName ?? asset.name}' を {entries[0].WindowType.Name} で開く" : $"'{asset.name}' を Project で選択";
        }

        // ── コピー ──

        private void CopyEventsToField()
        {
            var dest = _copyTargetField?.value as AnimData;
            if (Target == null || dest == null || dest == Target)
            {
                AppendLog("⚠ コピー先の AnimData を選んでください(対象自身は不可)");
                return;
            }

            CopyEvents(Target, dest);
            AppendLog($"イベントを '{dest.DisplayName ?? dest.name}' へコピー({Target.Events?.Length ?? 0} 件)");
        }

        private static void CopyEvents(AnimData from, AnimData to)
        {
            Undo.RecordObject(to, "Copy Anim Events");
            to.Events = from.Events != null ? (AssetEvent[])from.Events.Clone() : Array.Empty<AssetEvent>();
            EditorUtility.SetDirty(to);
            AssetDatabase.SaveAssetIfDirty(to);
        }

        // ── タイムライン(シーク + イベントマーカーのドラッグ + SE の波形) ──

        private void DrawTimeline()
        {
            var rect = GUILayoutUtility.GetRect(100, TimelineHeight, GUILayout.ExpandWidth(true));
            var pro = EditorGUIUtility.isProSkin;
            EditorGUI.DrawRect(rect, pro ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.89f, 0.9f, 0.93f)); // Light / Dark に合わせる(旧は暗い背景のみ)
            var target = Target;
            if (target == null)
            {
                GUI.Label(rect, "対象アセットが未選択です", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var length = target.LengthSec;
            var frameRate = target.FrameRate;

            // 目盛り: フレームごと(細)+ ラベル付き(太)。ラベル間隔は幅に応じて間引く。
            var totalFrames = Mathf.Max(1, Mathf.RoundToInt(length * frameRate));
            var bar = new Rect(rect.x + SeekBarGui.DefaultMarginX, rect.y + SeekBarGui.DefaultBarTop, rect.width - SeekBarGui.DefaultMarginX * 2f, SeekBarGui.DefaultBarHeight);
            EditorGUI.DrawRect(bar, pro ? new Color(0.3f, 0.3f, 0.3f) : new Color(0.74f, 0.76f, 0.82f));
            DrawTimelineTicks(bar, totalFrames, pro);

            GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, rect.width - 12f, 14f),
                $"0s  —  {length:0.##}s ({length * frameRate:0} フレーム @ {frameRate:0}fps)",
                EditorStyles.miniLabel);

            // SE 波形(マーカー位置から SE の長さぶん)
            DrawSeWaveforms(bar, length);

            // イベントマーカー
            var events = target.Events;
            var evt = Event.current;
            _timelineLabels.Clear();
            if (events != null)
            {
                for (var i = 0; i < events.Length; i++)
                {
                    var e = events[i];
                    var sec = EventSeconds(in e);
                    if (sec < 0f)
                    {
                        continue;
                    }

                    var x = bar.x + bar.width * Mathf.Clamp01(sec / length);
                    var marker = new Rect(x - 5f, bar.y - 6f, 10f, bar.height + 12f);
                    var over = sec > length + 1e-3f;
                    var color = over ? new Color(1f, 0.3f, 0.3f) : (e.Action == EventAction.PlayAsset ? new Color(1f, 0.8f, 0.3f) : new Color(0.6f, 0.8f, 1f));
                    EditorGUI.DrawRect(marker, color);
                    if (i == _selectedEvent)
                    {
                        EditorGUI.DrawRect(new Rect(marker.x - 1f, marker.y - 1f, marker.width + 2f, 2f), Color.white);
                    }

                    // 時刻 + 対象名(SE / VFX)をマーカー直下に出す
                    var asset = e.Action == EventAction.PlayAsset ? FindAsset(e.Target) : null;
                    var timeText = e.Trigger == EventTrigger.Frame ? $"F{e.Time:0}" : $"{e.Time:0.##}s";
                    var nameText = asset != null ? (asset.DisplayName ?? asset.name) : null;
                    _timelineLabels.Add((x, nameText != null ? $"{timeText} {nameText}" : timeText));

                    if (evt.type == EventType.MouseDown && marker.Contains(evt.mousePosition))
                    {
                        _draggingEvent = i;
                        _dragUndoGroup = Undo.GetCurrentGroup();
                        var picked = i;
                        _timelineContainer.schedule.Execute(() => SelectEvent(picked, false));
                        evt.Use();
                    }
                }
            }

            // 時刻 + 対象名は、前のラベルと重なるときは省く(イベントが多いときに文字が潰れないように。詳細は一覧の行で見る)。
            _timelineLabels.Sort((a, b) => a.x.CompareTo(b.x));
            var lastRight = float.MinValue;
            foreach (var (lx, text) in _timelineLabels)
            {
                if (lx - 40f < lastRight)
                {
                    continue;
                }

                GUI.Label(new Rect(lx - 40f, bar.y + 10f, 80f, 14f), text, MarkerLabelStyle());
                lastRight = lx + 40f;
            }

            // 再生ヘッド
            var anim = _scene?.Manager;
            var normalized = anim != null ? anim.GetNormalizedTime(_animHandle) : -1f;
            if (normalized >= 0f)
            {
                var px = bar.x + bar.width * Mathf.Clamp01(normalized);
                EditorGUI.DrawRect(new Rect(px - 1f, bar.y - 8f, 2f, bar.height + 16f), pro ? Color.white : new Color(0.1f, 0.1f, 0.18f));
            }

            switch (evt.type)
            {
                case EventType.MouseDrag when _draggingEvent >= 0 && events != null && _draggingEvent < events.Length:
                {
                    // MouseDown だけの RecordObject は変更前に記録が終わって Undo が効かないため、ドラッグごとに記録し MouseUp で 1 つにまとめる。
                    Undo.RecordObject(target, "Move Anim Event");
                    var sec = Mathf.Clamp((evt.mousePosition.x - bar.x) / bar.width, 0f, 1f) * length;
                    var e = events[_draggingEvent];
                    e.Time = e.Trigger == EventTrigger.Frame ? Mathf.Round(sec * frameRate) : Mathf.Round(sec * 100f) / 100f;
                    events[_draggingEvent] = e;
                    EditorUtility.SetDirty(target);
                    So?.Update();
                    RefreshEventSummaries();
                    SyncEventDetails();
                    evt.Use();
                    break;
                }
                case EventType.MouseUp when _draggingEvent >= 0:
                    _draggingEvent = -1;
                    Undo.CollapseUndoOperations(_dragUndoGroup);
                    _timelineContainer.schedule.Execute(AfterDataWrite);
                    evt.Use();
                    break;
                case EventType.MouseDown when SeekBarGui.TryHandleClickSeek(rect, bar, evt, out var t):
                {
                    // 空いている場所のクリックはシーク(再生中でなければ再生を開始してその位置へ)。
                    var animator = EnsureSceneTarget();
                    if (anim != null && animator != null)
                    {
                        if (!anim.IsPlaying(_animHandle))
                        {
                            _animHandle = _scene.Play(target, animator, 0f);
                        }

                        // クリックした瞬間で止める(ポーズはその位置に更新される)。続きは ▶ か ⏸。
                        _scene.SetPaused(_animHandle, true);
                        _scene.Seek(_animHandle, t);
                    }

                    evt.Use();
                    break;
                }
            }
        }

        private static GUIStyle _markerLabel;
        private static bool _markerLabelPro;

        // マーカーの下の時刻 + 対象名(Light では濃い色にする)。
        private static GUIStyle MarkerLabelStyle()
        {
            var pro = EditorGUIUtility.isProSkin;
            if (_markerLabel == null || _markerLabelPro != pro)
            {
                _markerLabelPro = pro;
                _markerLabel = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
                _markerLabel.normal.textColor = pro ? new Color(0.78f, 0.78f, 0.8f) : new Color(0.15f, 0.17f, 0.25f);
            }

            return _markerLabel;
        }

        // フレーム目盛り: フレームごと(細)+ ラベル付き(太)。ラベル間隔は幅に応じて間引く(TimelineRulerGui と同じ間引き。色だけ Light / Dark に合わせる)。
        private static void DrawTimelineTicks(Rect bar, int totalFrames, bool pro)
        {
            var labelEvery = Mathf.Max(1, Mathf.CeilToInt(totalFrames / Mathf.Max(1f, bar.width / 40f)));
            var tickEvery = Mathf.Max(1, Mathf.CeilToInt(totalFrames / Mathf.Max(1f, bar.width / 2f)));
            for (var f = 0; f <= totalFrames; f++)
            {
                var labeled = f % labelEvery == 0 || f == totalFrames;
                if (!labeled && f % tickEvery != 0)
                {
                    continue;
                }

                var x = bar.x + bar.width * (f / (float)totalFrames);
                var c = pro ? new Color(1f, 1f, 1f, labeled ? 0.35f : 0.12f) : new Color(0f, 0f, 0f, labeled ? 0.4f : 0.15f);
                EditorGUI.DrawRect(new Rect(x, bar.y - (labeled ? 4f : 2f), 1f, bar.height + (labeled ? 8f : 4f)), c);
                if (labeled)
                {
                    GUI.Label(new Rect(x - 14f, bar.yMax + 22f, 28f, 12f), f.ToString(), MarkerLabelStyle());
                }
            }
        }

        // 各 SE マーカーの位置から、その SE の長さぶん波形を半透明で重ねる(どこまで鳴るか・重なりが見える)。
        private void DrawSeWaveforms(Rect bar, float length)
        {
            if (Target?.Events == null || length <= 0f)
            {
                return;
            }

            EnsureAssetChoices();
            foreach (var e in Target.Events)
            {
                if (e.Action != EventAction.PlayAsset || !e.Target.IsAssigned || e.Target.Type != AssetType.Se)
                {
                    continue;
                }

                var sec = EventSeconds(in e);
                if (sec < 0f || !_seById.TryGetValue(e.Target.Id, out var se) || se == null)
                {
                    continue;
                }

                var clip = FirstClip(se);
                var wave = WaveformTextureCache.Get(clip);
                if (clip == null || wave == null)
                {
                    continue;
                }

                var startU = Mathf.Clamp01(sec / length);
                var endU = Mathf.Clamp01((sec + clip.length) / length);
                var rect = new Rect(bar.x + bar.width * startU, bar.y - 10f, Mathf.Max(2f, bar.width * (endU - startU)), 28f);
                var prev = GUI.color;
                GUI.color = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.55f) : new Color(0.2f, 0.25f, 0.4f, 0.6f);
                GUI.DrawTexture(rect, wave, ScaleMode.StretchToFill, true);
                GUI.color = prev;
            }
        }

        private static AudioClip FirstClip(SeData se)
        {
            if (se.Clips != null)
            {
                foreach (var c in se.Clips)
                {
                    if (c != null)
                    {
                        return c;
                    }
                }
            }

            if (se.Sources != null)
            {
                foreach (var s in se.Sources)
                {
                    if (s.Source != null)
                    {
                        return s.Source;
                    }
                }
            }

            return null;
        }
    }
}
