using System;
using System.Collections.Generic;
using System.Text;
using DDrive.Editor.Anim;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.CanvasTool;
using DDrive.Editor.Ux;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Anim2D;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anim2D
{
    // 一覧の欄(docs/1010 UX-7 / D-10): コマ(1 コマ = 1 行)・方向ごとのクリップ(1 方向 = 1 行)・イベントの要約(1 イベント = 1 行)。
    // いずれも CanvasItemList(絞り込み・件数・20 行超は ListView)に載せ、AnimSyncBox(署名が変わったときだけ作り直す)で包む。
    internal sealed partial class Anim2DEditor
    {
        private static readonly string[] EventKinds = { "SE", "VFX", "配置セット", "ほか" };
        private static readonly string[] RepeatLabels = { "毎周回", "1 回", "再生中は維持" };

        private Sprite[] _frames = Array.Empty<Sprite>();
        private float[] _frameTimes = Array.Empty<float>();
        private int _framesVersion;
        private CanvasItemList _frameList;
        private AnimSyncBox _frameSection;
        private CanvasItemList _dirList;
        private AnimSyncBox _dirSection;
        private CanvasItemList _eventList;
        private AnimSyncBox _eventSection;
        private Dictionary<(AssetType, ulong), string> _eventNames;

        // ── コマ ──

        // Clip のスプライトのキーを読み直す(読めなければ空)。
        private void ReloadFrames()
        {
            _framesVersion++;
            var clip = Target != null ? Target.Clip : null;
            if (clip == null || !AnimationClipEditorUtility.LoadSprites(clip, out var sprites, out var times) || sprites == null)
            {
                _frames = Array.Empty<Sprite>();
                _frameTimes = Array.Empty<float>();
                return;
            }

            _frames = sprites;
            _frameTimes = times ?? new float[sprites.Length];
        }

        private UxRow CreateFramesRow(FieldGuideEntry e)
        {
            _frameList = new CanvasItemList("コマ", "絵あり", null, "Clip を設定すると、スプライトのコマがここに並びます。", "絵なし");
            var box = new AnimSyncBox(FramesSignature, RebuildFrames, null);
            box.Add(_frameList);
            _frameSection = box;
            ReloadFrames();
            box.Rebuild();
            var row = new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, box.Sync);
            row.ExtraMatch = q => _frameList != null && _frameList.AnyMatch(q);
            return row;
        }

        private int FramesSignature()
        {
            var clip = Target != null ? Target.Clip : null;
            return CanvasSig.Add(CanvasSig.Add(17, clip != null ? clip.GetInstanceID() : 0), _framesVersion);
        }

        private void RebuildFrames()
        {
            if (_frameList == null)
            {
                return;
            }

            var items = new List<CanvasListItem>(_frames.Length);
            for (var i = 0; i < _frames.Length; i++)
            {
                var index = i;
                var sprite = _frames[i];
                items.Add(new CanvasListItem
                {
                    Key = "frame:" + i,
                    Title = sprite != null ? sprite.name : "(Sprite なし)",
                    Configured = sprite != null,
                    Search = FrameSummary(index),
                    Make = () => MakeFrameRow(index),
                });
            }

            _frameList.SetItems(items);
        }

        private string FrameSummary(int index)
        {
            var clip = Target != null ? Target.Clip : null;
            var fps = clip != null && clip.frameRate > 0f ? clip.frameRate : 30f;
            var t = index < _frameTimes.Length ? _frameTimes[index] : 0f;
            var end = index + 1 < _frameTimes.Length ? _frameTimes[index + 1] : (clip != null ? clip.length : t);
            return $"{t:0.000}s · F{Mathf.RoundToInt(t * fps)} · 次まで {Mathf.Max(0f, end - t):0.000}s";
        }

        private VisualElement MakeFrameRow(int index)
        {
            var sprite = index < _frames.Length ? _frames[index] : null;
            var row = new VisualElement();
            row.AddToClassList("ux-fxrow");
            var head = new VisualElement();
            head.AddToClassList("ux-fxrow__head");
            head.AddToClassList("ux-trk__head");
            if (sprite == null)
            {
                var flag = new Label("⚠") { tooltip = "Sprite の参照が切れています" };
                flag.AddToClassList("ux-lineflag");
                head.Add(flag);
            }

            var idx = new Label("#" + (index + 1));
            idx.AddToClassList("ux-dim");
            idx.AddToClassList("ux-trk__idx");
            head.Add(idx);

            var thumb = new Image { sprite = sprite, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            thumb.AddToClassList("ux-a2d-thumb");
            head.Add(thumb);

            var name = new Label(sprite != null ? sprite.name : "(Sprite なし)") { tooltip = sprite != null ? sprite.name : string.Empty };
            name.AddToClassList("ux-fxrow__name");
            head.Add(name);

            var summary = new Label(FrameSummary(index));
            summary.AddToClassList("ux-dim");
            summary.AddToClassList("ux-fxrow__summary");
            summary.tooltip = summary.text;
            head.Add(summary);

            if (sprite != null)
            {
                var ping = new Button(() => EditorGUIUtility.PingObject(sprite)) { text = "Project", tooltip = "このスプライトを Project ウィンドウで表示する" };
                ping.AddToClassList("ux-btn");
                head.Add(ping);
            }

            row.Add(head);
            return row;
        }

        // ── リタイミングの適用 ──

        private Label _retimeStatus;

        private UxRow CreateRetimeRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            var mode = new EnumField("配置モード", _placement) { tooltip = "Uniform = 等間隔 / Retiming = 上の「リタイミングのカーブ」で配置" };
            mode.AddToClassList("ux-a2d-modefield");
            mode.RegisterValueChangedCallback(evt => _placement = (PlacementMode)evt.newValue);
            line.Add(mode);
            line.Add(MakeButton("適用(Clip に焼き込む)", "ux-btn--primary", ApplyRetiming, "Clip と、枚数が同じ方向 Clip のスプライトの時刻を書き換える(Ctrl+Z で戻せる)"));
            box.Add(line);
            _retimeStatus = new Label(string.Empty);
            _retimeStatus.AddToClassList("ux-dim");
            _retimeStatus.AddToClassList("ux-note");
            box.Add(_retimeStatus);
            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        // ── 方向ごとのクリップ ──

        private UxRow CreateDirectionRows(FieldGuideEntry e)
        {
            _dirList = new CanvasItemList("方向", "クリップあり", null, "方向セットを Four / Eight にすると、その数の行がここに出ます。", "未設定");
            var box = new AnimSyncBox(DirectionSignature, RebuildDirections, null);

            var bar = new VisualElement();
            bar.AddToClassList("ux-toolsline");
            bar.Add(MakeButton("方向数に合わせて枠を作る", null, EnsureDirectionSlots, "方向セットの数(4 / 8)だけ DirectionClips の枠を用意する(足りない分だけ。既に入っている物は動かさない)"));
            box.Add(bar);
            box.Add(_dirList);
            _dirSection = box;
            box.Rebuild();
            return new UxVisualRow(So, DefaultSo, new[] { e }, e.Label, e.Hint, e.Tier, box, RowChanged)
            {
                NoFold = true,
                NoBulkReset = true,
                MaxIssues = 3,
                ExtraMatch = q => _dirList != null && _dirList.AnyMatch(q),
            };
        }

        private int DirectionSignature()
        {
            var t = Target;
            if (t == null)
            {
                return 0;
            }

            var h = CanvasSig.Add(17, (int)t.Directions);
            var clips = t.DirectionClips;
            h = CanvasSig.Add(h, clips != null ? clips.Length : 0);
            for (var i = 0; clips != null && i < clips.Length; i++)
            {
                h = CanvasSig.Add(h, clips[i] != null ? clips[i].GetInstanceID() : 0);
            }

            return h;
        }

        private int[] CurrentAngles()
        {
            switch (Target != null ? Target.Directions : DirectionSet.None)
            {
                case DirectionSet.Four: return DirectionAngle.FourAngles;
                case DirectionSet.Eight: return DirectionAngle.EightAngles;
                default: return Array.Empty<int>();
            }
        }

        private void RebuildDirections()
        {
            if (_dirList == null)
            {
                return;
            }

            var angles = CurrentAngles();
            var clips = Target != null ? Target.DirectionClips : null;
            var count = Mathf.Max(angles.Length, clips != null ? clips.Length : 0);
            var items = new List<CanvasListItem>(count);
            for (var i = 0; i < count; i++)
            {
                var index = i;
                var angle = i < angles.Length ? angles[i] : -1;
                var clip = clips != null && i < clips.Length ? clips[i] : null;
                items.Add(new CanvasListItem
                {
                    Key = "dir:" + i,
                    Title = angle >= 0 ? angle + "°" : "(余り)",
                    Configured = clip != null,
                    Search = (clip != null ? clip.name : string.Empty) + " " + (angle >= 0 ? angle + "度" : string.Empty),
                    Make = () => MakeDirectionRow(index, angle),
                });
            }

            _dirList.SetItems(items);
        }

        private VisualElement MakeDirectionRow(int index, int angle)
        {
            var clips = Target != null ? Target.DirectionClips : null;
            var clip = clips != null && index < clips.Length ? clips[index] : null;
            var row = new VisualElement();
            row.AddToClassList("ux-fxrow");
            var head = new VisualElement();
            head.AddToClassList("ux-fxrow__head");
            head.AddToClassList("ux-trk__head");
            if (clip == null)
            {
                var flag = new Label("⚠") { tooltip = "このスロットのクリップが未設定(または Missing)です" };
                flag.AddToClassList("ux-lineflag");
                head.Add(flag);
            }

            var idx = new Label("#" + (index + 1));
            idx.AddToClassList("ux-dim");
            idx.AddToClassList("ux-trk__idx");
            head.Add(idx);

            var chip = new Label(angle >= 0 ? angle + "°" : "余り") { tooltip = angle >= 0 ? "この方向(角度)" : "方向セットの数を超えたスロット(使われません)" };
            chip.AddToClassList("ux-chip");
            chip.AddToClassList("ux-fxrow__chip");
            chip.AddToClassList("ux-a2d-angle");
            chip.AddToClassList(angle >= 0 ? "ux-kind--0" : "ux-kind--5");
            head.Add(chip);

            var field = new ObjectField { objectType = typeof(AnimationClip), value = clip, allowSceneObjects = false };
            field.AddToClassList("ux-a2d-clipfield");
            field.RegisterValueChangedCallback(evt => SetDirectionClip(index, evt.newValue as AnimationClip));
            head.Add(field);

            var summary = new Label(ClipSummary(clip));
            summary.AddToClassList("ux-dim");
            summary.AddToClassList("ux-fxrow__summary");
            summary.tooltip = summary.text;
            head.Add(summary);
            row.Add(head);
            return row;
        }

        private static string ClipSummary(AnimationClip clip)
        {
            if (clip == null)
            {
                return "(未設定)";
            }

            var n = AnimationClipEditorUtility.LoadSprites(clip, out var sprites, out _) && sprites != null ? sprites.Length : 0;
            return $"{n} 枚 · {clip.length:0.###}s · {clip.frameRate:0.#}fps";
        }

        private void SetDirectionClip(int index, AnimationClip clip)
        {
            if (Target == null || So == null)
            {
                return;
            }

            So.Update();
            var prop = So.FindProperty("DirectionClips");
            if (prop == null)
            {
                return;
            }

            if (prop.arraySize <= index)
            {
                prop.arraySize = index + 1;
            }

            prop.GetArrayElementAtIndex(index).objectReferenceValue = clip;
            So.ApplyModifiedProperties();
            AfterDataWrite();
        }

        private void EnsureDirectionSlots()
        {
            if (Target == null || So == null)
            {
                return;
            }

            var need = Target.RequiredDirectionClips;
            So.Update();
            var prop = So.FindProperty("DirectionClips");
            if (prop == null || need <= 0 || prop.arraySize >= need)
            {
                AppendLog(need <= 0 ? "⚠ 方向セットが None です(Four / Eight にすると枠を作れます)" : "枠は足りています");
                return;
            }

            prop.arraySize = need;
            So.ApplyModifiedProperties();
            AppendLog($"DirectionClips の枠を {need} 個にしました");
            AfterDataWrite();
        }

        // ── イベントの要約 ──

        private UxRow CreateEventRows(FieldGuideEntry e)
        {
            _eventList = new CanvasItemList("イベント", "対象あり", EventKinds, "イベントがありません。「Anim Editor で編集」から追加できます。", "対象なし");
            var box = new AnimSyncBox(EventSignature, RebuildEventList, null);

            var bar = new VisualElement();
            bar.AddToClassList("ux-toolsline");
            bar.Add(MakeButton("Anim Editor で編集", "ux-btn--primary", OpenAnimEditorForEvents,
                "タイムライン上でマーカーを動かす・SE / VFX / 配置セットを同時に再生する(Anim Editor で Anim2D のプレビュー物を動かす)"));
            bar.Add(MakeButton("一覧を更新", null, () =>
            {
                _eventNames = null;
                RebuildEventList();
            }, "SE / VFX アセットを追加・改名したあとに押す"));
            box.Add(bar);
            box.Add(_eventList);
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

        private int EventSignature()
        {
            var t = Target;
            if (t == null)
            {
                return 0;
            }

            var events = t.Events;
            var h = CanvasSig.Add(17, events != null ? events.Length : 0);
            h = CanvasSig.Add(h, t.Clip != null ? t.Clip.GetInstanceID() : 0);
            h = CanvasSig.Add(h, _eventNames == null ? 1 : 0);
            for (var i = 0; events != null && i < events.Length; i++)
            {
                h = CanvasSig.Add(h, (int)events[i].Trigger);
                h = CanvasSig.Add(h, (int)events[i].Action);
                h = CanvasSig.Add(h, (int)events[i].Repeat);
                h = CanvasSig.Add(h, Mathf.RoundToInt(events[i].Time * 1000f));
                h = CanvasSig.Add(h, events[i].Target.Id);
                h = CanvasSig.Add(h, (int)events[i].Target.Type);
            }

            return h;
        }

        private void RebuildEventList()
        {
            if (_eventList == null)
            {
                return;
            }

            var items = new List<CanvasListItem>();
            var events = Target != null ? Target.Events : null;
            for (var i = 0; events != null && i < events.Length; i++)
            {
                var index = i;
                var ev = events[i];
                var kind = EventKindIndex(in ev);
                items.Add(new CanvasListItem
                {
                    Key = "evt:" + i,
                    Title = EventTitle(in ev),
                    Chip = EventKinds[kind],
                    Kind = kind,
                    Configured = ev.Action != EventAction.PlayAsset || ev.Target.IsAssigned,
                    Search = EventKinds[kind] + " " + EventSummary(in ev),
                    Make = () => MakeEventRow(index),
                });
            }

            _eventList.SetItems(items);
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

            EnsureEventNames();
            return _eventNames.TryGetValue((e.Target.Type, e.Target.Id), out var name) ? name : $"(Id 0x{e.Target.Id:X} が見つかりません)";
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
                sb.Append(" · ").Append(RepeatLabels[Mathf.Clamp((int)e.Repeat, 0, RepeatLabels.Length - 1)]);
            }

            if (e.Action != EventAction.PlayAsset)
            {
                sb.Append(" · ").Append(e.Action);
            }

            return sb.ToString();
        }

        private bool EventOverLength(in AssetEvent e)
        {
            if (Target == null || Target.Clip == null)
            {
                return false;
            }

            var fps = Target.FrameRate;
            var sec = e.Trigger == EventTrigger.Frame ? e.Time / fps : e.Trigger == EventTrigger.Time ? e.Time : -1f;
            return sec > Target.LengthSec + 1e-3f;
        }

        private VisualElement MakeEventRow(int index)
        {
            var events = Target != null ? Target.Events : null;
            if (events == null || index < 0 || index >= events.Length)
            {
                return new VisualElement();
            }

            var ev = events[index];
            var row = new VisualElement();
            row.AddToClassList("ux-fxrow");
            var head = new VisualElement();
            head.AddToClassList("ux-fxrow__head");
            head.AddToClassList("ux-trk__head");
            if (EventOverLength(in ev))
            {
                var flag = new Label("⚠") { tooltip = "イベントの時刻が Clip の長さを超えています(発火しません)" };
                flag.AddToClassList("ux-lineflag");
                head.Add(flag);
            }

            var idx = new Label("#" + (index + 1));
            idx.AddToClassList("ux-dim");
            idx.AddToClassList("ux-trk__idx");
            head.Add(idx);

            var kind = EventKindIndex(in ev);
            var chip = new Label(EventKinds[kind]) { tooltip = "イベントの種類(PlayAsset の対象の種別。ほか = SetParam / SendMessage / Duck または対象未設定)" };
            chip.AddToClassList("ux-chip");
            chip.AddToClassList("ux-fxrow__chip");
            chip.AddToClassList(kind == 3 ? "ux-kind--5" : "ux-kind--" + kind);
            head.Add(chip);

            var name = new Label(EventTitle(in ev));
            name.AddToClassList("ux-fxrow__name");
            name.tooltip = name.text;
            head.Add(name);

            var summary = new Label(EventSummary(in ev));
            summary.AddToClassList("ux-dim");
            summary.AddToClassList("ux-fxrow__summary");
            summary.tooltip = summary.text;
            head.Add(summary);
            row.Add(head);
            return row;
        }

        // ── 参照先アセットの名前(SE / VFX / 配置セット。プロジェクト検索は重いのでキャッシュ。プロジェクトの変更で捨てる) ──

        private void EnsureEventNames()
        {
            if (_eventNames != null)
            {
                return;
            }

            _eventNames = new Dictionary<(AssetType, ulong), string>();
            CollectNames<SeData>(AssetType.Se);
            CollectNames<VfxData>(AssetType.Vfx);
            CollectNames<AnchorGroupData>(AssetType.AnchorGroup);
        }

        private void CollectNames<T>(AssetType type) where T : AssetDataBase
        {
            foreach (var guid in AssetSearch.FindAssets("t:" + typeof(T).Name))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Tests/"))
                {
                    continue;
                }

                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null && asset.Id != 0)
                {
                    _eventNames[(type, asset.Id)] = asset.DisplayName ?? asset.name;
                }
            }
        }
    }
}
