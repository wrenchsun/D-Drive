using System;
using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.CanvasTool
{
    // 「ボタンの配線(Buttons)」の欄(docs/1010 UX-4 / D-10)。1 ボタン = 1 行(パス・「Click → OpenCanvas 'X' / LongPress → …」の 1 行要約・選択・＋配線・⚙)。
    // 展開(⚙)はその行だけで、そのボタンの配線 1 本 = 1 行(トリガー・アクション・対象・SE・削除)。一覧の上に絞り込みと件数。
    // 埋め込みの子の配線は展開せず「1 行の要約 + 開く」(子の配線は子の CanvasData で編集)。スライダーの配線は通常の一覧欄(Sliders)。
    internal sealed partial class CanvasEditor
    {
        private CanvasItemList _wireList;
        private CanvasSectionView _wireSection;
        private readonly HashSet<string> _wireOpen = new();
        private readonly Dictionary<ulong, SeData> _seLookup = new();
        private bool _seLookupRebuiltThisPass;

        private static readonly List<UiAction> ButtonActionChoices = BuildButtonActionChoices();

        private static List<UiAction> BuildButtonActionChoices()
        {
            var list = new List<UiAction>();
            foreach (UiAction action in Enum.GetValues(typeof(UiAction)))
            {
                if (CanvasButtonWireEditing.IsButtonAction(action))
                {
                    list.Add(action);
                }
            }

            return list;
        }

        private UxRow CreateWireRows(FieldGuideEntry e, UxCard card)
        {
            _wireList = new CanvasItemList("ボタン", "配線済み", null, "Prefab に UiButton がありません");
            _wireSection = new CanvasSectionView("wire", _foldOpen, WireTitle, WireSignature, _ => _wireList.SetItems(CollectWireItems()));
            _wireSection.Static.Add(_wireList);
            _wireSection.Rebuild();
            AddRow(card, SectionRow(e, _wireSection, q => _wireList.AnyMatch(q)), false);
            return RawListRow(e, "ボタンの配線(一覧そのもの)");
        }

        private string WireTitle()
        {
            var wires = Target?.Buttons?.Length ?? 0;
            var buttons = new HashSet<string>();
            for (var i = 0; Target?.Buttons != null && i < Target.Buttons.Length; i++)
            {
                buttons.Add(Target.Buttons[i].ButtonPath ?? string.Empty);
            }

            return $"{wires} 配線({buttons.Count} ボタン)";
        }

        private int WireSignature()
        {
            var t = Target;
            if (t == null)
            {
                return 0;
            }

            var h = CanvasSig.Add(17, t.Prefab != null ? t.Prefab.GetInstanceID() : 0);
            var rows = t.Buttons;
            for (var i = 0; rows != null && i < rows.Length; i++)
            {
                var w = rows[i];
                h = CanvasSig.Add(h, w.ButtonPath);
                h = CanvasSig.Add(h, (int)w.Trigger);
                h = CanvasSig.Add(h, (int)w.Action);
                h = CanvasSig.Add(h, (int)w.Target.Type);
                h = CanvasSig.Add(h, w.Target.Id);
                h = CanvasSig.Add(h, w.SignalKey);
                h = CanvasSig.Add(h, w.ClickSe.Value);
                h = CanvasSig.Add(h, w.EmbeddedRootPath);
            }

            var embeds = t.EmbeddedCanvases;
            for (var i = 0; embeds != null && i < embeds.Length; i++)
            {
                h = CanvasSig.Add(h, embeds[i].RootPath);
                h = CanvasSig.Add(h, embeds[i].Canvas.Value);
            }

            return h;
        }

        private List<CanvasListItem> CollectWireItems()
        {
            var items = new List<CanvasListItem>();
            _seLookupRebuiltThisPass = false; // 見つからない Id があっても、全 SeData の走査は 1 回の再構築につき 1 回まで
            if (Target == null || Target.Prefab == null)
            {
                return items;
            }

            var owner = Target;
            var groups = CanvasButtonWireEditing.BuildGroups(owner, Lookup);
            foreach (var row in groups.ParentRows)
            {
                items.Add(WireItem(owner, row, null));
            }

            foreach (var g in groups.Embeds)
            {
                foreach (var row in g.OverrideRows)
                {
                    items.Add(WireItem(owner, row, "[親での上書き] "));
                }
            }

            foreach (var g in groups.Embeds)
            {
                var group = g;
                items.Add(new CanvasListItem
                {
                    Key = "embedwire:" + g.RootPath,
                    Title = g.RootPath,
                    Chip = "埋め込み",
                    Summary = true,
                    Configured = g.ChildWires.Count > 0,
                    Search = g.RootPath,
                    Make = () => MakeEmbedWireSummary(group),
                });
            }

            if (items.Count == 0)
            {
                // ルートにだけ UiButton がある Prefab は行が 0 になる。「無い」ではなく「配線できない」と案内する。
                SetStatusIfIdle(owner.Prefab.GetComponent<UiButton>() != null
                    ? "ルート要素の UiButton には配線できません(子の要素に UiButton を付けてください)"
                    : null);
            }

            return items;
        }

        private void SetStatusIfIdle(string text)
        {
            if (!string.IsNullOrEmpty(text) && string.IsNullOrEmpty(_statusText))
            {
                SetStatus(text);
            }
        }

        private CanvasListItem WireItem(CanvasData owner, CanvasButtonWireEditing.ButtonRow row, string prefix)
        {
            var path = row.ButtonPath ?? string.Empty;
            var parts = new List<string>();
            foreach (var index in row.WireIndices)
            {
                parts.Add(CanvasButtonWireEditing.Summarize(GetWire(owner, index), Lookup));
            }

            var summary = parts.Count == 0 ? "配線なし" : string.Join(" / ", parts);
            return new CanvasListItem
            {
                Key = "wire:" + path,
                Title = (prefix ?? string.Empty) + (string.IsNullOrEmpty(path) ? RootElementLabel : path),
                Chip = row.InPrefab ? "ボタン" : "見つからない",
                Configured = row.WireIndices.Count > 0,
                Search = summary,
                Make = () => MakeButtonRow(owner, row, prefix, summary),
            };
        }

        // ボタン 1 つぶんの 1 行。⚙ でそのボタンの配線 1 本 = 1 行を開く。
        private VisualElement MakeButtonRow(CanvasData owner, CanvasButtonWireEditing.ButtonRow row, string prefix, string summaryText)
        {
            var path = row.ButtonPath ?? string.Empty;
            var key = "wire:" + path;
            var open = _wireOpen.Contains(key);
            var box = new VisualElement();
            box.AddToClassList("ux-fxrow");
            box.EnableInClassList("ux-fxrow--open", open);

            var head = new VisualElement();
            head.AddToClassList("ux-fxrow__head");
            if (row.WireIndices.Count > 0)
            {
                head.Add(NewFlag(_wireLineFlags, row.WireIndices[0], _wireIssue));
            }

            var name = new Label((prefix ?? string.Empty) + (string.IsNullOrEmpty(path) ? RootElementLabel : path))
            {
                tooltip = "クリックでこのボタンを選択して Inspector に出す(プレハブモード中はステージ内、確認用シーンの表示中はプレビュー実体、どちらも無ければ Prefab アセット内の要素を Ping)",
            };
            name.AddToClassList("ux-fxrow__name");
            name.RegisterCallback<ClickEvent>(_ => SelectElement(path, false));
            head.Add(name);
            var chip = new Label(row.InPrefab ? "ボタン" : "見つからない");
            chip.AddToClassList("ux-chip");
            chip.AddToClassList("ux-fxrow__chip");
            if (!row.InPrefab)
            {
                chip.AddToClassList("ux-chip--warn");
            }

            head.Add(chip);
            var summary = new Label(summaryText);
            summary.AddToClassList("ux-dim");
            summary.AddToClassList("ux-fxrow__summary");
            summary.tooltip = summaryText;
            head.Add(summary);

            head.Add(MakeButton("＋ 配線", "ux-btn--small", () =>
            {
                // 埋め込みの配下では子が使っているトリガーも避ける(親の配線は Action に関係なく子の同じトリガーを止めるため)。
                var added = CanvasButtonWireEditing.AddWire(owner, path, Lookup);
                if (added >= 0)
                {
                    _wireOpen.Add(key);
                }

                AfterButtonWireEdit(owner, added >= 0 ? "配線を追加しました" : $"'{path}' にはこれ以上足せるトリガーがありません(Click / DoubleClick / LongPress / Repeat が全部使われています。埋め込みの配下では子の配線の分も含みます)");
            }, "このボタンに配線を 1 本足す(トリガーはまだ使っていないもの。Ctrl+Z で戻せる)"));
            var toggle = new Button(() =>
            {
                if (!_wireOpen.Add(key))
                {
                    _wireOpen.Remove(key);
                }

                _wireList.Render();
            })
            { text = open ? "▾" : "⚙", tooltip = "このボタンの配線を 1 本ずつ編集する(トリガー・アクション・対象・クリック SE・削除)" };
            toggle.AddToClassList("ux-btn");
            toggle.AddToClassList("ux-btn--icon");
            head.Add(toggle);
            box.Add(head);

            if (open)
            {
                if (!row.InPrefab)
                {
                    var warn = new HelpBox(string.IsNullOrEmpty(path)
                        ? "Canvas のルート要素には配線できません(実行時に配線されません。子の要素に UiButton を付けて、そのパスを指定してください)。"
                        : $"Prefab の中に '{path}' の UiButton が見つかりません(パスの違い、または UiButton が付いていません)。", HelpBoxMessageType.Warning);
                    box.Add(warn);
                }

                if (row.WireIndices.Count == 0)
                {
                    var none = new Label("配線なし(「＋ 配線」で足せます)");
                    none.AddToClassList("ux-dim");
                    box.Add(none);
                }

                var details = new VisualElement();
                details.AddToClassList("ux-fxrow__details");
                foreach (var index in row.WireIndices)
                {
                    details.Add(BuildWireRow(owner, index));
                }

                box.Add(details);
            }

            return box;
        }

        // 埋め込みの子の配線: 展開せず 1 行の要約 + 「開く」。
        private VisualElement MakeEmbedWireSummary(CanvasButtonWireEditing.EmbedWireGroup g)
        {
            var row = new VisualElement();
            row.AddToClassList("ux-fxrow");
            var head = new VisualElement();
            head.AddToClassList("ux-fxrow__head");
            var name = new Label(g.RootPath);
            name.AddToClassList("ux-fxrow__name");
            head.Add(name);
            var chip = new Label("埋め込み");
            chip.AddToClassList("ux-chip");
            chip.AddToClassList("ux-fxrow__chip");
            head.Add(chip);
            var summary = new Label(g.Child == null
                ? "子の CanvasData が未設定、または見つかりません"
                : $"→ {NameOf(g.Child)}: 子の配線 {g.ChildWires.Count} 本(中身は子のエディターで)");
            summary.AddToClassList("ux-dim");
            summary.AddToClassList("ux-fxrow__summary");
            summary.tooltip = summary.text;
            head.Add(summary);
            if (g.Child != null)
            {
                var index = g.EmbedIndex;
                head.Add(MakeButton("開く", "ux-btn--small", () => EditEmbedded(index),
                    "編集対象をこの子の CanvasData に切り替える(「← 親へ戻る」で戻れる)。子の配線は子の CanvasData で編集する"));
            }

            row.Add(head);
            return row;
        }

        // 配線 1 本ぶん(1 行): トリガー / アクション / (アクションに応じた欄) / クリック SE / 削除。各欄は作ったときの対象(owner)に書く。
        private VisualElement BuildWireRow(CanvasData owner, int index)
        {
            var wire = GetWire(owner, index);
            var expected = wire; // この行を作ったときの値。書く前に同じ配線か確かめる
            var line = new VisualElement();
            line.AddToClassList("ux-wireline");

            var trigger = new EnumField(wire.Trigger) { tooltip = "どの操作で動くか(Click / DoubleClick / LongPress / Repeat)" };
            trigger.AddToClassList("ux-wireline__trigger");
            trigger.RegisterValueChangedCallback(evt =>
            {
                UpdateWire(owner, index, expected, w => { w.Trigger = (WireTrigger)evt.newValue; return w; });
                AfterButtonWireEdit(owner, null);
            });
            line.Add(trigger);

            var arrow = new Label("→");
            arrow.AddToClassList("ux-dim");
            line.Add(arrow);

            var choices = ButtonActionChoices;
            if (!choices.Contains(wire.Action))
            {
                choices = new List<UiAction>(ButtonActionChoices) { wire.Action }; // スライダー用の値が入っていても消さずに見せる
            }

            var action = new PopupField<UiAction>(choices, wire.Action) { tooltip = "押したときの動作" };
            action.AddToClassList("ux-wireline__action");
            action.RegisterValueChangedCallback(evt =>
            {
                UpdateWire(owner, index, expected, w => { w.Action = evt.newValue; return w; });
                AfterButtonWireEdit(owner, null); // アクションで出す欄が変わるので作り直す
            });
            line.Add(action);

            if (CanvasButtonWireEditing.UsesTarget(wire.Action))
            {
                var current = wire.Target.IsAssigned ? Lookup.Find(new AssetId<CanvasMarker>(wire.Target.Id, AssetType.Canvas)) : null;
                var target = new ObjectField { objectType = typeof(CanvasData), allowSceneObjects = false, value = current, tooltip = "開く Canvas" };
                target.AddToClassList("ux-wireline__field");
                target.RegisterValueChangedCallback(evt =>
                {
                    var picked = evt.newValue as CanvasData;
                    if (picked != null && picked.Id == 0)
                    {
                        // Id の無い CanvasData は書かず、欄を前の値へ戻す(それまでの対象を消さない)。
                        target.SetValueWithoutNotify(evt.previousValue);
                        if (owner == Target)
                        {
                            SetStatus("その CanvasData には Id がありません(対象は変えていません)");
                        }

                        return;
                    }

                    UpdateWire(owner, index, expected, w =>
                    {
                        w.Target = picked != null ? new AssetRef { Type = AssetType.Canvas, Id = picked.Id } : default;
                        return w;
                    });
                    AfterButtonWireEdit(owner, null);
                });
                line.Add(target);
                if (wire.Target.IsAssigned && current == null)
                {
                    var lost = new Label($"(Id 0x{wire.Target.Id:X} の CanvasData が見つかりません)");
                    lost.AddToClassList("ux-dim");
                    line.Add(lost);
                }
            }

            if (CanvasButtonWireEditing.UsesSignalKey(wire.Action))
            {
                var key = new TextField { value = wire.SignalKey ?? string.Empty, isDelayed = true, tooltip = "シグナルのキー(Ui.OnSignal(key, …) で受け取る)。Enter か欄外クリックで確定" };
                key.AddToClassList("ux-wireline__field");
                key.RegisterValueChangedCallback(evt =>
                {
                    UpdateWire(owner, index, expected, w => { w.SignalKey = evt.newValue; return w; });
                    AfterButtonWireEdit(owner, null);
                });
                line.Add(key);
            }

            if (wire.Action == UiAction.PlayPresentation)
            {
                var note = new Label("(実行時は未対応。警告だけが出ます)");
                note.AddToClassList("ux-dim");
                line.Add(note);
            }

            if (CanvasButtonWireEditing.UsesEmbeddedRootPath(wire.Action))
            {
                // 切り替える埋め込み: この CanvasData に登録された RootPath から選ぶ。先頭 = 空(このボタンが属している埋め込み = 自分自身)。
                const string SelfLabel = "(このボタンが属する埋め込み)";
                var paths = new List<string> { SelfLabel };
                CanvasButtonWireEditing.CollectEmbedPaths(owner, Lookup, paths); // 直下 + 入れ子の入れ子("OptionRoot/Inner")

                var currentPath = string.IsNullOrEmpty(wire.EmbeddedRootPath) ? SelfLabel : wire.EmbeddedRootPath;
                if (!paths.Contains(currentPath))
                {
                    paths.Add(currentPath); // 登録に無い値が入っていても消さずに見せる(下に注意が出る)
                }

                var embedPath = new PopupField<string>(paths, currentPath) { tooltip = "有効 / 無効を切り替える埋め込み Canvas(この CanvasData の「埋め込み Canvas」に登録した RootPath)。先頭は、埋め込みの子の配線で「自分を隠す」ときに使う" };
                embedPath.AddToClassList("ux-wireline__field");
                embedPath.RegisterValueChangedCallback(evt =>
                {
                    UpdateWire(owner, index, expected, w => { w.EmbeddedRootPath = evt.newValue == SelfLabel ? string.Empty : evt.newValue; return w; });
                    AfterButtonWireEdit(owner, null);
                });
                line.Add(embedPath);
            }

            var seLabel = new Label("SE");
            seLabel.AddToClassList("ux-dim");
            line.Add(seLabel);
            var currentSe = FindSeData(wire.ClickSe.Value);
            var se = new ObjectField { objectType = typeof(SeData), allowSceneObjects = false, value = currentSe, tooltip = "押したときに鳴らす SE(任意)" };
            se.AddToClassList("ux-wireline__field");
            se.RegisterValueChangedCallback(evt =>
            {
                var picked = evt.newValue as SeData;
                if (picked != null && picked.Id == 0)
                {
                    // Id の無い SeData は書かず、欄を前の値へ戻す(それまでの SE を消さない)。
                    se.SetValueWithoutNotify(evt.previousValue);
                    if (owner == Target)
                    {
                        SetStatus("その SeData には Id がありません(クリック SE は変えていません)");
                    }

                    return;
                }

                UpdateWire(owner, index, expected, w =>
                {
                    w.ClickSe = picked != null ? new AssetId<SeMarker>(picked.Id, AssetType.Se) : default;
                    return w;
                });
                AfterButtonWireEdit(owner, null);
            });
            line.Add(se);
            if (wire.ClickSe.IsValid && currentSe == null)
            {
                var lost = new Label($"(Id 0x{wire.ClickSe.Value:X} の SeData が見つかりません。実行時は Placeholder)");
                lost.AddToClassList("ux-dim");
                line.Add(lost);
            }

            line.Add(MakeButton("削除", "ux-btn--small", () =>
            {
                // 「削除」も各欄と同じく、行を作ったときの配線か確かめてから消す(並べ替え後に別の配線を消さない)。
                switch (CanvasButtonWireEditing.TryRemoveWire(owner, index, expected))
                {
                    case CanvasButtonWireEditing.RemoveOutcome.Removed:
                        AfterButtonWireEdit(owner, "配線を削除しました");
                        break;
                    case CanvasButtonWireEditing.RemoveOutcome.Stale:
                        NotifyWireRowsStale(owner);
                        break;
                }
            }, "この配線を取り除く(Ctrl+Z で戻せる)"));

            var wrap = new VisualElement();
            wrap.Add(line);
            var problem = CanvasButtonWireEditing.DescribeProblem(owner, index, Lookup);
            if (problem != null)
            {
                var p = new Label("⚠ " + problem);
                p.AddToClassList("ux-message");
                wrap.Add(p);
            }

            return wrap;
        }

        private static ButtonWire GetWire(CanvasData owner, int index)
            => owner != null && owner.Buttons != null && index >= 0 && index < owner.Buttons.Length ? owner.Buttons[index] : default;

        // 範囲外(Inspector で配線を減らした後)や、同じ添字が別の配線になっていたら(並べ替えた後)書かずに欄を作り直して知らせる。
        private void UpdateWire(CanvasData owner, int index, in ButtonWire expected, Func<ButtonWire, ButtonWire> mutate)
        {
            if (owner == null)
            {
                return;
            }

            var outOfRange = owner.Buttons == null || index < 0 || index >= owner.Buttons.Length;
            if (outOfRange || !CanvasButtonWireEditing.SameWire(owner.Buttons[index], expected))
            {
                NotifyWireRowsStale(owner);
                return;
            }

            Undo.RecordObject(owner, "Canvas: ボタンの配線");
            owner.Buttons[index] = mutate(owner.Buttons[index]);
            EditorUtility.SetDirty(owner);
        }

        private void NotifyWireRowsStale(CanvasData owner)
        {
            if (owner != Target)
            {
                return;
            }

            SyncSections(true);
            SetStatus("配線の並びが変わっていたため、欄を作り直しました(もう一度操作してください)");
        }

        // 配線を変えた後: 表示中の対象ならこの欄と検査を作り直す(選択に追従して別の Canvas に切り替わっていたら何もしない)。
        private void AfterButtonWireEdit(CanvasData owner, string status)
        {
            if (owner != Target)
            {
                return;
            }

            AfterDataWrite();
            if (!string.IsNullOrEmpty(status))
            {
                SetStatus(status);
            }
        }

        private SeData FindSeData(ulong id)
        {
            if (id == 0)
            {
                return null;
            }

            if (_seLookup.TryGetValue(id, out var cached) && cached != null && cached.Id == id)
            {
                return cached;
            }

            if (_seLookupRebuiltThisPass)
            {
                return null;
            }

            _seLookupRebuiltThisPass = true;
            _seLookup.Clear();
            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(SeData)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<SeData>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.Id != 0)
                {
                    _seLookup.TryAdd(asset.Id, asset);
                }
            }

            return _seLookup.TryGetValue(id, out cached) ? cached : null;
        }

        private void ApplyWireFilter(string query)
        {
            if (_wireList == null)
            {
                return;
            }

            _wireList.SetExternalQuery(query);
            if (!string.IsNullOrEmpty(query) && _wireList.AnyMatch(query))
            {
                _wireSection?.Open();
            }
        }
    }
}
