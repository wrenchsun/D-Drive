using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.CanvasTool
{
    // 「埋め込み Canvas(入れ子の子 Canvas)」の欄(docs/1010 UX-4 / D-10)。1 埋め込み = 1 行(「RootPath → 子の名前・有効で始まる / 無効で始まる」の 1 行要約・開く・⚙)。
    // 展開(⚙)はその行だけで、RootPath・子の CanvasData・無効で始める・表示 / 非表示(作業用)・削除。入れ子 Prefab から検出した未登録の候補も 1 候補 = 1 行(登録)。
    internal sealed partial class CanvasEditor
    {
        private CanvasItemList _embedList;
        private CanvasSectionView _embedSection;
        private readonly HashSet<string> _embedOpen = new();

        private UxRow CreateEmbeddedRows(FieldGuideEntry e, UxCard card)
        {
            _embedList = new CanvasItemList("埋め込み", "登録済み", null, "埋め込み Canvas はありません(入れ子の Prefab があると、ここに検出されます。「＋ 手動で追加」でも足せます)");
            _embedSection = new CanvasSectionView("embed", _foldOpen, EmbedTitle, EmbedSignature, _ => _embedList.SetItems(CollectEmbedItems()));
            var bar = new VisualElement();
            bar.AddToClassList("ux-toolsline");
            bar.Add(MakeButton("＋ 手動で追加", null, () =>
            {
                CanvasEmbeddedEditing.AddEmpty(Target);
                AfterDataWrite();
            }, "空の埋め込みを 1 行足す(RootPath と子の CanvasData は展開して入れる)"));
            _embedSection.Static.Add(bar);
            var help = new Label("親 Prefab の中に子 Canvas の Prefab を入れ子で入れたら「登録」します。子の動き・配線は子の CanvasData に 1 か所で持ち、親を開いたときも効きます。親に同じ要素の行があれば親が優先されます。");
            help.AddToClassList("ux-dim");
            help.AddToClassList("ux-note");
            _embedSection.Static.Add(help);
            _embedSection.Static.Add(_embedList);
            _embedSection.Rebuild();
            AddRow(card, SectionRow(e, _embedSection, q => _embedList.AnyMatch(q)), false);
            return RawListRow(e, "埋め込み Canvas(一覧そのもの)");
        }

        private string EmbedTitle()
        {
            var n = Target?.EmbeddedCanvases?.Length ?? 0;
            var inactive = 0;
            for (var i = 0; i < n; i++)
            {
                if (Target.EmbeddedCanvases[i].StartInactive)
                {
                    inactive++;
                }
            }

            return n == 0 ? "なし" : $"{n} 件" + (inactive > 0 ? $"(無効で始める {inactive})" : string.Empty);
        }

        private int EmbedSignature()
        {
            var t = Target;
            if (t == null)
            {
                return 0;
            }

            var h = CanvasSig.Add(17, t.Prefab != null ? t.Prefab.GetInstanceID() : 0);
            var rows = t.EmbeddedCanvases;
            for (var i = 0; rows != null && i < rows.Length; i++)
            {
                h = CanvasSig.Add(h, rows[i].RootPath);
                h = CanvasSig.Add(h, rows[i].Canvas.Value);
                h = CanvasSig.Add(h, rows[i].StartInactive ? 1 : 0);
            }

            return h;
        }

        private List<CanvasListItem> CollectEmbedItems()
        {
            var items = new List<CanvasListItem>();
            var owner = Target;
            if (owner == null)
            {
                return items;
            }

            var rows = owner.EmbeddedCanvases;
            for (var i = 0; rows != null && i < rows.Length; i++)
            {
                var index = i;
                var embed = rows[i];
                var child = Lookup.Find(embed.Canvas);
                var path = embed.RootPath ?? string.Empty;
                items.Add(new CanvasListItem
                {
                    Key = "embed:" + i,
                    Title = string.IsNullOrEmpty(path) ? "(RootPath 未入力)" : path,
                    Chip = "登録済み",
                    Configured = true,
                    Search = child != null ? NameOf(child) : string.Empty,
                    Make = () => MakeEmbeddedRow(owner, index),
                });
            }

            if (owner.Prefab != null)
            {
                foreach (var c in CanvasEmbeddedEditing.DetectCandidates(owner.Prefab, Lookup.All, rows))
                {
                    if (c.Registered)
                    {
                        continue;
                    }

                    var candidate = c;
                    items.Add(new CanvasListItem
                    {
                        Key = "cand:" + c.RootPath,
                        Title = c.RootPath,
                        Chip = "未登録",
                        Configured = false,
                        Search = NameOf(c.Canvas),
                        Make = () => MakeCandidateRow(candidate),
                    });
                }
            }

            return items;
        }

        private VisualElement MakeCandidateRow(CanvasEmbeddedEditing.Candidate c)
        {
            var row = new VisualElement();
            row.AddToClassList("ux-fxrow");
            var head = new VisualElement();
            head.AddToClassList("ux-fxrow__head");
            var name = new Label(c.RootPath);
            name.AddToClassList("ux-fxrow__name");
            head.Add(name);
            var chip = new Label("未登録");
            chip.AddToClassList("ux-chip");
            chip.AddToClassList("ux-fxrow__chip");
            head.Add(chip);
            var summary = new Label($"入れ子 Prefab から検出 → {NameOf(c.Canvas)}");
            summary.AddToClassList("ux-dim");
            summary.AddToClassList("ux-fxrow__summary");
            head.Add(summary);
            head.Add(MakeButton("登録", "ux-btn--small", () => RegisterEmbedded(c.RootPath, c.Canvas),
                "この入れ子 Prefab を埋め込み Canvas として EmbeddedCanvases に追加する(子の動き・配線は子の CanvasData のものが親の中でも効く)"));
            row.Add(head);
            return row;
        }

        private VisualElement MakeEmbeddedRow(CanvasData owner, int index)
        {
            var embed = owner.EmbeddedCanvases[index];
            var child = Lookup.Find(embed.Canvas);
            var key = "embed:" + index;
            var open = _embedOpen.Contains(key);
            var row = new VisualElement();
            row.AddToClassList("ux-fxrow");
            row.EnableInClassList("ux-fxrow--open", open);

            var head = new VisualElement();
            head.AddToClassList("ux-fxrow__head");
            head.Add(NewFlag(_embedLineFlags, index, _embedIssue));
            var name = new Label(string.IsNullOrEmpty(embed.RootPath) ? "(RootPath 未入力)" : embed.RootPath);
            name.AddToClassList("ux-fxrow__name");
            head.Add(name);
            var chip = new Label(embed.StartInactive ? "無効で始める" : "有効で始まる");
            chip.AddToClassList("ux-chip");
            chip.AddToClassList("ux-fxrow__chip");
            head.Add(chip);
            var summary = new Label(child != null ? $"→ {NameOf(child)}(子の中身は子のエディターで)" : "→ 子の CanvasData が未設定、または見つかりません");
            summary.AddToClassList("ux-dim");
            summary.AddToClassList("ux-fxrow__summary");
            summary.tooltip = summary.text;
            head.Add(summary);

            var edit = MakeButton("開く", "ux-btn--small", () => EditEmbedded(index), "編集対象をこの子の CanvasData に切り替える(「← 親へ戻る」で戻れる)");
            edit.SetEnabled(child != null);
            head.Add(edit);
            var toggle = new Button(() =>
            {
                if (!_embedOpen.Add(key))
                {
                    _embedOpen.Remove(key);
                }

                _embedList.Render();
            })
            { text = open ? "▾" : "⚙", tooltip = "RootPath・子の CanvasData・無効で始める・表示 / 非表示・削除" };
            toggle.AddToClassList("ux-btn");
            toggle.AddToClassList("ux-btn--icon");
            head.Add(toggle);
            row.Add(head);

            if (open)
            {
                row.Add(MakeEmbeddedDetails(owner, index, embed, child));
            }

            return row;
        }

        private VisualElement MakeEmbeddedDetails(CanvasData owner, int index, EmbeddedCanvas embed, CanvasData child)
        {
            var line = new VisualElement();
            line.AddToClassList("ux-wireline");
            line.AddToClassList("ux-fxrow__details");

            var pathField = new TextField { value = embed.RootPath ?? string.Empty, isDelayed = true, tooltip = "親 Prefab ルートからの相対パス(入れ子になっている子 Canvas のルート)。Enter か欄外クリックで確定" };
            pathField.AddToClassList("ux-wireline__field");
            pathField.RegisterValueChangedCallback(evt =>
            {
                if (owner == null || owner.EmbeddedCanvases == null || index >= owner.EmbeddedCanvases.Length)
                {
                    return;
                }

                // 確定時に正規化する(`\` → `/`、先頭・末尾の `/` を除く)。新しい配下の「親での上書き」の行を整理する(キャンセルしたら変更しない)。
                var newPath = (evt.newValue ?? string.Empty).Replace('\\', '/').Trim('/');
                var current = owner.EmbeddedCanvases[index];
                if (!CanvasEmbeddedEditing.ChangeEmbedWithCleanup(owner, index, newPath, Lookup.Find(current.Canvas), out var cleanup))
                {
                    if (cleanup.Cancelled)
                    {
                        pathField.SetValueWithoutNotify(evt.previousValue);
                        SetStatus("RootPath の変更をキャンセルしました(何も変更していません)");
                    }
                    else
                    {
                        pathField.SetValueWithoutNotify(newPath);
                    }

                    return;
                }

                if (owner == Target)
                {
                    AfterDataWrite();
                    var detail = cleanup.Describe();
                    if (!string.IsNullOrEmpty(detail))
                    {
                        SetStatus(detail);
                    }
                }
            });
            line.Add(pathField);

            var canvasField = new ObjectField { objectType = typeof(CanvasData), allowSceneObjects = false, tooltip = "子の CanvasData" };
            canvasField.AddToClassList("ux-wireline__field");
            canvasField.SetValueWithoutNotify(child);
            canvasField.RegisterValueChangedCallback(evt =>
            {
                if (owner == null || owner.EmbeddedCanvases == null || index >= owner.EmbeddedCanvases.Length)
                {
                    return;
                }

                var picked = evt.newValue as CanvasData;
                if (picked != null && picked.Id == 0)
                {
                    SetStatus("その CanvasData には Id がありません");
                    canvasField.SetValueWithoutNotify(evt.previousValue);
                    return;
                }

                // 子を選び直したら、その配下の「親での上書き」の行を整理する(キャンセルしたら変更しない)。
                if (!CanvasEmbeddedEditing.ChangeEmbedWithCleanup(owner, index, owner.EmbeddedCanvases[index].RootPath, picked, out var cleanup))
                {
                    if (cleanup.Cancelled)
                    {
                        canvasField.SetValueWithoutNotify(evt.previousValue);
                        SetStatus("子 CanvasData の変更をキャンセルしました(何も変更していません)");
                    }

                    return;
                }

                if (owner == Target)
                {
                    AfterDataWrite();
                    var detail = cleanup.Describe();
                    if (!string.IsNullOrEmpty(detail))
                    {
                        SetStatus(detail);
                    }
                }
            });
            line.Add(canvasField);

            var startInactive = new Toggle
            {
                text = "無効で始める",
                value = embed.StartInactive,
                tooltip = "親を開いたとき、この子を無効(非表示)で始める。Ui.SetEmbeddedActive か、ボタンの配線(ActivateEmbedded / ToggleEmbedded)で有効にすると、子の Appear → Idle が始まる。オフ = 従来どおり有効で始まる",
            };
            startInactive.AddToClassList("ux-bar__repeat");
            startInactive.RegisterValueChangedCallback(evt =>
            {
                if (owner == null || owner.EmbeddedCanvases == null || index >= owner.EmbeddedCanvases.Length)
                {
                    return;
                }

                Undo.RecordObject(owner, "Canvas: 埋め込みを無効で始める");
                var changed = owner.EmbeddedCanvases[index];
                changed.StartInactive = evt.newValue;
                owner.EmbeddedCanvases[index] = changed;
                EditorUtility.SetDirty(owner);
                if (owner == Target)
                {
                    AfterDataWrite();
                    SetStatus(evt.newValue
                        ? $"'{changed.RootPath}' は無効で始まります(確認用プレビューは開き直すと反映されます)"
                        : $"'{changed.RootPath}' は有効で始まります(確認用プレビューは開き直すと反映されます)");
                }
            });
            line.Add(startInactive);

            line.Add(MakeButton("表示 / 非表示(作業用)", "ux-btn--small", () => ToggleEmbedWorkVisibility(owner, index),
                "作業のために、この子の表示を一時的に切り替える(データにも Prefab にも保存しない)。プレハブモードを開いていればそちらを優先し、SceneView での表示だけを切り替える(もう一度押すか、Hierarchy の目のアイコンで戻る)。確認用プレビューだけのときは実際に有効 / 無効を切り替える(子の Appear / Disappear も再生される)"));
            line.Add(MakeButton("削除", "ux-btn--small", () =>
            {
                if (CanvasEmbeddedEditing.RemoveAt(Target, index))
                {
                    AfterDataWrite();
                }
            }, "この埋め込みの登録を外す(親の ElementFx の行は消えない。子の設定が親の中で効かなくなる)"));
            return line;
        }

        // 登録と同時に、その配線を指す親の ElementFx の行を整理する(既定のままの行は確認なしで取り除き、設定のある行は 1 回確認。1 つの Undo グループ)。
        private void RegisterEmbedded(string rootPath, CanvasData child)
        {
            if (CanvasEmbeddedEditing.RegisterWithCleanup(Target, rootPath, child, out var cleanup))
            {
                AfterDataWrite();
                var detail = cleanup.Describe();
                var head = $"'{rootPath}' を埋め込み Canvas(子 = {NameOf(child)})として登録しました";
                SetStatus(string.IsNullOrEmpty(detail) ? head : head + "。" + detail);
            }
            else if (cleanup.Cancelled)
            {
                SetStatus($"'{rootPath}' の登録をキャンセルしました(何も変更していません)");
            }
        }

        // 作業用の表示切り替え(保存しない)。確認用プレビュー: 実 UiManager の SetEmbeddedActive(演出つき)。
        // プレハブモード: SceneVisibilityManager(Prefab を汚さない)。両方開いていればプレハブモードを優先。
        private void ToggleEmbedWorkVisibility(CanvasData owner, int index)
        {
            if (owner != Target || owner.EmbeddedCanvases == null || index >= owner.EmbeddedCanvases.Length)
            {
                return;
            }

            var rootPath = owner.EmbeddedCanvases[index].RootPath;
            if (string.IsNullOrEmpty(rootPath))
            {
                SetStatus("RootPath が空です");
                return;
            }

            var stage = GetTargetStage();
            var displayed = FindPlaybackTarget(rootPath, out _);
            if (displayed == null)
            {
                SetStatus($"'{rootPath}' の表示中の実体がありません(プレハブモードで開くか、「確認用シーン」で表示してください)");
                return;
            }

            if (stage != null)
            {
                var visibility = SceneVisibilityManager.instance;
                var hidden = visibility.IsHidden(displayed.gameObject, false);
                if (hidden)
                {
                    visibility.Show(displayed.gameObject, true);
                }
                else
                {
                    visibility.Hide(displayed.gameObject, true);
                }

                SetStatus(hidden
                    ? $"'{rootPath}' を SceneView に表示しました(作業用。保存されません)"
                    : $"'{rootPath}' を SceneView で非表示にしました(作業用。保存されません。もう一度押すと戻ります)");
                return;
            }

            if (_manager != null && _manager.IsOpen(_previewHandle))
            {
                var previewRoot = _manager.GetGameObject(_previewHandle);
                var path = previewRoot != null ? TransformPath.GetRelative(previewRoot.transform, displayed) : null;
                if (!string.IsNullOrEmpty(path))
                {
                    var active = _manager.IsEmbeddedActive(_previewHandle, path);
                    _manager.SetEmbeddedActive(_previewHandle, path, !active);
                    SetStatus(active
                        ? $"'{rootPath}' を無効にしました(作業用。保存されません)"
                        : $"'{rootPath}' を有効にしました(作業用。保存されません)");
                    return;
                }
            }

            SetStatus($"'{rootPath}' を切り替えられませんでした(プレハブモードか確認用プレビューで表示してください)");
        }

        private void ApplyEmbedFilter(string query)
        {
            if (_embedList == null)
            {
                return;
            }

            _embedList.SetExternalQuery(query);
            if (!string.IsNullOrEmpty(query) && _embedList.AnyMatch(query))
            {
                _embedSection?.Open();
            }
        }
    }
}
