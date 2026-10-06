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
            _seLookupRebuiltThisPass = false; // 見つからない Id があっても、全 SeData の走査は 1 回の再構築につき 1 回まで(GE-R-07)
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
                // ルートにだけ UiButton がある Prefab は行が 0 になる。「無い」ではなく「配線できない」と案内する(GE-R-16)。
                var rootHasButton = _target.Prefab.GetComponent<UiButton>() != null;
                _buttonWireContainer.Add(new Label(rootHasButton
                    ? "ルート要素の UiButton には配線できません(子の要素に UiButton を付けてください)"
                    : "Prefab に UiButton がありません") { style = { opacity = 0.7f, whiteSpace = WhiteSpace.Normal } });
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
                var rootNote = string.IsNullOrEmpty(wire.ButtonPath) ? "   (ルート要素の配線は実行時に効きません)" : string.Empty;
                foldout.Add(new Label($"・{(string.IsNullOrEmpty(wire.ButtonPath) ? RootElementLabel : wire.ButtonPath)}   {wire.Summary}{(wire.OverriddenByParent ? "   [親で上書き]" : string.Empty)}{rootNote}")
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
                // 埋め込みの配下では子が使っているトリガーも避ける(親の配線は Action に関係なく子の同じトリガーを止めるため。GE-R-05)。
                var added = CanvasButtonWireEditing.AddWire(owner, path, Lookup);
                AfterButtonWireEdit(owner, added >= 0 ? "配線を追加しました" : $"'{path}' にはこれ以上足せるトリガーがありません(Click / DoubleClick / LongPress / Repeat が全部使われています。埋め込みの配下では子の配線の分も含みます)");
            })
            {
                text = "+ 配線を追加",
                tooltip = "このボタンに配線を 1 本足す(トリガーはまだ使っていないもの。埋め込みの配下では、子の CanvasData が使っているトリガーも避ける。Ctrl+Z で戻せる)",
            });
            box.Add(header);

            if (!row.InPrefab)
            {
                box.Add(new HelpBox(string.IsNullOrEmpty(path)
                    ? "Canvas のルート要素には配線できません(実行時に配線されません。子の要素に UiButton を付けて、そのパスを指定してください)。"
                    : $"Prefab の中に '{path}' の UiButton が見つかりません(パスの違い、または UiButton が付いていません)。", HelpBoxMessageType.Warning));
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
            var expected = wire; // この行を作ったときの値。書く前に同じ配線か確かめる(GE-R-08)
            var container = new VisualElement { style = { marginLeft = 4, marginTop = 2 } };
            var line = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center } };

            var trigger = new EnumField(wire.Trigger) { style = { width = 110 }, tooltip = "どの操作で動くか(Click / DoubleClick / LongPress / Repeat)" };
            trigger.RegisterValueChangedCallback(evt =>
            {
                UpdateWire(owner, index, expected, w => { w.Trigger = (WireTrigger)evt.newValue; return w; });
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
                UpdateWire(owner, index, expected, w => { w.Action = evt.newValue; return w; });
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
                    if (picked != null && picked.Id == 0)
                    {
                        // Id の無い CanvasData は書かず、欄を前の値へ戻す(それまでの対象を消さない。GE-R-07)。
                        target.SetValueWithoutNotify(evt.previousValue);
                        if (owner == _target && _statusLabel != null)
                        {
                            _statusLabel.text = "その CanvasData には Id がありません(対象は変えていません)";
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
                    line.Add(new Label($"(Id 0x{wire.Target.Id:X} の CanvasData が見つかりません)") { style = { opacity = 0.7f } });
                }
            }

            if (CanvasButtonWireEditing.UsesSignalKey(wire.Action))
            {
                var key = new TextField { value = wire.SignalKey ?? string.Empty, isDelayed = true, style = { width = 200 }, tooltip = "シグナルのキー(Ui.OnSignal(key, …) で受け取る)。Enter か欄外クリックで確定" };
                key.RegisterValueChangedCallback(evt =>
                {
                    UpdateWire(owner, index, expected, w => { w.SignalKey = evt.newValue; return w; });
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
                    UpdateWire(owner, index, expected, w => { w.EmbeddedRootPath = evt.newValue == SelfLabel ? string.Empty : evt.newValue; return w; });
                    AfterButtonWireEdit(owner, null);
                });
                line.Add(embedPath);
            }

            line.Add(new Label("SE") { style = { marginLeft = 6, marginRight = 2, opacity = 0.8f } });
            var currentSe = FindSeData(wire.ClickSe.Value);
            var se = new ObjectField { objectType = typeof(SeData), allowSceneObjects = false, value = currentSe, style = { width = 150 }, tooltip = "押したときに鳴らす SE(任意)" };
            se.RegisterValueChangedCallback(evt =>
            {
                var picked = evt.newValue as SeData;
                if (picked != null && picked.Id == 0)
                {
                    // Id の無い SeData は書かず、欄を前の値へ戻す(それまでの SE を消さない。GE-R-11)。
                    se.SetValueWithoutNotify(evt.previousValue);
                    if (owner == _target && _statusLabel != null)
                    {
                        _statusLabel.text = "その SeData には Id がありません(クリック SE は変えていません)";
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
                line.Add(new Label($"(Id 0x{wire.ClickSe.Value:X} の SeData が見つかりません。実行時は Placeholder)") { style = { opacity = 0.7f } });
            }

            line.Add(new Button(() =>
            {
                // 「削除」も各欄と同じく、行を作ったときの配線か確かめてから消す(並べ替え後に別の配線を消さない。GE-R-14)。
                switch (CanvasButtonWireEditing.TryRemoveWire(owner, index, expected))
                {
                    case CanvasButtonWireEditing.RemoveOutcome.Removed:
                        AfterButtonWireEdit(owner, "配線を削除しました");
                        break;
                    case CanvasButtonWireEditing.RemoveOutcome.Stale:
                        NotifyWireRowsStale(owner);
                        break;
                }
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

        // 範囲外(Inspector で配線を減らした後)や、同じ添字が別の配線になっていたら(並べ替えた後。GE-R-08 / GE-R-12)
        // 書かずに欄を作り直して知らせる(Undo で行が減った後に古い行の UI から呼ばれても例外にしない)。
        private void UpdateWire(CanvasData owner, int index, in ButtonWire expected, Func<ButtonWire, ButtonWire> mutate)
        {
            if (owner == null)
            {
                return;
            }

            // 範囲外(Inspector で配線を減らした後)も、照合で弾いたときと同じく欄を作り直して知らせる(GE-R-12)。
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

        // 行を作ったときと配線の並びが違っていた: 表示中の対象なら欄を作り直して知らせる(書き込みはしない)。
        private void NotifyWireRowsStale(CanvasData owner)
        {
            if (owner != _target)
            {
                return;
            }

            RebuildButtonWires();
            if (_statusLabel != null)
            {
                _statusLabel.text = "配線の並びが変わっていたため、欄を作り直しました(もう一度操作してください)";
            }
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
    }
}
