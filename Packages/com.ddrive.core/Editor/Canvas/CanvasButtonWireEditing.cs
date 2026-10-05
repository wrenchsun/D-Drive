using System.Collections.Generic;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.CanvasTool
{
    // [07_canvas_prefab.md] A-4 追記(2026-10-06) — Canvas Editor の「ボタンの配線」欄のロジック(UI を持たない部分)。
    // Prefab 内の UiButton と CanvasData.Buttons(ButtonWire)を突き合わせて、ElementFx の一覧と同じ
    // 「親の要素 / 親での上書き / 埋め込み: 子」のグループに分ける。配線の追加・削除もここに置く(EditMode で単体テストできる)。
    // 埋め込みの子の配線は子の CanvasData が持つ(親での上書きは要素 + トリガー単位。U-28 の決まり)。
    public static class CanvasButtonWireEditing
    {
        // ボタン 1 つぶん(Canvas のルート基準のパス)と、そのボタンを指す配線(Buttons の添字)。
        public sealed class ButtonRow
        {
            public string ButtonPath;

            // Prefab の中にその UiButton があるか(false = 配線だけが残っている / パスが違う)。
            public bool InPrefab;

            public readonly List<int> WireIndices = new();
        }

        // 埋め込みの子が持つ配線 1 本ぶんの読み取り表示。
        public readonly struct ChildWireSummary
        {
            public readonly string ButtonPath;   // 子のルート基準
            public readonly WireTrigger Trigger;
            public readonly string Summary;
            public readonly bool OverriddenByParent;

            public ChildWireSummary(string buttonPath, WireTrigger trigger, string summary, bool overriddenByParent)
            {
                ButtonPath = buttonPath;
                Trigger = trigger;
                Summary = summary;
                OverriddenByParent = overriddenByParent;
            }
        }

        public sealed class EmbedWireGroup
        {
            public int EmbedIndex;
            public string RootPath;
            public CanvasData Child;

            // 親の Buttons のうち、この埋め込みの配下のボタンを指す行(親での上書き)。
            public readonly List<ButtonRow> OverrideRows = new();

            // 子の CanvasData の Buttons(読み取り表示)。
            public readonly List<ChildWireSummary> ChildWires = new();
        }

        public sealed class WireGroups
        {
            public readonly List<ButtonRow> ParentRows = new();
            public readonly List<EmbedWireGroup> Embeds = new();
        }

        public static WireGroups BuildGroups(CanvasData canvas, CanvasEmbeddedEditing.CanvasLookup lookup)
        {
            var groups = new WireGroups();
            if (canvas == null)
            {
                return groups;
            }

            var embeds = canvas.EmbeddedCanvases;
            if (embeds != null)
            {
                for (var i = 0; i < embeds.Length; i++)
                {
                    if (string.IsNullOrEmpty(embeds[i].RootPath))
                    {
                        continue; // RootPath が空の行は、どのボタンも配下に持たない(Validator が別に警告する)
                    }

                    groups.Embeds.Add(new EmbedWireGroup
                    {
                        EmbedIndex = i,
                        RootPath = embeds[i].RootPath,
                        Child = lookup?.Find(embeds[i].Canvas),
                    });
                }
            }

            // ① Prefab の中の UiButton(埋め込みの配下は子の担当なので親の行にはしない)。
            if (canvas.Prefab != null)
            {
                var root = canvas.Prefab.transform;
                var buttons = canvas.Prefab.GetComponentsInChildren<UiButton>(true);
                for (var i = 0; i < buttons.Length; i++)
                {
                    var path = TransformPath.GetRelative(root, buttons[i].transform) ?? string.Empty;
                    if (FindEmbed(groups, path, out _) != null)
                    {
                        continue;
                    }

                    if (FindRow(groups.ParentRows, path) == null)
                    {
                        groups.ParentRows.Add(new ButtonRow { ButtonPath = path, InPrefab = true });
                    }
                }
            }

            // ② 配線(Buttons)。埋め込みの配下を指す行は「親での上書き」、それ以外は親のボタンの行に付ける。
            var wires = canvas.Buttons;
            if (wires != null)
            {
                for (var i = 0; i < wires.Length; i++)
                {
                    var path = wires[i].ButtonPath ?? string.Empty;
                    var embed = FindEmbed(groups, path, out _);
                    var rows = embed != null ? embed.OverrideRows : groups.ParentRows;
                    var row = FindRow(rows, path);
                    if (row == null)
                    {
                        row = new ButtonRow { ButtonPath = path, InPrefab = embed != null || ExistsInPrefab(canvas, path) };
                        rows.Add(row);
                    }

                    row.WireIndices.Add(i);
                }
            }

            // ③ 子の配線(読み取り表示)。親に同じ要素 + 同じトリガーの配線があれば「親で上書き」。
            for (var e = 0; e < groups.Embeds.Count; e++)
            {
                var group = groups.Embeds[e];
                var childWires = group.Child != null ? group.Child.Buttons : null;
                if (childWires == null)
                {
                    continue;
                }

                for (var i = 0; i < childWires.Length; i++)
                {
                    var parentPath = EmbeddedPaths.Combine(group.RootPath, childWires[i].ButtonPath ?? string.Empty);
                    group.ChildWires.Add(new ChildWireSummary(
                        childWires[i].ButtonPath ?? string.Empty,
                        childWires[i].Trigger,
                        Summarize(childWires[i], lookup),
                        HasWire(wires, parentPath, childWires[i].Trigger)));
                }
            }

            return groups;
        }

        // 配線 1 本の短い説明(例: "Click → SendSignal 'option/apply'")。
        public static string Summarize(in ButtonWire wire, CanvasEmbeddedEditing.CanvasLookup lookup)
        {
            var detail = string.Empty;
            switch (wire.Action)
            {
                case UiAction.OpenCanvas:
                    var target = wire.Target.IsAssigned && lookup != null
                        ? lookup.Find(new AssetId<CanvasMarker>(wire.Target.Id, AssetType.Canvas))
                        : null;
                    detail = wire.Target.IsAssigned ? $" '{(target != null ? target.name : "(見つかりません)")}'" : " (未設定)";
                    break;
                case UiAction.SendSignal:
                    detail = string.IsNullOrEmpty(wire.SignalKey) ? " (キー未設定)" : $" '{wire.SignalKey}'";
                    break;
            }

            return $"{wire.Trigger} → {wire.Action}{detail}";
        }

        // そのボタンでまだ使っていないトリガー(Click → DoubleClick → LongPress → Repeat の順)。全部使用済みなら Click。
        public static WireTrigger FirstFreeTrigger(CanvasData canvas, string buttonPath)
        {
            var wires = canvas != null ? canvas.Buttons : null;
            var triggers = (WireTrigger[])System.Enum.GetValues(typeof(WireTrigger));
            for (var i = 0; i < triggers.Length; i++)
            {
                if (!HasWire(wires, buttonPath ?? string.Empty, triggers[i]))
                {
                    return triggers[i];
                }
            }

            return WireTrigger.Click;
        }

        // 配線を 1 本足す(Undo 可)。トリガーはそのボタンでまだ使っていないもの、アクションは None。戻り値 = 足した行の添字(失敗は -1)。
        public static int AddWire(CanvasData canvas, string buttonPath)
        {
            if (canvas == null)
            {
                return -1;
            }

            var path = buttonPath ?? string.Empty;
            var trigger = FirstFreeTrigger(canvas, path);
            Undo.RecordObject(canvas, "Canvas: ボタンの配線を追加");
            var old = canvas.Buttons ?? System.Array.Empty<ButtonWire>();
            var next = new ButtonWire[old.Length + 1];
            System.Array.Copy(old, next, old.Length);
            next[old.Length] = new ButtonWire { ButtonPath = path, Trigger = trigger, Action = UiAction.None };
            canvas.Buttons = next;
            EditorUtility.SetDirty(canvas);
            return old.Length;
        }

        // 配線を 1 本取り除く(Undo 可)。範囲外なら何もしない。
        public static bool RemoveWire(CanvasData canvas, int index)
        {
            if (canvas == null || canvas.Buttons == null || index < 0 || index >= canvas.Buttons.Length)
            {
                return false;
            }

            Undo.RecordObject(canvas, "Canvas: ボタンの配線を削除");
            var old = canvas.Buttons;
            var next = new ButtonWire[old.Length - 1];
            for (int i = 0, j = 0; i < old.Length; i++)
            {
                if (i != index)
                {
                    next[j++] = old[i];
                }
            }

            canvas.Buttons = next;
            EditorUtility.SetDirty(canvas);
            return true;
        }

        // アクションごとに、配線のどの欄を使うか(使わない欄は画面に出さない)。
        public static bool UsesTarget(UiAction action) => action == UiAction.OpenCanvas;

        public static bool UsesSignalKey(UiAction action) => action == UiAction.SendSignal;

        // ボタンの配線で選べるアクション(SetOption はスライダー専用。PlayPresentation は実行時に未対応の警告になる)。
        public static bool IsButtonAction(UiAction action) => action != UiAction.SetOption;

        // 配線 1 本の「その場で分かる問題」(無ければ null)。検査(CanvasDataValidator)と同じ観点のうち、欄を見れば直せるものだけ。
        public static string DescribeProblem(CanvasData canvas, int index)
        {
            if (canvas == null || canvas.Buttons == null || index < 0 || index >= canvas.Buttons.Length)
            {
                return null;
            }

            var wire = canvas.Buttons[index];
            if (wire.Action == UiAction.OpenCanvas && !wire.Target.IsAssigned)
            {
                return "開く Canvas が未設定です";
            }

            if (wire.Action == UiAction.SendSignal && string.IsNullOrEmpty(wire.SignalKey))
            {
                return "シグナルのキーが未設定です";
            }

            if (wire.Action == UiAction.SetOption)
            {
                return "SetOption はスライダーの配線用です(ボタンでは何も起きません)";
            }

            var path = wire.ButtonPath ?? string.Empty;
            for (var i = 0; i < index; i++)
            {
                if (canvas.Buttons[i].Trigger == wire.Trigger
                    && string.Equals(canvas.Buttons[i].ButtonPath ?? string.Empty, path, System.StringComparison.Ordinal))
                {
                    return $"同じボタンに {wire.Trigger} の配線が重複しています";
                }
            }

            return null;
        }
        private static EmbedWireGroup FindEmbed(WireGroups groups, string path, out string childPath)
        {
            childPath = null;
            EmbedWireGroup best = null;
            for (var i = 0; i < groups.Embeds.Count; i++)
            {
                var g = groups.Embeds[i];
                if (!EmbeddedPaths.TryToChildPath(g.RootPath, path, out var child))
                {
                    continue;
                }

                // 重なる登録では、より内側(長い RootPath)の登録が担当する(実行時の決まりと同じ)。
                if (best == null || g.RootPath.Length > best.RootPath.Length)
                {
                    best = g;
                    childPath = child;
                }
            }

            return best;
        }

        private static ButtonRow FindRow(List<ButtonRow> rows, string path)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i].ButtonPath, path, System.StringComparison.Ordinal))
                {
                    return rows[i];
                }
            }

            return null;
        }

        private static bool HasWire(ButtonWire[] wires, string path, WireTrigger trigger)
        {
            if (wires == null)
            {
                return false;
            }

            for (var i = 0; i < wires.Length; i++)
            {
                if (wires[i].Trigger == trigger && string.Equals(wires[i].ButtonPath ?? string.Empty, path, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ExistsInPrefab(CanvasData canvas, string path)
        {
            if (canvas.Prefab == null)
            {
                return false;
            }

            var t = string.IsNullOrEmpty(path) ? canvas.Prefab.transform : canvas.Prefab.transform.Find(path);
            return t != null && t.GetComponent<UiButton>() != null;
        }
    }
}
