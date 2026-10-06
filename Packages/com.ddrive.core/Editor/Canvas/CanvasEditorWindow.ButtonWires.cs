using System;
using System.Collections.Generic;
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
    // [07_canvas_prefab.md] A-4 追記(2026-10-06) — Canvas Editor の「ボタンの配線」欄(CanvasData.Buttons の編集 UI)。
    // Prefab 内の UiButton を一覧し、ボタン + トリガーごとにアクションと対象を選ぶ。グループ分け(親の要素 / 親での上書き /
    // 埋め込み: 子)とデータの操作は CanvasButtonWireEditing、ここは画面だけ。埋め込みの子の配線は子の CanvasData で編集する。
    public sealed partial class CanvasEditorWindow
    {
        private Foldout _buttonWireFoldout;
        private VisualElement _buttonWireContainer;
        private readonly Dictionary<string, bool> _wireGroupExpanded = new();
        private readonly Dictionary<ulong, SeData> _seLookup = new();

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

        private void BuildButtonWireSection()
        {
            _buttonWireFoldout = new Foldout { text = "ボタンの配線(Buttons)", value = true, style = { marginTop = 8 } };
            _root.Add(_buttonWireFoldout);
            _buttonWireFoldout.Add(new HelpBox(
                "Prefab 内の UiButton ごとに、押したときの動作(トリガー → アクション)を設定します。埋め込みの子の配線は子の CanvasData に持ちます(「この Canvas を編集」)。親に同じ要素・同じトリガーの配線があれば親が優先されます。",
                HelpBoxMessageType.Info));
            _buttonWireContainer = new VisualElement();
            _buttonWireFoldout.Add(_buttonWireContainer);
        }

        private void RebuildButtonWires()
        {
            if (_buttonWireContainer == null)
            {
                return;
            }

            _buttonWireContainer.Clear();
            if (_target == null)
            {
                return;
            }

            if (_target.Prefab == null)
            {
                _buttonWireContainer.Add(new Label("Prefab を設定してください") { style = { opacity = 0.7f } });
                return;
            }

            var owner = _target;
            var groups = CanvasButtonWireEditing.BuildGroups(owner, Lookup);
            if (groups.ParentRows.Count == 0 && groups.Embeds.Count == 0)
            {
                _buttonWireContainer.Add(new Label("Prefab に UiButton がありません") { style = { opacity = 0.7f } });
                return;
            }

            VisualElement parentContainer = _buttonWireContainer;
            if (groups.Embeds.Count > 0)
            {
                var parentFoldout = NewWireGroupFoldout("parent", $"{NameOf(owner)} のボタン({groups.ParentRows.Count})");
                _buttonWireContainer.Add(parentFoldout);
                parentContainer = parentFoldout;
            }

            if (groups.ParentRows.Count == 0)
            {
                parentContainer.Add(new Label("この Canvas 自身のボタンはありません") { style = { opacity = 0.7f } });
            }

            foreach (var row in groups.ParentRows)
            {
                parentContainer.Add(BuildButtonRow(owner, row, null));
            }

            foreach (var g in groups.Embeds)
            {
                if (g.OverrideRows.Count == 0)
                {
                    continue;
                }

                parentContainer.Add(new Label($"↳ 親での上書き: {(g.Child != null ? NameOf(g.Child) : "(未解決)")}({g.RootPath})。同じ要素・同じトリガーの配線は、子の CanvasData の設定より優先されます")
                {
                    style = { unityFontStyleAndWeight = FontStyle.Bold, whiteSpace = WhiteSpace.Normal, marginTop = 4, marginBottom = 2 },
                });
                foreach (var row in g.OverrideRows)
                {
                    parentContainer.Add(BuildButtonRow(owner, row, "[親での上書き] "));
                }
            }

            foreach (var g in groups.Embeds)
            {
                _buttonWireContainer.Add(BuildEmbedWireGroup(g));
            }
        }

        private Foldout NewWireGroupFoldout(string key, string title)
        {
            var foldout = new Foldout { text = title, value = !_wireGroupExpanded.TryGetValue(key, out var open) || open, style = { marginTop = 4 } };
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == foldout)
                {
                    _wireGroupExpanded[key] = evt.newValue;
                }
            });
            return foldout;
        }

        // 埋め込みごとのグループ: 子の配線を読み取り表示し、「この Canvas を編集」で編集対象を子へ切り替える。
        private VisualElement BuildEmbedWireGroup(CanvasButtonWireEditing.EmbedWireGroup g)
        {
            var title = g.Child != null ? $"埋め込み: {NameOf(g.Child)}({g.RootPath})" : $"埋め込み: (未解決)({g.RootPath})";
            var foldout = NewWireGroupFoldout("embed:" + g.RootPath, title);
            if (g.Child == null)
            {
                foldout.Add(new HelpBox("子の CanvasData が未設定、または見つかりません。上の「埋め込み Canvas」で指定してください。", HelpBoxMessageType.Warning));
                return foldout;
            }

            var index = g.EmbedIndex;
            foldout.Add(new Button(() => EditEmbedded(index))
            {
                text = "この Canvas を編集",
                tooltip = "編集対象をこの子の CanvasData に切り替える(「← 親へ戻る」で戻れる)。子の配線は子の CanvasData で編集する",
            });
            foldout.Add(new Label($"子の配線: {g.ChildWires.Count} 本(読み取り表示。編集は「この Canvas を編集」で)") { style = { opacity = 0.7f, marginTop = 2 } });
            foreach (var wire in g.ChildWires)
            {
                foldout.Add(new Label($"・{(string.IsNullOrEmpty(wire.ButtonPath) ? RootElementLabel : wire.ButtonPath)}   {wire.Summary}{(wire.OverriddenByParent ? "   [親で上書き]" : string.Empty)}")
                {
                    style = { whiteSpace = WhiteSpace.Normal, opacity = wire.OverriddenByParent ? 0.5f : 0.85f },
                });
            }

            return foldout;
        }

        // ボタン 1 つぶん: パス・「選択」・「+ 配線を追加」と、そのボタンの配線の行。
        private VisualElement BuildButtonRow(CanvasData owner, CanvasButtonWireEditing.ButtonRow row, string labelPrefix)
        {
            var path = row.ButtonPath ?? string.Empty;
            var box = new VisualElement { style = { marginBottom = 6, paddingLeft = 4, borderLeftWidth = 2, borderLeftColor = new Color(0.5f, 0.5f, 0.5f, 0.4f) } };

            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center } };
            header.Add(new Label((labelPrefix ?? string.Empty) + (string.IsNullOrEmpty(path) ? RootElementLabel : path))
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1f, whiteSpace = WhiteSpace.Normal },
            });
            header.Add(new Button(() => SelectElement(path, focus: false))
            {
                text = "選択",
                tooltip = "このボタンを選択して Inspector に出す(プレハブモード中はステージ内、確認用シーンの表示中はプレビュー実体、どちらも無ければ Prefab アセット内の要素を Ping)",
            });
            header.Add(new Button(() =>
            {
                CanvasButtonWireEditing.AddWire(owner, path);
                AfterButtonWireEdit(owner, "配線を追加しました");
            })
            {
                text = "+ 配線を追加",
                tooltip = "このボタンに配線を 1 本足す(トリガーはまだ使っていないもの。Ctrl+Z で戻せる)",
            });
            box.Add(header);

            if (!row.InPrefab)
            {
                box.Add(new HelpBox($"Prefab の中に '{path}' の UiButton が見つかりません(パスの違い、または UiButton が付いていません)。", HelpBoxMessageType.Warning));
            }

            if (row.WireIndices.Count == 0)
            {
                box.Add(new Label("配線なし") { style = { opacity = 0.6f, marginLeft = 4 } });
            }

            foreach (var index in row.WireIndices)
            {
                box.Add(BuildWireRow(owner, index));
            }

            return box;
        }

        // 配線 1 本ぶん: トリガー / アクション / (アクションに応じた欄) / クリック SE / 削除。各欄は作ったときの対象(owner)に書く。
        private VisualElement BuildWireRow(CanvasData owner, int index)
        {
            var wire = GetWire(owner, index);
            var container = new VisualElement { style = { marginLeft = 4, marginTop = 2 } };
            var line = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center } };

            var trigger = new EnumField(wire.Trigger) { style = { width = 110 }, tooltip = "どの操作で動くか(Click / DoubleClick / LongPress / Repeat)" };
            trigger.RegisterValueChangedCallback(evt =>
            {
                UpdateWire(owner, index, w => { w.Trigger = (WireTrigger)evt.newValue; return w; });
                AfterButtonWireEdit(owner, null);
            });
            line.Add(trigger);

            line.Add(new Label("→") { style = { marginLeft = 2, marginRight = 2 } });

            var choices = ButtonActionChoices;
            if (!choices.Contains(wire.Action))
            {
                choices = new List<UiAction>(ButtonActionChoices) { wire.Action }; // スライダー用の値が入っていても消さずに見せる
            }

            var action = new PopupField<UiAction>(choices, wire.Action) { style = { width = 150 }, tooltip = "押したときの動作" };
            action.RegisterValueChangedCallback(evt =>
            {
                UpdateWire(owner, index, w => { w.Action = evt.newValue; return w; });
                AfterButtonWireEdit(owner, null); // アクションで出す欄が変わるので作り直す
            });
            line.Add(action);

            if (CanvasButtonWireEditing.UsesTarget(wire.Action))
            {
                var current = wire.Target.IsAssigned ? Lookup.Find(new AssetId<CanvasMarker>(wire.Target.Id, AssetType.Canvas)) : null;
                var target = new ObjectField { objectType = typeof(CanvasData), allowSceneObjects = false, value = current, style = { width = 200 }, tooltip = "開く Canvas" };
                target.RegisterValueChangedCallback(evt =>
                {
                    var picked = evt.newValue as CanvasData;
                    UpdateWire(owner, index, w =>
                    {
                        w.Target = picked != null && picked.Id != 0 ? new AssetRef { Type = AssetType.Canvas, Id = picked.Id } : default;
                        return w;
                    });
                    AfterButtonWireEdit(owner, picked != null && picked.Id == 0 ? "その CanvasData には Id がありません" : null);
                });
                line.Add(target);
                if (wire.Target.IsAssigned && current == null)
                {
                    line.Add(new Label($"(Id 0x{wire.Target.Id:X} の CanvasData が見つかりません)") { style = { opacity = 0.7f } });
                }
            }

            if (CanvasButtonWireEditing.UsesSignalKey(wire.Action))
            {
                var key = new TextField { value = wire.SignalKey ?? string.Empty, isDelayed = true, style = { width = 200 }, tooltip = "シグナルのキー(Ui.OnSignal(key, …) で受け取る)。Enter か欄外クリックで確定" };
                key.RegisterValueChangedCallback(evt =>
                {
                    UpdateWire(owner, index, w => { w.SignalKey = evt.newValue; return w; });
                    AfterButtonWireEdit(owner, null);
                });
                line.Add(key);
            }

            if (wire.Action == UiAction.PlayPresentation)
            {
                line.Add(new Label("(実行時は未対応。警告だけが出ます)") { style = { opacity = 0.7f } });
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

                var embedPath = new PopupField<string>(paths, currentPath) { style = { width = 200 }, tooltip = "有効 / 無効を切り替える埋め込み Canvas(この CanvasData の「埋め込み Canvas」に登録した RootPath)。先頭は、埋め込みの子の配線で「自分を隠す」ときに使う" };
                embedPath.RegisterValueChangedCallback(evt =>
                {
                    UpdateWire(owner, index, w => { w.EmbeddedRootPath = evt.newValue == SelfLabel ? string.Empty : evt.newValue; return w; });
                    AfterButtonWireEdit(owner, null);
                });
                line.Add(embedPath);
            }

            line.Add(new Label("SE") { style = { marginLeft = 6, marginRight = 2, opacity = 0.8f } });
            var se = new ObjectField { objectType = typeof(SeData), allowSceneObjects = false, value = FindSeData(wire.ClickSe.Value), style = { width = 150 }, tooltip = "押したときに鳴らす SE(任意)" };
            se.RegisterValueChangedCallback(evt =>
            {
                var picked = evt.newValue as SeData;
                UpdateWire(owner, index, w =>
                {
                    w.ClickSe = picked != null && picked.Id != 0 ? new AssetId<SeMarker>(picked.Id, AssetType.Se) : default;
                    return w;
                });
                AfterButtonWireEdit(owner, null);
            });
            line.Add(se);

            line.Add(new Button(() =>
            {
                CanvasButtonWireEditing.RemoveWire(owner, index);
                AfterButtonWireEdit(owner, "配線を削除しました");
            })
            {
                text = "削除",
                tooltip = "この配線を取り除く(Ctrl+Z で戻せる)",
            });

            container.Add(line);

            var problem = CanvasButtonWireEditing.DescribeProblem(owner, index, Lookup);
            if (problem != null)
            {
                container.Add(new Label("⚠ " + problem) { style = { color = new Color(0.95f, 0.75f, 0.25f), whiteSpace = WhiteSpace.Normal, marginLeft = 2 } });
            }

            return container;
        }

        private static ButtonWire GetWire(CanvasData owner, int index)
            => owner != null && owner.Buttons != null && index >= 0 && index < owner.Buttons.Length ? owner.Buttons[index] : default;

        // 範囲外なら何もしない(Undo で行が減った後に、古い行の UI から呼ばれても例外にしない)。
        private static void UpdateWire(CanvasData owner, int index, Func<ButtonWire, ButtonWire> mutate)
        {
            if (owner == null || owner.Buttons == null || index < 0 || index >= owner.Buttons.Length)
            {
                return;
            }

            Undo.RecordObject(owner, "Canvas: ボタンの配線");
            owner.Buttons[index] = mutate(owner.Buttons[index]);
            EditorUtility.SetDirty(owner);
        }

        // 配線を変えた後: 表示中の対象ならこの欄と検査を作り直す(選択に追従して別の Canvas に切り替わっていたら何もしない)。
        private void AfterButtonWireEdit(CanvasData owner, string status)
        {
            if (owner != _target)
            {
                return;
            }

            RefreshValidation();
            RebuildButtonWires();
            if (!string.IsNullOrEmpty(status) && _statusLabel != null)
            {
                _statusLabel.text = status;
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
    }
}
